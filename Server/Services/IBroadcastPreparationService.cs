using WicsPlatform.Server.Middleware;
using WicsPlatform.Server.Models.wics;

namespace WicsPlatform.Server.Services
{
    public record TakeoverInfo(ulong SpeakerId, ulong PreviousChannelId);

    public record PreparedBroadcast(
        Channel Channel,
        List<SpeakerInfo> Speakers,
        List<WebSocketMiddleware.MediaInfo> Media,
        List<WebSocketMiddleware.TtsInfo> Tts,
        List<TakeoverInfo> Takeovers)
    {
        // 새로 추가: schedule_play 순서를 보존한 혼합 재생 목록 (Media/TTS 교차)
        public List<PlaylistEntry>? OrderedPlaylist { get; init; }
    }

    public interface IBroadcastPreparationService
    {
        Task<PreparedBroadcast> PrepareAsync(ulong channelId, IEnumerable<ulong>? selectedGroupIds = null, CancellationToken ct = default);
    }
}
