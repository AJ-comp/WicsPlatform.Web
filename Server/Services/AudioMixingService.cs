using ManagedBass;
using ManagedBass.Mix;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using WicsPlatform.Audio;
using WicsPlatform.Server.Contracts;
using WicsPlatform.Shared;  // MicConfig 사용

namespace WicsPlatform.Server.Services
{
    public class AudioMixingService : IAudioMixingService, IDisposable
    {
        private readonly ILogger<AudioMixingService> logger;
        private readonly IUdpBroadcastService udpService;
        private readonly OpusCodec opusCodec;
        private readonly ConcurrentDictionary<string, MixerSession> _sessions = new();
        private bool _bassInitialized = false;

        private class MixerSession
        {
            public string BroadcastId { get; set; }
            public int MixerStream { get; set; }
            public int MicPushStream { get; set; }
            public int MediaStream { get; set; }
            public int TtsStream { get; set; }
            public List<SpeakerInfo> Speakers { get; set; }
            public Timer OutputTimer { get; set; }
            public bool IsActive { get; set; }
            public MicConfig MicConfig { get; set; }
            public float MicVolume { get; set; } = 1.0f;
            public float MediaVolume { get; set; } = 0.7f;
            public float TtsVolume { get; set; } = 0.8f;
            public float MasterVolume { get; set; } = 1.0f;
        }

        public AudioMixingService(
            ILogger<AudioMixingService> logger,
            IUdpBroadcastService udpService,
            OpusCodec opusCodec)
        {
            this.logger = logger;
            this.udpService = udpService;
            this.opusCodec = opusCodec;

            InitializeBass();
        }

        private void InitializeBass()
        {
            try
            {
                // MicConfig 기본값으로 BASS 초기화
                var micConfig = new MicConfig();  // 16000Hz, 1채널

                if (!Bass.Init(-1, micConfig.SampleRate, DeviceInitFlags.Mono))
                {
                    logger.LogWarning($"Failed to initialize BASS: {Bass.LastError}");
                }
                else
                {
                    _bassInitialized = true;
                    logger.LogInformation($"BASS initialized with MicConfig: {micConfig.SampleRate}Hz");
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error initializing BASS");
            }
        }

        public async Task<bool> InitializeMixer(string broadcastId, List<SpeakerInfo> speakers)
        {
            if (!_bassInitialized)
            {
                logger.LogError("BASS not initialized");
                return false;
            }

            // 기존 세션이 있으면 정리
            if (_sessions.ContainsKey(broadcastId))
            {
                await StopMixer(broadcastId);
            }

            // MicConfig 생성 (항상 같은 설정)
            var micConfig = new MicConfig();  // 16000Hz, 1채널

            var session = new MixerSession
            {
                BroadcastId = broadcastId,
                Speakers = speakers,
                IsActive = true,
                MicConfig = micConfig
            };

            try
            {
                // MicConfig 설정 사용 (모노 고정)
                var flags = BassFlags.Decode | BassFlags.Float | BassFlags.MixerNonStop | BassFlags.Mono;

                // 1. 메인 믹서 생성 (MicConfig 설정 사용)
                session.MixerStream = BassMix.CreateMixerStream(
                    micConfig.SampleRate,  // 16000Hz
                    micConfig.Channels,    // 1 (모노)
                    flags
                );

                if (session.MixerStream == 0)
                {
                    logger.LogError($"Failed to create mixer: {Bass.LastError}");
                    return false;
                }

                // 2. 마이크용 Push 스트림 생성
                session.MicPushStream = Bass.CreateStream(
                    micConfig.SampleRate,  // 16000Hz
                    micConfig.Channels,    // 1 (모노)
                    BassFlags.Float | BassFlags.Decode,
                    StreamProcedureType.Push
                );

                if (session.MicPushStream == 0)
                {
                    logger.LogError($"Failed to create mic push stream: {Bass.LastError}");
                    Bass.StreamFree(session.MixerStream);
                    return false;
                }

                // 3. Push 스트림을 믹서에 연결
                if (!BassMix.MixerAddChannel(
                    session.MixerStream,
                    session.MicPushStream,
                    BassFlags.MixerChanNoRampin | BassFlags.MixerChanDownMix))
                {
                    logger.LogError($"Failed to add mic stream to mixer: {Bass.LastError}");
                    Bass.StreamFree(session.MixerStream);
                    Bass.StreamFree(session.MicPushStream);
                    return false;
                }

                // 4. 초기 볼륨 설정
                Bass.ChannelSetAttribute(session.MicPushStream, ChannelAttribute.Volume, session.MicVolume);

                // 5. 출력 타이머 시작 (MicConfig.TimesliceMs 사용)
                session.OutputTimer = new Timer(
                    async _ => await ProcessMixedOutput(broadcastId),
                    null,
                    TimeSpan.Zero,
                    TimeSpan.FromMilliseconds(micConfig.TimesliceMs)  // 60ms
                );

                _sessions[broadcastId] = session;

                logger.LogInformation($"Audio mixer initialized for broadcast: {broadcastId} " +
                    $"(MicConfig: {micConfig.SampleRate}Hz, {micConfig.Channels}ch, {micConfig.TimesliceMs}ms)");

                return true;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, $"Failed to initialize mixer for broadcast: {broadcastId}");

                // 정리
                if (session.MixerStream != 0) Bass.StreamFree(session.MixerStream);
                if (session.MicPushStream != 0) Bass.StreamFree(session.MicPushStream);

                return false;
            }
        }

        public async Task AddMicrophoneData(string broadcastId, byte[] pcmData)
        {
            if (!_sessions.TryGetValue(broadcastId, out var session) || !session.IsActive)
            {
                logger.LogWarning($"No active mixer session for broadcast: {broadcastId}");
                return;
            }

            try
            {
                // PCM Int16 → Float 변환
                var samples = pcmData.Length / 2;
                var floatData = new float[samples];

                for (int i = 0; i < samples; i++)
                {
                    var int16Sample = BitConverter.ToInt16(pcmData, i * 2);
                    floatData[i] = int16Sample / 32768f;
                }

                // Push 스트림에 데이터 추가
                var result = Bass.StreamPutData(
                    session.MicPushStream,
                    floatData,
                    floatData.Length * 4
                );

                if (result == -1)
                {
                    logger.LogWarning($"Failed to push mic data: {Bass.LastError}");
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error adding microphone data");
            }
        }

        private async Task ProcessMixedOutput(string broadcastId)
        {
            if (!_sessions.TryGetValue(broadcastId, out var session) || !session.IsActive)
                return;

            try
            {
                // MicConfig에서 샘플 수 계산 (60ms @ 16000Hz = 960 샘플)
                var samplesPerOutput = session.MicConfig.GetSamplesPerTimeslice();
                var floatBuffer = new float[samplesPerOutput * session.MicConfig.Channels];

                // 믹서에서 데이터 읽기
                var bytesRead = Bass.ChannelGetData(
                    session.MixerStream,
                    floatBuffer,
                    (int)DataFlags.Float | (floatBuffer.Length * 4)
                );

                if (bytesRead <= 0)
                    return;

                var samplesRead = bytesRead / 4;

                // Float → Int16 PCM 변환
                var pcm16 = new byte[samplesRead * 2];
                for (int i = 0; i < samplesRead; i++)
                {
                    // 마스터 볼륨 적용
                    var sample = floatBuffer[i] * session.MasterVolume;
                    sample = Math.Max(-1.0f, Math.Min(1.0f, sample));
                    var int16Sample = (short)(sample * 32767);

                    pcm16[i * 2] = (byte)(int16Sample & 0xFF);
                    pcm16[i * 2 + 1] = (byte)(int16Sample >> 8);
                }

                // ✅ PCM을 Opus로 압축
                var opusData = opusCodec.Encode(pcm16);

                // ✅ 압축된 Opus 데이터를 UDP로 전송
                await udpService.SendAudioToSpeakers(session.Speakers, opusData);

                // 디버깅용 로그 (선택사항)
                if (session.Speakers.Count > 0 && logger.IsEnabled(LogLevel.Debug))
                {
                    logger.LogDebug($"Broadcast {broadcastId}: PCM {pcm16.Length} bytes → Opus {opusData.Length} bytes (압축률: {(opusData.Length * 100.0 / pcm16.Length):F1}%)");
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, $"Error processing mixed output for broadcast: {broadcastId}");
            }
        }

        public async Task AddMediaStream(string broadcastId, string mediaPath)
        {
            if (!_sessions.TryGetValue(broadcastId, out var session) || !session.IsActive)
            {
                logger.LogWarning($"No active mixer session for broadcast: {broadcastId}");
                return;
            }

            try
            {
                // 기존 미디어 스트림 정리
                if (session.MediaStream != 0)
                {
                    BassMix.MixerRemoveChannel(session.MediaStream);
                    Bass.StreamFree(session.MediaStream);
                    session.MediaStream = 0;
                }

                // 새 미디어 스트림 생성
                var fullPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", mediaPath.TrimStart('/'));

                if (!File.Exists(fullPath))
                {
                    logger.LogError($"Media file not found: {fullPath}");
                    return;
                }

                session.MediaStream = Bass.CreateStream(
                    fullPath,
                    0, 0,
                    BassFlags.Decode | BassFlags.Float
                );

                if (session.MediaStream == 0)
                {
                    logger.LogError($"Failed to create media stream: {Bass.LastError}");
                    return;
                }

                // 믹서에 추가
                if (!BassMix.MixerAddChannel(
                    session.MixerStream,
                    session.MediaStream,
                    BassFlags.MixerChanNoRampin))
                {
                    logger.LogError($"Failed to add media to mixer: {Bass.LastError}");
                    Bass.StreamFree(session.MediaStream);
                    session.MediaStream = 0;
                    return;
                }

                // 볼륨 설정
                Bass.ChannelSetAttribute(session.MediaStream, ChannelAttribute.Volume, session.MediaVolume);

                logger.LogInformation($"Media stream added to mixer: {Path.GetFileName(mediaPath)}");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, $"Error adding media stream: {mediaPath}");
            }
        }

        public async Task RemoveMediaStream(string broadcastId)
        {
            if (!_sessions.TryGetValue(broadcastId, out var session))
                return;

            if (session.MediaStream != 0)
            {
                BassMix.MixerRemoveChannel(session.MediaStream);
                Bass.StreamFree(session.MediaStream);
                session.MediaStream = 0;

                logger.LogInformation($"Media stream removed from mixer for broadcast: {broadcastId}");
            }
        }

        public async Task SetVolume(string broadcastId, AudioSource source, float volume)
        {
            if (!_sessions.TryGetValue(broadcastId, out var session))
                return;

            volume = Math.Max(0, Math.Min(1, volume)); // 0-1 범위로 제한

            int targetStream = 0;

            switch (source)
            {
                case AudioSource.Microphone:
                    session.MicVolume = volume;
                    targetStream = session.MicPushStream;
                    break;

                case AudioSource.Media:
                    session.MediaVolume = volume;
                    targetStream = session.MediaStream;
                    break;

                case AudioSource.TTS:
                    session.TtsVolume = volume;
                    targetStream = session.TtsStream;
                    break;

                case AudioSource.Master:
                    session.MasterVolume = volume;
                    targetStream = session.MixerStream;
                    break;
            }

            if (targetStream != 0)
            {
                Bass.ChannelSetAttribute(targetStream, ChannelAttribute.Volume, volume);
                logger.LogDebug($"Volume set for {source}: {volume:F2}");
            }
        }

        public async Task<bool> StopMixer(string broadcastId)
        {
            if (_sessions.TryRemove(broadcastId, out var session))
            {
                session.IsActive = false;

                // 타이머 정지
                session.OutputTimer?.Dispose();

                // 스트림 정리
                if (session.MediaStream != 0)
                {
                    BassMix.MixerRemoveChannel(session.MediaStream);
                    Bass.StreamFree(session.MediaStream);
                }

                if (session.TtsStream != 0)
                {
                    BassMix.MixerRemoveChannel(session.TtsStream);
                    Bass.StreamFree(session.TtsStream);
                }

                if (session.MicPushStream != 0)
                {
                    Bass.StreamFree(session.MicPushStream);
                }

                if (session.MixerStream != 0)
                {
                    Bass.StreamFree(session.MixerStream);
                }

                logger.LogInformation($"Audio mixer stopped for broadcast: {broadcastId}");
                return true;
            }

            return false;
        }


        // 마이크 스트림만 제거하는 메서드
        public async Task RemoveMicrophoneStream(string broadcastId)
        {
            if (!_sessions.TryGetValue(broadcastId, out var session))
            {
                logger.LogWarning($"No session found for broadcast: {broadcastId}");
                return;
            }

            try
            {
                if (session.MicPushStream != 0)
                {
                    // 믹서에서 마이크 스트림 제거
                    BassMix.MixerRemoveChannel(session.MicPushStream);

                    // 스트림 해제
                    Bass.StreamFree(session.MicPushStream);
                    session.MicPushStream = 0;

                    logger.LogInformation($"Microphone stream removed for broadcast: {broadcastId}");
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, $"Error removing microphone stream for broadcast: {broadcastId}");
            }
        }

        // 활성 미디어 스트림이 있는지 확인
        public bool HasActiveMediaStream(string broadcastId)
        {
            if (!_sessions.TryGetValue(broadcastId, out var session))
                return false;

            return session.MediaStream != 0 && session.IsActive;
        }

        // 활성 TTS 스트림이 있는지 확인
        public bool HasActiveTtsStream(string broadcastId)
        {
            if (!_sessions.TryGetValue(broadcastId, out var session))
                return false;

            return session.TtsStream != 0 && session.IsActive;
        }

        // 마이크 스트림 재초기화 (재연결 시 사용)
        public async Task<bool> InitializeMicStream(string broadcastId)
        {
            if (!_sessions.TryGetValue(broadcastId, out var session))
            {
                logger.LogError($"No session found for broadcast: {broadcastId}");
                return false;
            }

            try
            {
                // 기존 마이크 스트림이 있으면 제거
                if (session.MicPushStream != 0)
                {
                    await RemoveMicrophoneStream(broadcastId);
                }

                // 새 Push 스트림 생성
                session.MicPushStream = Bass.CreateStream(
                    session.MicConfig.SampleRate,
                    session.MicConfig.Channels,
                    BassFlags.Float | BassFlags.Decode,
                    StreamProcedureType.Push
                );

                if (session.MicPushStream == 0)
                {
                    logger.LogError($"Failed to create mic push stream: {Bass.LastError}");
                    return false;
                }

                // 믹서에 추가
                if (!BassMix.MixerAddChannel(
                    session.MixerStream,
                    session.MicPushStream,
                    BassFlags.MixerChanNoRampin | BassFlags.MixerChanDownMix))
                {
                    logger.LogError($"Failed to add mic stream to mixer: {Bass.LastError}");
                    Bass.StreamFree(session.MicPushStream);
                    session.MicPushStream = 0;
                    return false;
                }

                // 볼륨 설정
                Bass.ChannelSetAttribute(session.MicPushStream, ChannelAttribute.Volume, session.MicVolume);

                logger.LogInformation($"Microphone stream re-initialized for broadcast: {broadcastId}");
                return true;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, $"Error initializing mic stream for broadcast: {broadcastId}");
                return false;
            }
        }



        public bool IsMixerActive(string broadcastId)
        {
            return _sessions.TryGetValue(broadcastId, out var session) && session.IsActive;
        }

        public void Dispose()
        {
            // 모든 믹서 세션 정리
            var tasks = _sessions.Keys.Select(id => StopMixer(id)).ToArray();
            Task.WaitAll(tasks);

            _sessions.Clear();

            if (_bassInitialized)
            {
                Bass.Free();
                logger.LogInformation("BASS freed in AudioMixingService");
            }
        }
    }
}