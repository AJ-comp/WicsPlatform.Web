using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using WicsPlatform.Server.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using WicsPlatform.Server.Data;
using System.Diagnostics;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using WicsPlatform.Audio; // MicConfig
using WicsPlatform.Shared;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace WicsPlatform.Server.Services;

static class MathUtil
{
    public static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
    public static float Clamp(float v, float min, float max) => v < min ? min : (v > max ? max : v);
}

sealed class AutoDetachSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _src;
    private readonly Action _onEndedOnce;
    private int _ended;
    public AutoDetachSampleProvider(ISampleProvider src, Action onEndedOnce) { _src = src; _onEndedOnce = onEndedOnce; }
    public WaveFormat WaveFormat => _src.WaveFormat;
    public int Read(float[] buffer, int offset, int count)
    {
        int n = _src.Read(buffer, offset, count);
        if (n == 0 && Interlocked.Exchange(ref _ended, 1) == 0)
        { try { _onEndedOnce?.Invoke(); } catch { } }
        return n;
    }
}

public class AudioMixingService : IAudioMixingService, IDisposable
{
    public event Action<ulong> OnMediaEnded; // ← 미디어 종료 이벤트

    private readonly ILogger<AudioMixingService> logger;
    private readonly IUdpBroadcastService udpService;
    private readonly IServiceScopeFactory scopeFactory;
    private readonly ConcurrentDictionary<ulong, MixerSession> _sessions = new();

    private class MainMixerConfig { public int SampleRate { get; set; } public int Channels { get; set; } public int TimesliceMs { get; set; } public int SamplesPerSlice() => (SampleRate * TimesliceMs) / 1000; }

    private sealed class TtsEntry
    {
        public int Id { get; set; }
        public AudioFileReader Reader { get; set; } = default!;
        public ISampleProvider InputToMixer { get; set; } = default!;
        public VolumeSampleProvider VolumeNode { get; set; } = default!;
        public int EndNearTicks { get; set; } = 0; // 종료 근접 휴리스틱 카운터
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
        public DateTime LastMicReceivedUtc { get; set; } = DateTime.MinValue;

        // 미디어 (파일 1개)
        public AudioFileReader? MediaReader { get; set; }
        public ISampleProvider? MediaToMixer { get; set; }
        public FadeInOutSampleProvider? MediaFadeNode { get; set; }
        public VolumeSampleProvider? MediaVolumeNode { get; set; }
        public float MediaVolume { get; set; } = 0.7f;
        public int MediaEndNearTicks { get; set; } = 0;

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

    private const int FIXED_TIMESLICE_MS = 60; // 요청 주기
    private const int MIC_PREFILL_MS = 100;    // 마이크 프리필

    public AudioMixingService(ILogger<AudioMixingService> logger, IUdpBroadcastService udpService, IServiceScopeFactory scopeFactory)
    { this.logger = logger; this.udpService = udpService; this.scopeFactory = scopeFactory; }

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
        if (_sessions.ContainsKey(broadcastId)) await StopMixer(broadcastId);
        var mixFormat = WaveFormat.CreateIeeeFloatWaveFormat(mixSampleRate, mixChannels);
        var mixer = new MixingSampleProvider(mixFormat) { ReadFully = true };
        var session = new MixerSession
        {
            ChannelId = channelId,
            Speakers = speakers,
            IsActive = true,
            MixerConfig = new MainMixerConfig { SampleRate = mixSampleRate, Channels = mixChannels, TimesliceMs = FIXED_TIMESLICE_MS },
            Mixer = mixer,
            MicInputSampleRate = micCfg.SampleRate,
            MicInputChannels = micCfg.Channels,
            OpusCodec = new OpusCodec(mixSampleRate, mixChannels, bitrate: 32000, logger)
        };
        session.OutputTimer = new Timer(async _ => await ProcessMixedOutput(broadcastId), null, TimeSpan.Zero, TimeSpan.FromMilliseconds(session.MixerConfig.TimesliceMs));
        _sessions[broadcastId] = session;
        logger.LogInformation($"[NAudio] Mixer initialized: mix={mixSampleRate}Hz/{mixChannels}ch");
        return true;
    }

    // 마이크 (PCM16 가정)
    public async Task AddMicrophoneData(ulong broadcastId, byte[] micBytes)
    {
        if (!_sessions.TryGetValue(broadcastId, out var s) || !s.IsActive) return;
        s.LastMicReceivedUtc = DateTime.UtcNow;
        if (s.MicBuffer == null)
        {
            var micFmt = new WaveFormat(s.MicInputSampleRate, s.MicInputChannels);
            s.MicBuffer = new BufferedWaveProvider(micFmt) { DiscardOnBufferOverflow = true, BufferDuration = TimeSpan.FromSeconds(2) };
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
            logger.LogInformation("[NAudio] Mic attached to mixer");
        }
    }

    // ===== 미디어: 메인 믹서에 연결 (리샘플/채널 매칭 후) =====
    public async Task AddMediaStream(ulong broadcastId, string mediaPath)
    {
        if (!_sessions.TryGetValue(broadcastId, out var s) || !s.IsActive) return;

        try
        {
            RemoveMediaStreamInternal(s);

            var full = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", mediaPath.TrimStart('/'));
            if (!File.Exists(full)) { logger.LogError($"Media not found: {full}"); return; }

            var reader = new AudioFileReader(full);
            ISampleProvider sp = reader;

            sp = EnsureSampleRate(sp, s.MixerConfig.SampleRate);
            sp = EnsureChannelMatch(sp, s.MixerConfig.Channels);

            var fade = new FadeInOutSampleProvider(sp, initiallySilent: true); fade.BeginFadeIn(20);
            var vol = new VolumeSampleProvider(fade) { Volume = s.MediaVolume };
            var cut = new OffsetSampleProvider(vol) { Take = reader.TotalTime };

            AutoDetachSampleProvider? final = null;
            final = new AutoDetachSampleProvider(
                cut,
                onEndedOnce: () =>
                {
                    try { lock (s.MixerLock) if (final != null) s.Mixer.RemoveMixerInput(final); } catch { } 
                    try { reader.Dispose(); } catch { } 
                    s.MediaReader = null;
                    s.MediaFadeNode = null;
                    s.MediaVolumeNode = null;
                    s.MediaToMixer = null;
                    s.MediaEndNearTicks = 0;
                    logger.LogInformation("[NAudio] Media ended → detached (auto)");
                    try { OnMediaEnded?.Invoke(broadcastId); } catch { }
                });

            s.MediaReader = reader;
            s.MediaFadeNode = fade;
            s.MediaVolumeNode = vol;
            s.MediaToMixer = final;
            s.MediaEndNearTicks = 0;

            lock (s.MixerLock) s.Mixer.AddMixerInput(s.MediaToMixer);

            logger.LogInformation($"[NAudio] Media added to mixer: {Path.GetFileName(mediaPath)}");
        }
        catch (Exception ex) { logger.LogError(ex, $"AddMediaStream error: {mediaPath}"); }
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
            if (s.MediaToMixer != null) s.Mixer.RemoveMixerInput(s.MediaToMixer);
        }
        try { s.MediaReader?.Dispose(); } catch { }
        s.MediaReader = null;
        s.MediaToMixer = null;
        s.MediaFadeNode = null;
        s.MediaVolumeNode = null;
        s.MediaEndNearTicks = 0;
    }

    // ===== TTS: EOF 자동 제거(미디어와 동일 패턴) =====
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

            var fade = new FadeInOutSampleProvider(sp, initiallySilent: true); fade.BeginFadeIn(10);
            var vol = new VolumeSampleProvider(fade) { Volume = s.TtsVolume };
            var cut = new OffsetSampleProvider(vol) { Take = reader.TotalTime };

            var id = s.NextTtsId++;

            AutoDetachSampleProvider? final = null;
            final = new AutoDetachSampleProvider(
                cut,
                onEndedOnce: () =>
                {
                    try { lock (s.MixerLock) if (final != null) s.Mixer.RemoveMixerInput(final); } catch { }
                    try { reader.Dispose(); } catch { }
                    s.TtsEntries.Remove(id);
                    logger.LogInformation($"[NAudio] TTS ended → detached (id={id})");
                });

            var entry = new TtsEntry
            {
                Id = id,
                Reader = reader,
                InputToMixer = final,
                VolumeNode = vol
            };

            lock (s.MixerLock) s.Mixer.AddMixerInput(entry.InputToMixer);
            s.TtsEntries[id] = entry;

            logger.LogInformation($"[NAudio] TTS added: {Path.GetFileName(ttsPath)} (id={id})");
            return id;
        }
        catch (Exception ex) { logger.LogError(ex, $"AddTtsStream error: {ttsPath}"); return 0; }
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
            foreach (var e in s.TtsEntries.Values) s.Mixer.RemoveMixerInput(e.InputToMixer);
        }
        foreach (var e in s.TtsEntries.Values) e.Reader.Dispose();
        s.TtsEntries.Clear();
    }

    public async Task UpdateCodecSettings(ulong broadcastId, int sampleRate, int channels, int bitrate)
    { if (_sessions.TryGetValue(broadcastId, out var s)) { s.OpusCodec?.UpdateSettings(sampleRate, channels, bitrate); logger.LogInformation($"Opus updated: {sampleRate}Hz, {channels}ch, {bitrate}bps"); } }

    public async Task SetVolume(ulong broadcastId, AudioSource source, float volume)
    {
        if (!_sessions.TryGetValue(broadcastId, out var s)) return;
        volume = MathUtil.Clamp01(volume);
        switch (source)
        {
            case AudioSource.Microphone: s.MicVolume = volume; if (s.MicVolumeNode != null) s.MicVolumeNode.Volume = volume; break;
            case AudioSource.Media: s.MediaVolume = volume; if (s.MediaVolumeNode != null) s.MediaVolumeNode.Volume = volume; break;
            case AudioSource.TTS: s.TtsVolume = volume; foreach (var e in s.TtsEntries.Values) e.VolumeNode.Volume = volume; break;
            case AudioSource.Master: s.MasterVolume = volume; break;
        }
    }

    // ====== 간단 플레이어 컨트롤 구현 ======
    public async Task<bool> SeekMediaAsync(ulong broadcastId, double seconds)
    {
        if (!_sessions.TryGetValue(broadcastId, out var s) || s.MediaReader == null) return false;
        try
        {
            var r = s.MediaReader;
            var target = r.CurrentTime + TimeSpan.FromSeconds(seconds);
            if (target < TimeSpan.Zero) target = TimeSpan.Zero;
            if (target > r.TotalTime) target = r.TotalTime - TimeSpan.FromMilliseconds(10);
            r.CurrentTime = target;
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, $"SeekMediaAsync failed for {broadcastId}");
            return false;
        }
    }

    public (TimeSpan current, TimeSpan total) GetMediaTimes(ulong broadcastId)
    {
        if (_sessions.TryGetValue(broadcastId, out var s) && s.MediaReader != null)
        {
            return (s.MediaReader.CurrentTime, s.MediaReader.TotalTime);
        }
        return (TimeSpan.Zero, TimeSpan.Zero);
    }

    private async Task ProcessMixedOutput(ulong broadcastId)
    {
        if (!_sessions.TryGetValue(broadcastId, out var s) || !s.IsActive) return;
        try
        {
            // Media 종료 휴리스틱
            if (s.MediaToMixer != null && s.MediaReader != null && s.MediaReader.TotalTime.TotalMilliseconds > 0)
            {
                var remainingMs = (s.MediaReader.TotalTime - s.MediaReader.CurrentTime).TotalMilliseconds;
                if (remainingMs <= 80)
                {
                    if (++s.MediaEndNearTicks >= 5)
                    {
                        logger.LogInformation($"[NAudio][Heuristic] Force end media (remain {remainingMs:F1}ms) broadcast={broadcastId}");
                        RemoveMediaStreamInternal(s);
                        try { OnMediaEnded?.Invoke(broadcastId); } catch { }
                    }
                }
                else s.MediaEndNearTicks = 0;
            }

            // TTS 종료 휴리스틱
            if (s.TtsEntries.Count > 0)
            {
                List<int>? forceRemove = null;
                foreach (var kv in s.TtsEntries.ToList())
                {
                    var e = kv.Value;
                    if (e.Reader.TotalTime.TotalMilliseconds <= 0) continue;
                    var remain = (e.Reader.TotalTime - e.Reader.CurrentTime).TotalMilliseconds;
                    if (remain <= 80)
                    {
                        e.EndNearTicks++;
                        if (e.EndNearTicks >= 5)
                        {
                            (forceRemove ??= new List<int>()).Add(kv.Key);
                        }
                    }
                    else if (e.EndNearTicks > 0) e.EndNearTicks = 0;
                }
                if (forceRemove != null)
                {
                    foreach (var id in forceRemove)
                    {
                        if (s.TtsEntries.TryGetValue(id, out var e))
                        {
                            logger.LogInformation($"[NAudio][Heuristic] Force end TTS id={id} broadcast={broadcastId}");
                            lock (s.MixerLock) s.Mixer.RemoveMixerInput(e.InputToMixer);
                            try { e.Reader.Dispose(); } catch { }
                            s.TtsEntries.Remove(id);
                        }
                    }
                }
            }

            // 마이크 활성 상태 확인 및 필요 시 분리
            var now = DateTime.UtcNow;
            var micRecentMs = (now - s.LastMicReceivedUtc).TotalMilliseconds;
            var micHasRecent = s.MicToMixer != null && micRecentMs <= 1000;
            if (s.MicToMixer != null && !micHasRecent && (s.MicBuffer == null || s.MicBuffer.BufferedDuration.TotalMilliseconds <= 0))
            {
                lock (s.MixerLock)
                {
                    s.Mixer.RemoveMixerInput(s.MicToMixer);
                }
                s.MicToMixer = null;
                s.MicBuffer = null;
                s.MicVolumeNode = null;
                s.MicAttachedToMixer = false;
                logger.LogInformation($"[NAudio] Mic auto-detached (inactive) for {broadcastId}");
            }

            bool hasMic = s.MicToMixer != null && micHasRecent;
            bool hasMedia = s.MediaToMixer != null;
            bool hasTts = s.TtsEntries.Count > 0;
            if (!(hasMic || hasMedia || hasTts)) return;

            int frames = s.MixerConfig.SamplesPerSlice();
            int samplesNeeded = frames * s.MixerConfig.Channels;
            var floatBuf = new float[samplesNeeded];
            int read = s.Mixer.Read(floatBuf, 0, samplesNeeded);
            if (read <= 0) return;

            // 무음 프레임 전송 스킵
            double energy = 0;
            for (int i = 0; i < read; i += Math.Max(1, read / 256))
            {
                var v = floatBuf[i];
                energy += v * v;
            }
            if (energy < 1e-6) return;

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
            await udpService.SendAudioToSpeakers(s.Speakers.Where(sp => sp.Active), opus);
        }
        catch (Exception ex) { logger.LogError(ex, $"ProcessMixedOutput error for {broadcastId}"); }
    }

    public bool HasActiveMediaStream(ulong id) =>
        _sessions.TryGetValue(id, out var s) && s.IsActive && s.MediaToMixer != null && s.MediaReader != null &&
        (s.MediaReader.CurrentTime < s.MediaReader.TotalTime - TimeSpan.FromMilliseconds(50));

    public bool HasActiveTtsStream(ulong id)
    {
        if (!_sessions.TryGetValue(id, out var s) || !s.IsActive) return false;
        foreach (var kv in s.TtsEntries)
        {
            var r = kv.Value.Reader;
            if (r.CurrentTime < r.TotalTime - TimeSpan.FromMilliseconds(50)) return true;
        }
        return false;
    }

    public async Task<bool> InitializeMicStream(ulong broadcastId) => true;
    public bool IsMixerActive(ulong broadcastId) => _sessions.TryGetValue(broadcastId, out var s) && s.IsActive;

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
            s.LastMicReceivedUtc = DateTime.MinValue;
            logger.LogInformation($"Mic removed for {broadcastId}");
        }
    }

    public async Task<bool> StopMixer(ulong broadcastId)
    {
        if (_sessions.TryRemove(broadcastId, out var s))
        {
            s.IsActive = false; s.OutputTimer?.Dispose();
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
        Task.WaitAll(tasks); _sessions.Clear();
    }

    private static ISampleProvider EnsureSampleRate(ISampleProvider sp, int targetRate) =>
        sp.WaveFormat.SampleRate == targetRate ? sp : new WdlResamplingSampleProvider(sp, targetRate);

    private static ISampleProvider EnsureChannelMatch(ISampleProvider sp, int targetChannels)
    {
        if (sp.WaveFormat.Channels == targetChannels) return sp;
        if (targetChannels == 1 && sp.WaveFormat.Channels == 2)
            return new StereoToMonoSampleProvider(sp) { LeftVolume = 0.5f, RightVolume = 0.5f };
        if (targetChannels == 2 && sp.WaveFormat.Channels == 1)
            return new MonoToStereoSampleProvider(sp);
        if (sp.WaveFormat.Channels > 2 && targetChannels == 2)
        {
            var mpx = new MultiplexingSampleProvider(new[] { sp }, 2);
            mpx.ConnectInputToOutput(0, 0); mpx.ConnectInputToOutput(1, 1); return mpx;
        }
        throw new NotSupportedException($"Channel mapping {sp.WaveFormat.Channels} -> {targetChannels} not supported.");
    }
}
