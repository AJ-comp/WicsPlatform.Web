using System.Text.Json;
using WicsPlatform.Server.Services;
using static WicsPlatform.Server.Middleware.WebSocketMiddleware;

namespace WicsPlatform.Server.Contracts
{
    public interface ITtsBroadcastService
    {
        Task<TtsPlaybackResult> HandlePlayRequestAsync(
            ulong broadcastId,
            JsonElement requestData,
            List<TtsInfo> availableTts,
            List<SpeakerInfo> onlineSpeakers,
            ulong channelId);

        Task<bool> StopTtsByBroadcastIdAsync(ulong broadcastId);
        Task<TtsPlaybackStatus> GetStatusByBroadcastIdAsync(ulong broadcastId);

        public event Action<ulong> OnPlaybackCompleted;

        void Dispose();
    }

    public class TtsPlaybackResult
    {
        public bool Success { get; set; }
        public string SessionId { get; set; }
        public string Message { get; set; }
        public List<TtsFileStatus> TtsFiles { get; set; } = new();
    }

    public class TtsFileStatus
    {
        public ulong Id { get; set; }
        public string Name { get; set; }
        public string Status { get; set; }
        public string GeneratedFilePath { get; set; }
        public TimeSpan? Duration { get; set; }
    }

    public class TtsPlaybackStatus
    {
        public string SessionId { get; set; }
        public bool IsPlaying { get; set; }
        public int CurrentIndex { get; set; }
        public int TotalCount { get; set; }
    }
}