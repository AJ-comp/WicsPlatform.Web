using System.Text.Json;
using WicsPlatform.Server.Middleware;
using WicsPlatform.Server.Services;
using static WicsPlatform.Server.Middleware.WebSocketMiddleware;

namespace WicsPlatform.Server.Contracts
{
    public interface IMediaBroadcastService
    {
        // 간소화된 메서드 - BroadcastSession을 직접 받음
        Task<MediaPlaybackResult> HandlePlayRequestAsync(
            ulong broadcastId,
            JsonElement requestData,
            List<MediaInfo> availableMedia,
            List<SpeakerInfo> onlineSpeakers,
            ulong channelId);

        Task<bool> StopMediaByBroadcastIdAsync(ulong broadcastId);
        Task<MediaPlaybackStatus> GetStatusByBroadcastIdAsync(ulong broadcastId);

        // 현재 재생중인 곡 간단 정보
        (ulong? mediaId, string fileName)? GetCurrentMedia(ulong broadcastId);

        // 다음 트랙으로 이동
        Task<bool> SkipToNextAsync(ulong broadcastId);

        public event Action<ulong> OnPlaybackCompleted;

        void Dispose();
    }

    public class MediaPlaybackResult
    {
        public bool Success { get; set; }
        public string SessionId { get; set; }
        public string Message { get; set; }
        public List<MediaFileStatus> MediaFiles { get; set; } = new();
    }

    public class MediaFileStatus
    {
        public ulong Id { get; set; }
        public string FileName { get; set; }
        public string Status { get; set; }
        public string ErrorMessage { get; set; }
        public long FileSize { get; set; }
        public string Format { get; set; }
        public TimeSpan? Duration { get; set; }
    }

    public class MediaPlaybackStatus
    {
        public string SessionId { get; set; }
        public bool IsPlaying { get; set; }
        public int CurrentTrackIndex { get; set; }
        public TimeSpan CurrentPosition { get; set; }
        public TimeSpan TotalDuration { get; set; }
    }
}