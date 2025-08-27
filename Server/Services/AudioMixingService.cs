using ManagedBass;
using ManagedBass.Mix;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using WicsPlatform.Audio;
using WicsPlatform.Server.Contracts;
using WicsPlatform.Shared;

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
            public List<int> TtsStreams { get; set; } = new(); // TTS는 여러 개 가능
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
                var micConfig = new MicConfig();

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

            if (_sessions.ContainsKey(broadcastId))
            {
                await StopMixer(broadcastId);
            }

            var micConfig = new MicConfig();

            var session = new MixerSession
            {
                BroadcastId = broadcastId,
                Speakers = speakers,
                IsActive = true,
                MicConfig = micConfig
            };

            try
            {
                var flags = BassFlags.Decode | BassFlags.Float | BassFlags.MixerNonStop | BassFlags.Mono;

                // 1. 메인 믹서 생성
                session.MixerStream = BassMix.CreateMixerStream(
                    micConfig.SampleRate,
                    micConfig.Channels,
                    flags
                );

                if (session.MixerStream == 0)
                {
                    logger.LogError($"Failed to create mixer: {Bass.LastError}");
                    return false;
                }

                // 2. 마이크용 Push 스트림 생성
                session.MicPushStream = Bass.CreateStream(
                    micConfig.SampleRate,
                    micConfig.Channels,
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

                // 5. 출력 타이머 시작
                session.OutputTimer = new Timer(
                    async _ => await ProcessMixedOutput(broadcastId),
                    null,
                    TimeSpan.Zero,
                    TimeSpan.FromMilliseconds(micConfig.TimesliceMs)
                );

                _sessions[broadcastId] = session;

                logger.LogInformation($"Audio mixer initialized for broadcast: {broadcastId} " +
                    $"(MicConfig: {micConfig.SampleRate}Hz, {micConfig.Channels}ch, {micConfig.TimesliceMs}ms)");

                return true;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, $"Failed to initialize mixer for broadcast: {broadcastId}");

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
                var samples = pcmData.Length / 2;
                var floatData = new float[samples];

                for (int i = 0; i < samples; i++)
                {
                    var int16Sample = BitConverter.ToInt16(pcmData, i * 2);
                    floatData[i] = int16Sample / 32768f;
                }

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
                var samplesPerOutput = session.MicConfig.GetSamplesPerTimeslice();
                var floatBuffer = new float[samplesPerOutput * session.MicConfig.Channels];

                var bytesRead = Bass.ChannelGetData(
                    session.MixerStream,
                    floatBuffer,
                    (int)DataFlags.Float | (floatBuffer.Length * 4)
                );

                if (bytesRead <= 0)
                    return;

                var samplesRead = bytesRead / 4;

                var pcm16 = new byte[samplesRead * 2];
                for (int i = 0; i < samplesRead; i++)
                {
                    var sample = floatBuffer[i] * session.MasterVolume;
                    sample = Math.Max(-1.0f, Math.Min(1.0f, sample));
                    var int16Sample = (short)(sample * 32767);

                    pcm16[i * 2] = (byte)(int16Sample & 0xFF);
                    pcm16[i * 2 + 1] = (byte)(int16Sample >> 8);
                }

                var opusData = opusCodec.Encode(pcm16);
                await udpService.SendAudioToSpeakers(session.Speakers, opusData);
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

        // ★ TTS 스트림 추가 메서드 (새로 추가)
        public async Task<int> AddTtsStream(string broadcastId, string ttsPath)
        {
            if (!_sessions.TryGetValue(broadcastId, out var session) || !session.IsActive)
            {
                logger.LogWarning($"No active mixer session for broadcast: {broadcastId}");
                return 0;
            }

            try
            {
                var fullPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", ttsPath.TrimStart('/'));

                if (!File.Exists(fullPath))
                {
                    logger.LogError($"TTS file not found: {fullPath}");
                    return 0;
                }

                var ttsStream = Bass.CreateStream(
                    fullPath,
                    0, 0,
                    BassFlags.Decode | BassFlags.Float
                );

                if (ttsStream == 0)
                {
                    logger.LogError($"Failed to create TTS stream: {Bass.LastError}");
                    return 0;
                }

                if (!BassMix.MixerAddChannel(
                    session.MixerStream,
                    ttsStream,
                    BassFlags.MixerChanNoRampin))
                {
                    logger.LogError($"Failed to add TTS to mixer: {Bass.LastError}");
                    Bass.StreamFree(ttsStream);
                    return 0;
                }

                // TTS 볼륨 설정
                Bass.ChannelSetAttribute(ttsStream, ChannelAttribute.Volume, session.TtsVolume);

                session.TtsStreams.Add(ttsStream);

                logger.LogInformation($"TTS stream added to mixer: {Path.GetFileName(ttsPath)} (Stream ID: {ttsStream})");

                return ttsStream;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, $"Error adding TTS stream: {ttsPath}");
                return 0;
            }
        }

        // ★ TTS 스트림 제거 메서드 (새로 추가)
        public async Task RemoveTtsStream(string broadcastId, int ttsStreamId)
        {
            if (!_sessions.TryGetValue(broadcastId, out var session))
                return;

            if (session.TtsStreams.Contains(ttsStreamId))
            {
                BassMix.MixerRemoveChannel(ttsStreamId);
                Bass.StreamFree(ttsStreamId);
                session.TtsStreams.Remove(ttsStreamId);

                logger.LogInformation($"TTS stream removed from mixer: Stream ID {ttsStreamId}");
            }
        }

        // ★ 모든 TTS 스트림 제거 메서드 (새로 추가)
        public async Task RemoveAllTtsStreams(string broadcastId)
        {
            if (!_sessions.TryGetValue(broadcastId, out var session))
                return;

            foreach (var ttsStream in session.TtsStreams.ToList())
            {
                BassMix.MixerRemoveChannel(ttsStream);
                Bass.StreamFree(ttsStream);
            }

            session.TtsStreams.Clear();

            logger.LogInformation($"All TTS streams removed from mixer for broadcast: {broadcastId}");
        }

        public async Task SetVolume(string broadcastId, AudioSource source, float volume)
        {
            if (!_sessions.TryGetValue(broadcastId, out var session))
                return;

            volume = Math.Max(0, Math.Min(1, volume));

            switch (source)
            {
                case AudioSource.Microphone:
                    session.MicVolume = volume;
                    if (session.MicPushStream != 0)
                        Bass.ChannelSetAttribute(session.MicPushStream, ChannelAttribute.Volume, volume);
                    break;

                case AudioSource.Media:
                    session.MediaVolume = volume;
                    if (session.MediaStream != 0)
                        Bass.ChannelSetAttribute(session.MediaStream, ChannelAttribute.Volume, volume);
                    break;

                case AudioSource.TTS:
                    session.TtsVolume = volume;
                    // 모든 TTS 스트림에 볼륨 적용
                    foreach (var ttsStream in session.TtsStreams)
                    {
                        Bass.ChannelSetAttribute(ttsStream, ChannelAttribute.Volume, volume);
                    }
                    break;

                case AudioSource.Master:
                    session.MasterVolume = volume;
                    if (session.MixerStream != 0)
                        Bass.ChannelSetAttribute(session.MixerStream, ChannelAttribute.Volume, volume);
                    break;
            }

            logger.LogDebug($"Volume set for {source}: {volume:F2}");
        }

        public async Task<bool> StopMixer(string broadcastId)
        {
            if (_sessions.TryRemove(broadcastId, out var session))
            {
                session.IsActive = false;

                session.OutputTimer?.Dispose();

                if (session.MediaStream != 0)
                {
                    BassMix.MixerRemoveChannel(session.MediaStream);
                    Bass.StreamFree(session.MediaStream);
                }

                // TTS 스트림들 정리
                foreach (var ttsStream in session.TtsStreams)
                {
                    BassMix.MixerRemoveChannel(ttsStream);
                    Bass.StreamFree(ttsStream);
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
                    BassMix.MixerRemoveChannel(session.MicPushStream);
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

        public bool HasActiveMediaStream(string broadcastId)
        {
            if (!_sessions.TryGetValue(broadcastId, out var session))
                return false;

            return session.MediaStream != 0 && session.IsActive;
        }

        public bool HasActiveTtsStream(string broadcastId)
        {
            if (!_sessions.TryGetValue(broadcastId, out var session))
                return false;

            return session.TtsStreams.Any() && session.IsActive;
        }

        public async Task<bool> InitializeMicStream(string broadcastId)
        {
            if (!_sessions.TryGetValue(broadcastId, out var session))
            {
                logger.LogError($"No session found for broadcast: {broadcastId}");
                return false;
            }

            try
            {
                if (session.MicPushStream != 0)
                {
                    await RemoveMicrophoneStream(broadcastId);
                }

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