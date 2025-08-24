using ManagedBass;
using ManagedBass.Mix;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WicsPlatform.Server.Middleware;
using static WicsPlatform.Server.Middleware.WebSocketMiddleware;

namespace WicsPlatform.Server.Services
{
    public class MediaBroadcastService : IMediaBroadcastService, IDisposable
    {
        private readonly ILogger<MediaBroadcastService> _logger;
        private readonly IUdpBroadcastService _udpService;
        private readonly ConcurrentDictionary<string, MediaSession> _sessions = new();
        private readonly ConcurrentDictionary<string, string> _broadcastToMediaSession = new();
        private bool _bassInitialized = false;

        public MediaBroadcastService(
            ILogger<MediaBroadcastService> logger,
            IUdpBroadcastService udpService)
        {
            _logger = logger;
            _udpService = udpService;
            InitializeBass();
        }

        private void InitializeBass()
        {
            try
            {
                // Bass 초기화 (디바이스 -1은 "no sound" 디바이스)
                if (!Bass.Init(-1, 48000, DeviceInitFlags.Mono))
                {
                    _logger.LogWarning($"Failed to initialize BASS: {Bass.LastError}");
                }
                else
                {
                    _bassInitialized = true;
                    _logger.LogInformation("BASS initialized successfully");

                    // 버전 정보 로깅
                    var version = Bass.Version;
                    _logger.LogInformation($"BASS Version: {version}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error initializing BASS");
            }
        }

        public async Task<MediaPlaybackResult> HandlePlayRequestAsync(
            string broadcastId,
            JsonElement requestData,
            List<MediaInfo> availableMedia,
            List<SpeakerInfo> onlineSpeakers,
            ulong channelId)
        {
            try
            {
                _logger.LogInformation($"Processing media play request for broadcast: {broadcastId}");

                // 1. 요청된 미디어 ID 파싱
                var requestedMediaIds = ParseMediaIds(requestData);

                // 2. 재생할 미디어 선택
                var mediaToPlay = SelectMediaToPlay(availableMedia, requestedMediaIds);

                if (!mediaToPlay.Any())
                {
                    return new MediaPlaybackResult
                    {
                        Success = false,
                        Message = "No media files available"
                    };
                }

                // 3. 이전 세션 정지
                await StopPreviousSessionIfExists(broadcastId);

                // 4. 새 세션 시작
                var sessionId = Guid.NewGuid().ToString();
                var result = await StartMediaSession(
                    sessionId,
                    channelId,
                    mediaToPlay,
                    onlineSpeakers
                );

                // 5. 성공 시 매핑 저장
                if (result.Success)
                {
                    _broadcastToMediaSession[broadcastId] = result.SessionId;
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error handling media play request");
                return new MediaPlaybackResult
                {
                    Success = false,
                    Message = ex.Message
                };
            }
        }

        private List<ulong> ParseMediaIds(JsonElement requestData)
        {
            var mediaIds = new List<ulong>();

            if (requestData.TryGetProperty("mediaIds", out var mediaIdsElement))
            {
                foreach (var idElement in mediaIdsElement.EnumerateArray())
                {
                    if (idElement.TryGetUInt64(out var mediaId))
                    {
                        mediaIds.Add(mediaId);
                    }
                }
            }

            return mediaIds;
        }

        private List<MediaInfo> SelectMediaToPlay(List<MediaInfo> availableMedia, List<ulong> requestedIds)
        {
            if (availableMedia == null || !availableMedia.Any())
                return new List<MediaInfo>();

            // 요청된 ID가 없으면 모든 미디어 재생
            if (!requestedIds.Any())
                return availableMedia.ToList();

            // 요청된 ID에 해당하는 미디어만 선택
            return availableMedia.Where(m => requestedIds.Contains(m.Id)).ToList();
        }

        private async Task StopPreviousSessionIfExists(string broadcastId)
        {
            if (_broadcastToMediaSession.TryRemove(broadcastId, out var previousSessionId))
            {
                if (_sessions.TryRemove(previousSessionId, out var session))
                {
                    session.CancellationTokenSource?.Cancel();

                    // BASS 스트림 정리
                    foreach (var stream in session.BassStreams)
                    {
                        Bass.StreamFree(stream);
                    }
                    session.BassStreams.Clear();

                    _logger.LogInformation($"Stopped previous session {previousSessionId}");
                }
            }
        }

        private async Task<MediaPlaybackResult> StartMediaSession(
            string sessionId,
            ulong channelId,
            List<MediaInfo> mediaFiles,
            List<SpeakerInfo> speakers)
        {
            var result = new MediaPlaybackResult
            {
                SessionId = sessionId,
                Success = false
            };

            try
            {
                if (!_bassInitialized)
                {
                    result.Message = "Audio system not initialized";
                    return result;
                }

                var session = new MediaSession
                {
                    SessionId = sessionId,
                    ChannelId = channelId,
                    Speakers = speakers,
                    StartTime = DateTime.UtcNow,
                    CancellationTokenSource = new CancellationTokenSource()
                };

                // 미디어 파일 검증
                foreach (var media in mediaFiles)
                {
                    var fileStatus = await ValidateMediaFile(media);
                    result.MediaFiles.Add(fileStatus);

                    if (fileStatus.Status == "opened")
                    {
                        session.ValidatedFiles.Add(media);
                    }
                }

                if (!session.ValidatedFiles.Any())
                {
                    result.Message = "No valid media files found";
                    return result;
                }

                _sessions[sessionId] = session;

                // 백그라운드에서 재생 시작
                _ = Task.Run(() => PlaybackLoop(session));

                result.Success = true;
                result.Message = $"Started playback of {session.ValidatedFiles.Count} files";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error starting media session");
                result.Message = ex.Message;
            }

            return result;
        }

        private async Task<MediaFileStatus> ValidateMediaFile(MediaInfo media)
        {
            var status = new MediaFileStatus
            {
                Id = media.Id,
                FileName = media.FileName,
                Status = "error"
            };

            try
            {
                var fullPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", media.FullPath.TrimStart('/'));

                if (!File.Exists(fullPath))
                {
                    status.ErrorMessage = "File not found";
                    return status;
                }

                var fileInfo = new FileInfo(fullPath);
                status.FileSize = fileInfo.Length;
                status.Format = Path.GetExtension(fullPath).ToUpper().TrimStart('.');

                // BASS로 파일 검증
                var stream = Bass.CreateStream(fullPath, 0, 0, BassFlags.Decode);
                if (stream != 0)
                {
                    // 파일 길이 가져오기
                    var lengthBytes = Bass.ChannelGetLength(stream);
                    var lengthSeconds = Bass.ChannelBytes2Seconds(stream, lengthBytes);
                    status.Duration = TimeSpan.FromSeconds(lengthSeconds);

                    Bass.StreamFree(stream);
                    status.Status = "opened";

                    _logger.LogInformation($"Validated media file: {media.FileName}, Duration: {status.Duration}");
                }
                else
                {
                    status.ErrorMessage = $"BASS error: {Bass.LastError}";
                    _logger.LogWarning($"Failed to open media file with BASS: {media.FileName}, Error: {Bass.LastError}");
                }
            }
            catch (Exception ex)
            {
                status.ErrorMessage = ex.Message;
                _logger.LogError(ex, $"Error validating media file: {media.FileName}");
            }

            return status;
        }

        private async Task PlaybackLoop(MediaSession session)
        {
            var cancellationToken = session.CancellationTokenSource.Token;

            try
            {
                _logger.LogInformation($"Starting playback loop for session {session.SessionId}");

                foreach (var media in session.ValidatedFiles)
                {
                    if (cancellationToken.IsCancellationRequested)
                        break;

                    _logger.LogInformation($"Playing: {media.FileName}");
                    await PlaySingleFile(media, session, cancellationToken);

                    if (!cancellationToken.IsCancellationRequested)
                        await Task.Delay(500, cancellationToken); // 트랙 간 짧은 대기
                }

                _logger.LogInformation($"Playback completed for session {session.SessionId}");
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation($"Playback cancelled for session {session.SessionId}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error in playback loop");
            }
            finally
            {
                // 스트림 정리
                foreach (var stream in session.BassStreams)
                {
                    Bass.StreamFree(stream);
                }
                session.BassStreams.Clear();

                _sessions.TryRemove(session.SessionId, out _);
            }
        }

        private async Task PlaySingleFile(MediaInfo media, MediaSession session, CancellationToken cancellationToken)
        {
            var fullPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", media.FullPath.TrimStart('/'));

            // BASS 스트림 생성 (디코드 모드 + 모노 + 48kHz 리샘플링)
            var stream = Bass.CreateStream(
                fullPath,
                0,
                0,
                BassFlags.Decode | BassFlags.Mono | BassFlags.Float
            );

            if (stream == 0)
            {
                _logger.LogError($"Failed to create BASS stream for {media.FileName}: {Bass.LastError}");
                return;
            }

            session.BassStreams.Add(stream);

            try
            {
                // 리샘플링을 위한 믹서 스트림 생성 (48kHz 모노)
                var mixerStream = BassMix.CreateMixerStream(
                    48000,
                    1,
                    BassFlags.Decode | BassFlags.Float | BassFlags.MixerNonStop
                );

                if (mixerStream == 0)
                {
                    _logger.LogError($"Failed to create mixer stream: {Bass.LastError}");
                    return;
                }

                // 원본 스트림을 믹서에 추가
                if (!BassMix.MixerAddChannel(mixerStream, stream, BassFlags.MixerChanNoRampin))
                {
                    _logger.LogError($"Failed to add channel to mixer: {Bass.LastError}");
                    Bass.StreamFree(mixerStream);
                    return;
                }

                session.BassStreams.Add(mixerStream);

                // 버퍼 설정 (60ms @ 48kHz 모노)
                const int sampleRate = 48000;
                const int bufferMs = 60;
                const int samplesPerBuffer = (sampleRate * bufferMs) / 1000; // 2880 samples
                var floatBuffer = new float[samplesPerBuffer];

                _logger.LogInformation($"Playing {media.FileName} - Buffer: {samplesPerBuffer} samples ({bufferMs}ms)");

                // 재생 루프
                while (!cancellationToken.IsCancellationRequested)
                {
                    // BASS에서 float 샘플 읽기
                    var bytesRead = Bass.ChannelGetData(mixerStream, floatBuffer, (int)DataFlags.Float | (samplesPerBuffer * 4));

                    if (bytesRead <= 0)
                    {
                        // 파일 끝 또는 에러
                        if (bytesRead == -1)
                        {
                            _logger.LogInformation($"Reached end of file: {media.FileName}");
                        }
                        else
                        {
                            _logger.LogWarning($"Error reading from stream: {Bass.LastError}");
                        }
                        break;
                    }

                    var samplesRead = bytesRead / 4; // float는 4바이트

                    // Float to Int16 PCM 변환
                    var pcm16 = new byte[samplesRead * 2];
                    for (int i = 0; i < samplesRead; i++)
                    {
                        // 클리핑 방지
                        var sample = Math.Max(-1.0f, Math.Min(1.0f, floatBuffer[i]));
                        var int16Sample = (short)(sample * 32767);

                        pcm16[i * 2] = (byte)(int16Sample & 0xFF);
                        pcm16[i * 2 + 1] = (byte)(int16Sample >> 8);
                    }

                    // UDP로 스피커에 전송
                    await _udpService.SendAudioToSpeakers(session.Speakers, pcm16);

                    // 재생 타이밍 동기화
                    await Task.Delay(bufferMs, cancellationToken);
                }

                // 스트림 정리
                Bass.StreamFree(mixerStream);
            }
            finally
            {
                Bass.StreamFree(stream);
                session.BassStreams.Remove(stream);
            }

            _logger.LogInformation($"Finished playing: {media.FileName}");
        }

        public async Task<bool> StopMediaByBroadcastIdAsync(string broadcastId)
        {
            if (_broadcastToMediaSession.TryRemove(broadcastId, out var sessionId))
            {
                if (_sessions.TryRemove(sessionId, out var session))
                {
                    session.CancellationTokenSource?.Cancel();

                    // BASS 스트림 정리
                    foreach (var stream in session.BassStreams)
                    {
                        Bass.StreamFree(stream);
                    }
                    session.BassStreams.Clear();

                    _logger.LogInformation($"Stopped media session {sessionId}");
                    return true;
                }
            }
            return false;
        }

        public async Task<MediaPlaybackStatus> GetStatusByBroadcastIdAsync(string broadcastId)
        {
            if (_broadcastToMediaSession.TryGetValue(broadcastId, out var sessionId) &&
                _sessions.TryGetValue(sessionId, out var session))
            {
                return new MediaPlaybackStatus
                {
                    SessionId = sessionId,
                    IsPlaying = true,
                    CurrentTrackIndex = 0,
                    CurrentPosition = TimeSpan.Zero,
                    TotalDuration = TimeSpan.Zero
                };
            }
            return null;
        }

        public void Dispose()
        {
            // 모든 세션 정리
            foreach (var session in _sessions.Values)
            {
                session.CancellationTokenSource?.Cancel();
                session.CancellationTokenSource?.Dispose();

                // BASS 스트림 정리
                foreach (var stream in session.BassStreams)
                {
                    Bass.StreamFree(stream);
                }
            }

            _sessions.Clear();
            _broadcastToMediaSession.Clear();

            // BASS 종료
            if (_bassInitialized)
            {
                Bass.Free();
                _logger.LogInformation("BASS freed");
            }
        }

        private class MediaSession
        {
            public string SessionId { get; set; }
            public ulong ChannelId { get; set; }
            public List<MediaInfo> ValidatedFiles { get; set; } = new();
            public List<SpeakerInfo> Speakers { get; set; }
            public DateTime StartTime { get; set; }
            public CancellationTokenSource CancellationTokenSource { get; set; }
            public List<int> BassStreams { get; set; } = new(); // BASS 스트림 핸들 저장
        }
    }
}