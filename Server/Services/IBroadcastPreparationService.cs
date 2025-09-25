using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using WicsPlatform.Server.Models.wics;
using WicsPlatform.Shared;
using WicsPlatform.Server.Middleware;

namespace WicsPlatform.Server.Services
{
    public record TakeoverInfo(ulong SpeakerId, ulong PreviousChannelId);

    public record PreparedBroadcast(
        Channel Channel,
        List<SpeakerInfo> Speakers,
        List<WebSocketMiddleware.MediaInfo> Media,
        List<WebSocketMiddleware.TtsInfo> Tts,
        List<TakeoverInfo> Takeovers);

    public interface IBroadcastPreparationService
    {
        Task<PreparedBroadcast> PrepareAsync(ulong channelId, IEnumerable<ulong>? selectedGroupIds = null, CancellationToken ct = default);
    }
}
