using System.Collections.Concurrent;
using System.Text.Json;
using WicsPlatform.Server.Contracts;
using static WicsPlatform.Server.Middleware.WebSocketMiddleware;

namespace WicsPlatform.Server.Services;

public class MediaBroadcastService : IMediaBroadcastService, IDisposable
{
    private readonly ILogger<MediaBroadcastService> logger;
    private readonly IAudioMixingService audioMixingService;
    private readonly ConcurrentDictionary<ulong, MediaSession> _sessions = new();

    public event Action<ulong> OnPlaybackCompleted;

    private class MediaSession
    {
        public ulong BroadcastId { get; set; }
        public List<MediaInfo> MediaFiles { get; set; }
        public int CurrentIndex { get; set; }
        public int CurrentStream { get; set; }
        public bool IsPlaying { get; set; }
    }

    public MediaBroadcastService(
        ILogger<MediaBroadcastService> logger,
        IUdpBroadcastService udpService,
        IAudioMixingService audioMixingService)
    {
        this.logger = logger;
        this.audioMixingService = audioMixingService;

        // 이벤트 구독: 믹서에서 미디어 종료 -> 다음 트랙 자동 진행
        this.audioMixingService.OnMediaEnded += HandleMediaEnded;
    }

    public async Task<MediaPlaybackResult> HandlePlayRequestAsync(
        ulong broadcastId,
        JsonElement requestData,
        List<MediaInfo> availableMedia,
        List<SpeakerInfo> onlineSpeakers,
        ulong channelId)
    {
        try
        {
            logger.LogInformation($"Starting media playback for broadcast: {broadcastId}");

            // 1. 기존 재생 중지
            await StopMediaByBroadcastIdAsync(broadcastId);

            // 2. 재생할 미디어 선택
            var mediaToPlay = SelectMediaToPlay(availableMedia, requestData);
            if (!mediaToPlay.Any())
            {
                return new MediaPlaybackResult
                {
                    Success = false,
                    Message = "No media files available"
                };
            }

            // 3. 새 세션 생성
            var session = new MediaSession
            {
                BroadcastId = broadcastId,
                MediaFiles = mediaToPlay,
                CurrentIndex = 0,
                IsPlaying = true
            };

            _sessions[broadcastId] = session;

            // 4. 첫 번째 파일 재생 시작
            await PlayCurrentIndex(broadcastId);

            // 5. 결과 반환
            var result = new MediaPlaybackResult
            {
                SessionId = broadcastId.ToString(),
                Success = true,
                Message = $"Started playback of {mediaToPlay.Count} files"
            };

            foreach (var media in mediaToPlay)
            {
                result.MediaFiles.Add(new MediaFileStatus
                {
                    Id = media.Id,
                    FileName = media.FileName,
                    Status = "ready"
                });
            }

            return result;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, $"Error starting media playback for broadcast {broadcastId}");
            return new MediaPlaybackResult
            {
                Success = false,
                Message = ex.Message
            };
        }
    }

    private async Task PlayCurrentIndex(ulong broadcastId)
    {
        if (!_sessions.TryGetValue(broadcastId, out var session) || !session.IsPlaying)
            return;

        if (session.CurrentIndex >= session.MediaFiles.Count)
        {
            // 플레이리스트 종료
            logger.LogInformation($"Playlist completed for broadcast {broadcastId}");
            await StopMediaByBroadcastIdAsync(broadcastId);
            OnPlaybackCompleted?.Invoke(broadcastId);
            return;
        }

        var media = session.MediaFiles[session.CurrentIndex];
        var fullPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", media.FullPath.TrimStart('/'));

        if (!File.Exists(fullPath))
        {
            logger.LogError($"Media file not found: {fullPath}");
            session.CurrentIndex++;
            await PlayCurrentIndex(broadcastId); // 다음 파일로 이동
            return;
        }

        await audioMixingService.AddMediaStream(broadcastId, media.FullPath);
        logger.LogInformation($"Playing: {media.FileName} ({session.CurrentIndex + 1}/{session.MediaFiles.Count})");
    }

    private async void HandleMediaEnded(ulong broadcastId)
    {
        try
        {
            if (!_sessions.TryGetValue(broadcastId, out var session) || !session.IsPlaying)
                return;

            // 다음 트랙으로 이동
            session.CurrentIndex++;
            await PlayCurrentIndex(broadcastId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, $"Error advancing to next media for broadcast {broadcastId}");
        }
    }

    public async Task<bool> SkipToNextAsync(ulong broadcastId)
    {
        try
        {
            if (!_sessions.TryGetValue(broadcastId, out var session) || !session.IsPlaying)
                return false;

            // 현재 미디어 제거 후 인덱스 증가
            await audioMixingService.RemoveMediaStream(broadcastId);
            session.CurrentIndex++;
            await PlayCurrentIndex(broadcastId);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, $"Error skipping to next media for broadcast {broadcastId}");
            return false;
        }
    }

    public async Task<bool> StopMediaByBroadcastIdAsync(ulong broadcastId)
    {
        try
        {
            logger.LogInformation($"Stopping media for broadcast: {broadcastId}");

            // 1. 믹서에서 미디어 제거
            await audioMixingService.RemoveMediaStream(broadcastId);

            // 2. 세션 정리 (남기되 IsPlaying false)
            if (_sessions.TryGetValue(broadcastId, out var session))
            {
                session.IsPlaying = false;
            }

            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, $"Error stopping media for broadcast {broadcastId}");
            return false;
        }
    }

    public async Task<MediaPlaybackStatus> GetStatusByBroadcastIdAsync(ulong broadcastId)
    {
        if (_sessions.TryGetValue(broadcastId, out var session) && session.IsPlaying)
        {
            var currentPosition = TimeSpan.Zero;
            var duration = TimeSpan.Zero;

            return new MediaPlaybackStatus
            {
                SessionId = broadcastId.ToString(),
                IsPlaying = true,
                CurrentTrackIndex = session.CurrentIndex,
                CurrentPosition = currentPosition,
                TotalDuration = duration
            };
        }

        return new MediaPlaybackStatus
        {
            SessionId = broadcastId.ToString(),
            IsPlaying = false
        };
    }

    // expose now-playing info
    public (ulong? mediaId, string fileName)? GetCurrentMedia(ulong broadcastId)
    {
        if (_sessions.TryGetValue(broadcastId, out var session) &&
            session.IsPlaying &&
            session.CurrentIndex >= 0 &&
            session.CurrentIndex < session.MediaFiles?.Count)
        {
            var m = session.MediaFiles[session.CurrentIndex];
            return (m.Id, m.FileName);
        }
        return null;
    }

    private List<MediaInfo> SelectMediaToPlay(List<MediaInfo> availableMedia, JsonElement requestData)
    {
        if (availableMedia == null || !availableMedia.Any())
            return new List<MediaInfo>();

        // 요청에 특정 미디어 ID가 있으면 해당 미디어만 선택
        if (requestData.TryGetProperty("mediaIds", out var mediaIdsElement))
        {
            var requestedIds = new List<ulong>();
            foreach (var idElement in mediaIdsElement.EnumerateArray())
            {
                if (idElement.TryGetUInt64(out var mediaId))
                {
                    requestedIds.Add(mediaId);
                }
            }

            if (requestedIds.Any())
            {
                var selected = availableMedia.Where(m => requestedIds.Contains(m.Id)).ToList();

                // 셔플 지정 여부 확인
                bool shuffle = false;
                if (requestData.TryGetProperty("shuffle", out var shuffleElement) && shuffleElement.ValueKind == JsonValueKind.True)
                {
                    shuffle = true;
                }

                if (shuffle)
                {
                    var rnd = new Random();
                    // Fisher–Yates shuffle
                    for (int i = selected.Count - 1; i > 0; i--)
                    {
                        int j = rnd.Next(i + 1);
                        (selected[i], selected[j]) = (selected[j], selected[i]);
                    }
                }

                return selected;
            }
        }

        // 요청된 ID가 없으면 모든 미디어 재생
        var all = availableMedia.ToList();

        // 셔플 지정 여부 확인
        if (requestData.TryGetProperty("shuffle", out var shuffleAll) && shuffleAll.ValueKind == JsonValueKind.True)
        {
            var rnd = new Random();
            for (int i = all.Count - 1; i > 0; i--)
            {
                int j = rnd.Next(i + 1);
                (all[i], all[j]) = (all[j], all[i]);
            }
        }

        return all;
    }

    public void Dispose()
    {
        try
        {
            if (audioMixingService != null)
            {
                audioMixingService.OnMediaEnded -= HandleMediaEnded;
            }
        }
        catch { }

        // 모든 세션 정리 시도
        foreach (var kvp in _sessions)
        {
            _ = StopMediaByBroadcastIdAsync(kvp.Key);
        }

        _sessions.Clear();
    }
}
