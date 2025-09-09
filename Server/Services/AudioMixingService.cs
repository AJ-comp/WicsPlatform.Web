using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using WicsPlatform.Server.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using WicsPlatform.Server.Data;
using System.Diagnostics;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using WicsPlatform.Audio; // MicConfig (실제 위치에 맞게 유지)
using WicsPlatform.Shared;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace WicsPlatform.Server.Services
{
    // 간단 유틸(타깃 프레임워크에 Clamp 없음 대비)
    static class MathUtil
    {
        public static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
        public static float Clamp(float v, float min, float max) => v < min ? min : (v > max ? max : v);
    }

    /// <summary>
    /// ISampleProvider가 EOF(더 이상 읽을 샘플 없음)에 도달했을 때 1회 콜백을 발생시키는 래퍼
    /// </summary>
    sealed class EndNotifyingSampleProvider : ISampleProvider
    {
        private readonly ISampleProvider source;
        private bool ended;
        public event Action? OnEnded;
        public EndNotifyingSampleProvider(ISampleProvider source) { this.source = source; }
        public WaveFormat WaveFormat => source.WaveFormat;
        public int Read(float[] buffer, int offset, int count)
        {
            int n = source.Read(buffer, offset, count);
            if (n == 0 && !ended)
            {
                ended = true;
                try { OnEnded?.Invoke(); } catch { /* swallow */ }
            }
            return n;
        }
    }

    public class AudioMixingService : IAudioMixingService, IDisposable
    {
        private readonly ILogger<AudioMixingService> logger;
        private readonly IUdpBroadcastService udpService;
        private readonly IServiceScopeFactory scopeFactory;
        private readonly ConcurrentDictionary<ulong, MixerSession> _sessions = new();

        private class MainMixerConfig
        {
            public int SampleRate { get; set; }
            public int Channels { get; set; }
            public int TimesliceMs { get; set; }   // 60ms 고정
            public int SamplesPerSlice() => (SampleRate * TimesliceMs) / 1000;
        }

        private sealed class TtsEntry
        {
            public int Id { get; set; }
            public AudioFileReader Reader { get; set; } = default!;
            public ISampleProvider InputToMixer { get; set; } = default!;
            public VolumeSampleProvider VolumeNode { get; set; } = default!;
        }

        private sealed class MixerSession : IDisposable
        {
            public ulong ChannelId { get; set; }
            public List<SpeakerInfo> Speakers { get; set; } = new();
            public OpusCodec OpusCodec { get; set; } = default!;
            public MainMixerConfig MixerConfig { get; set; } = default!;
            public Timer OutputTimer { get; set; } = default!;
            public bool IsActive { get; set; }

            // 메인 믹서
            public MixingSampleProvider Mixer { get; set; } = default!;
            public object MixerLock { get; } = new();

            // 마이크
            public int MicInputSampleRate { get; set; }
            public int MicInputChannels { get; set; }
            public BufferedWaveProvider? MicBuffer { get; set; }
            public ISampleProvider? MicToMixer { get; set; }
            public VolumeSampleProvider? MicVolumeNode { get; set; }
            public bool MicAttachedToMixer { get; set; } = false;
            public float MicVolume { get; set; } = 1.0f;

            // 미디어 (파일 1개)
            public AudioFileReader? MediaReader { get; set; }
            public ISampleProvider? MediaToMixer { get; set; }
            public FadeInOutSampleProvider? MediaFadeNode { get; set; }
            public VolumeSampleProvider? MediaVolumeNode { get; set; }
            public EndNotifyingSampleProvider? MediaEndNode { get; set; }
            public volatile bool MediaPendingRemove;
            public float MediaVolume { get; set; } = 0.7f;

            // TTS (여러 개)
            public Dictionary<int, TtsEntry> TtsEntries { get; } = new();
            public float TtsVolume { get; set; } = 0.8f;
            public int NextTtsId { get; set; } = 1;

            // 마스터
            public float MasterVolume { get; set; } = 1.0f;

            public void Dispose()
            {
                MediaReader?.Dispose();
                foreach (var kv in TtsEntries) kv.Value.Reader.Dispose();
            }
        }

        private const int FIXED_TIMESLICE_MS = 60;    // 요청 주기
        private const int MIC_PREFILL_MS = 100;   // 마이크 프리필

        public AudioMixingService(
            ILogger<AudioMixingService> logger,
            IUdpBroadcastService udpService,
            IServiceScopeFactory scopeFactory)
        {
            this.logger = logger;
            this.udpService = udpService;
            this.scopeFactory = scopeFactory;
        }

        // 초기화
        public async Task<bool> InitializeMixer(ulong broadcastId, ulong channelId, List<SpeakerInfo> speakers)
        {
            int mixSampleRate = 16000; byte mixChannels = 1;
            using (var scope = scopeFactory.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<wicsContext>();
                var ch = await db.Channels.FirstOrDefaultAsync(c => c.Id == channelId);
                if (ch != null) { mixSampleRate = (int)ch.SamplingRate; mixChannels = ch.ChannelCount; }
                else logger.LogWarning($"Channel not found in DB for broadcast: {broadcastId}, using defaults");
            }

            var micCfg = new MicConfig();

            if (_sessions.ContainsKey(broadcastId))
                await StopMixer(broadcastId);

            var mixFormat = WaveFormat.CreateIeeeFloatWaveFormat(mixSampleRate, mixChannels);
            var mixer = new MixingSampleProvider(mixFormat) { ReadFully = true };

            var session = new MixerSession
            {
                ChannelId = channelId,
                Speakers = speakers,
                IsActive = true,
                MixerConfig = new MainMixerConfig
                {
                    SampleRate = mixSampleRate,
                    Channels = mixChannels,
                    TimesliceMs = FIXED_TIMESLICE_MS
                },
                Mixer = mixer,
                MicInputSampleRate = micCfg.SampleRate,
                MicInputChannels = micCfg.Channels,
                OpusCodec = new OpusCodec(mixSampleRate, mixChannels, bitrate: 32000, logger),
            };

            session.OutputTimer = new Timer(async _ => await ProcessMixedOutput(broadcastId),
                                            null,
                                            TimeSpan.Zero,
                                            TimeSpan.FromMilliseconds(session.MixerConfig.TimesliceMs));

            _sessions[broadcastId] = session;
            logger.LogInformation($"[NAudio] Mixer initialized: mix={mixSampleRate}Hz/{mixChannels}ch, micIn={session.MicInputSampleRate}Hz/{session.MicInputChannels}ch, slice={session.MixerConfig.TimesliceMs}ms");
            return true;
        }

        // 마이크 (PCM16 가정)
        public async Task AddMicrophoneData(ulong broadcastId, byte[] micBytes)
        {
            if (!_sessions.TryGetValue(broadcastId, out var s) || !s.IsActive) return;

            if (s.MicBuffer == null)
            {
                var micFmt = new WaveFormat(s.MicInputSampleRate, s.MicInputChannels);
                s.MicBuffer = new BufferedWaveProvider(micFmt)
                {
                    DiscardOnBufferOverflow = true,
                    BufferDuration = TimeSpan.FromSeconds(2)
                };
            }

            s.MicBuffer.AddSamples(micBytes, 0, micBytes.Length);

            if (!s.MicAttachedToMixer && s.MicBuffer.BufferedDuration.TotalMilliseconds >= MIC_PREFILL_MS)
            {
                ISampleProvider sp = s.MicBuffer.ToSampleProvider();
                sp = EnsureSampleRate(sp, s.MixerConfig.SampleRate);
                sp = EnsureChannelMatch(sp, s.MixerConfig.Channels);
                s.MicVolumeNode = new VolumeSampleProvider(sp) { Volume = s.MicVolume };
                s.MicToMixer = s.MicVolumeNode;

                lock (s.MixerLock) s.Mixer.AddMixerInput(s.MicToMixer);
                s.MicAttachedToMixer = true;
                logger.LogInformation($"[NAudio] Mic attached after prefill ~{MIC_PREFILL_MS}ms (buffered={s.MicBuffer.BufferedDuration.TotalMilliseconds:F0}ms)");
            }
        }

        // ===== 미디어: 메인 믹서에 연결 (리샘플/채널 매칭 후) =====
        public async Task AddMediaStream(ulong broadcastId, string mediaPath)
        {
            if (!_sessions.TryGetValue(broadcastId, out var s) || !s.IsActive) return;

            try
            {
                RemoveMediaStreamInternal(s); // 기존 미디어 정리

                var full = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", mediaPath.TrimStart('/'));
                if (!File.Exists(full)) { logger.LogError($"Media not found: {full}"); return; }

                var reader = new AudioFileReader(full);      // float32, 파일 SR/채널
                ISampleProvider sp = reader;

                // 믹서 포맷으로 정규화
                sp = EnsureSampleRate(sp, s.MixerConfig.SampleRate);
                sp = EnsureChannelMatch(sp, s.MixerConfig.Channels);

                // 짧은 페이드인(클릭 방지)
                var fade = new FadeInOutSampleProvider(sp, initiallySilent: true);
                fade.BeginFadeIn(20); // 20ms

                // 볼륨 노드
                var vol = new VolumeSampleProvider(fade) { Volume = s.MediaVolume };

                // EOF 감지 → 세션 플래그로 제거 예약
                var end = new EndNotifyingSampleProvider(vol);
                end.OnEnded += () => { s.MediaPendingRemove = true; };

                s.MediaReader = reader;
                s.MediaFadeNode = fade;
                s.MediaVolumeNode = vol;
                s.MediaEndNode = end;
                s.MediaToMixer = end;

                lock (s.MixerLock) s.Mixer.AddMixerInput(s.MediaToMixer);
                logger.LogInformation($"[NAudio] Media added to mixer: {Path.GetFileName(mediaPath)}");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, $"AddMediaStream error: {mediaPath}");
            }
        }

        public async Task RemoveMediaStream(ulong broadcastId)
        {
            if (!_sessions.TryGetValue(broadcastId, out var s)) return;
            RemoveMediaStreamInternal(s);
        }

        private void RemoveMediaStreamInternal(MixerSession s)
        {
            lock (s.MixerLock)
            {
                if (s.MediaToMixer != null)
                    s.Mixer.RemoveMixerInput(s.MediaToMixer);
            }
            s.MediaReader?.Dispose();
            s.MediaReader = null;
            s.MediaToMixer = null;
            s.MediaFadeNode = null;
            s.MediaVolumeNode = null;
            s.MediaEndNode = null;
            s.MediaPendingRemove = false;
        }

        // ===== TTS: EOF 자동 제거 추가 =====
        public async Task<int> AddTtsStream(ulong broadcastId, string ttsPath)
        {
            if (!_sessions.TryGetValue(broadcastId, out var s) || !s.IsActive) return 0;

            try
            {
                var full = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", ttsPath.TrimStart('/'));
                if (!File.Exists(full)) { logger.LogError($"TTS not found: {full}"); return 0; }

                var reader = new AudioFileReader(full);
                ISampleProvider sp = reader;

                sp = EnsureSampleRate(sp, s.MixerConfig.SampleRate);
                sp = EnsureChannelMatch(sp, s.MixerConfig.Channels);

                // (옵션) 짧은 페이드인
                var fade = new FadeInOutSampleProvider(sp, initiallySilent: true);
                fade.BeginFadeIn(10);

                var vol = new VolumeSampleProvider(fade) { Volume = s.TtsVolume };

                // EOF 자동 제거 래퍼
                var end = new EndNotifyingSampleProvider(vol);

                var id = s.NextTtsId++;

                end.OnEnded += () =>
                {
                    // 재생 끝나면 믹서에서 제거 + 리더 dispose + 사전에서 제거
                    lock (s.MixerLock) s.Mixer.RemoveMixerInput(end);
                    reader.Dispose();
                    s.TtsEntries.Remove(id);
                    logger.LogInformation($"[NAudio] TTS auto-removed after EOF (id={id})");
                };

                var entry = new TtsEntry { Id = id, Reader = reader, InputToMixer = end, VolumeNode = vol };

                lock (s.MixerLock) s.Mixer.AddMixerInput(entry.InputToMixer);

                s.TtsEntries[id] = entry;
                logger.LogInformation($"[NAudio] TTS added: {Path.GetFileName(ttsPath)} (id={id})");
                return id;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, $"AddTtsStream error: {ttsPath}");
                return 0;
            }
        }

        public async Task RemoveTtsStream(ulong broadcastId, int ttsStreamId)
        {
            if (!_sessions.TryGetValue(broadcastId, out var s)) return;
            if (!s.TtsEntries.TryGetValue(ttsStreamId, out var e)) return;

            lock (s.MixerLock) s.Mixer.RemoveMixerInput(e.InputToMixer);
            e.Reader.Dispose();
            s.TtsEntries.Remove(ttsStreamId);
        }

        public async Task RemoveAllTtsStreams(ulong broadcastId)
        {
            if (!_sessions.TryGetValue(broadcastId, out var s)) return;

            lock (s.MixerLock)
            {
                foreach (var e in s.TtsEntries.Values)
                    s.Mixer.RemoveMixerInput(e.InputToMixer);
            }
            foreach (var e in s.TtsEntries.Values) e.Reader.Dispose();
            s.TtsEntries.Clear();
        }

        public async Task UpdateCodecSettings(ulong broadcastId, int sampleRate, int channels, int bitrate)
        {
            if (_sessions.TryGetValue(broadcastId, out var s))
            {
                s.OpusCodec?.UpdateSettings(sampleRate, channels, bitrate);
                logger.LogInformation($"Opus updated: {sampleRate}Hz, {channels}ch, {bitrate}bps");
            }
        }

        public async Task SetVolume(ulong broadcastId, AudioSource source, float volume)
        {
            if (!_sessions.TryGetValue(broadcastId, out var s)) return;
            volume = MathUtil.Clamp01(volume);

            switch (source)
            {
                case AudioSource.Microphone:
                    s.MicVolume = volume;
                    if (s.MicVolumeNode != null) s.MicVolumeNode.Volume = volume;
                    break;
                case AudioSource.Media:
                    s.MediaVolume = volume;
                    if (s.MediaVolumeNode != null) s.MediaVolumeNode.Volume = volume;
                    break;
                case AudioSource.TTS:
                    s.TtsVolume = volume;
                    foreach (var e in s.TtsEntries.Values) e.VolumeNode.Volume = volume;
                    break;
                case AudioSource.Master:
                    s.MasterVolume = volume;
                    break;
            }
            logger.LogDebug($"Volume set for {source}: {volume:F2}");
        }

        // 60ms Pull → Opus → UDP, 그리고 미디어 EOF 정리
        private async Task ProcessMixedOutput(ulong broadcastId)
        {
            if (!_sessions.TryGetValue(broadcastId, out var s) || !s.IsActive) return;

            try
            {
                int frames = s.MixerConfig.SamplesPerSlice();
                int samplesNeeded = frames * s.MixerConfig.Channels;

                var floatBuf = new float[samplesNeeded];
                int read = s.Mixer.Read(floatBuf, 0, samplesNeeded);
                if (read <= 0) return;

                var pcm16 = new byte[read * 2];
                for (int i = 0; i < read; i++)
                {
                    float sample = floatBuf[i] * s.MasterVolume;
                    sample = MathUtil.Clamp(sample, -1f, 1f);
                    short i16 = (short)(sample * 32767f);
                    pcm16[2 * i] = (byte)(i16 & 0xFF);
                    pcm16[2 * i + 1] = (byte)((i16 >> 8) & 0xFF);
                }

                var opus = s.OpusCodec.Encode(pcm16);
                await udpService.SendAudioToSpeakers(s.Speakers, opus);

                // 미디어가 끝났다면 안전하게 제거
                if (s.MediaPendingRemove)
                {
                    RemoveMediaStreamInternal(s);
                    logger.LogInformation("[NAudio] Media auto-removed after EOF");
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, $"ProcessMixedOutput error for {broadcastId}");
            }
        }

        public bool HasActiveMediaStream(ulong id) =>
            _sessions.TryGetValue(id, out var s) && s.MediaReader != null && s.IsActive;

        public bool HasActiveTtsStream(ulong id) =>
            _sessions.TryGetValue(id, out var s) && s.TtsEntries.Count > 0 && s.IsActive;

        public async Task<bool> InitializeMicStream(ulong broadcastId) => true;

        public bool IsMixerActive(ulong broadcastId) =>
            _sessions.TryGetValue(broadcastId, out var s) && s.IsActive;

        public async Task RemoveMicrophoneStream(ulong broadcastId)
        {
            if (!_sessions.TryGetValue(broadcastId, out var s)) return;

            if (s.MicToMixer != null)
            {
                lock (s.MixerLock) s.Mixer.RemoveMixerInput(s.MicToMixer);
                s.MicToMixer = null;
                s.MicBuffer = null;
                s.MicVolumeNode = null;
                s.MicAttachedToMixer = false;
                logger.LogInformation($"Mic removed for {broadcastId}");
            }
        }

        public async Task<bool> StopMixer(ulong broadcastId)
        {
            if (_sessions.TryRemove(broadcastId, out var s))
            {
                s.IsActive = false;
                s.OutputTimer?.Dispose();

                lock (s.MixerLock)
                {
                    if (s.MicToMixer != null) s.Mixer.RemoveMixerInput(s.MicToMixer);
                    if (s.MediaToMixer != null) s.Mixer.RemoveMixerInput(s.MediaToMixer);
                    foreach (var e in s.TtsEntries.Values) s.Mixer.RemoveMixerInput(e.InputToMixer);
                }
                s.Dispose();
                logger.LogInformation($"[NAudio] Mixer stopped for {broadcastId}");
                return true;
            }
            return false;
        }

        public void Dispose()
        {
            var tasks = _sessions.Keys.Select(id => StopMixer(id)).ToArray();
            Task.WaitAll(tasks);
            _sessions.Clear();
        }

        // 리샘플/채널 매칭
        private static ISampleProvider EnsureSampleRate(ISampleProvider sp, int targetRate)
        {
            if (sp.WaveFormat.SampleRate == targetRate) return sp;
            return new WdlResamplingSampleProvider(sp, targetRate);
        }

        private static ISampleProvider EnsureChannelMatch(ISampleProvider sp, int targetChannels)
        {
            if (sp.WaveFormat.Channels == targetChannels) return sp;

            if (targetChannels == 1 && sp.WaveFormat.Channels == 2)
                return new StereoToMonoSampleProvider(sp) { LeftVolume = 0.5f, RightVolume = 0.5f };

            if (targetChannels == 2 && sp.WaveFormat.Channels == 1)
                return new MonoToStereoSampleProvider(sp);

            if (sp.WaveFormat.Channels > 2 && targetChannels == 2)
            {
                // 간단 다운믹스: 입력의 앞 2채널을 L/R에 매핑 (일반적인 기본값)
                var mpx = new MultiplexingSampleProvider(new[] { sp }, 2);
                mpx.ConnectInputToOutput(0, 0); // L
                mpx.ConnectInputToOutput(1, 1); // R
                return mpx;
            }

            throw new NotSupportedException($"Channel mapping {sp.WaveFormat.Channels} -> {targetChannels} not supported.");
        }
    }
}
