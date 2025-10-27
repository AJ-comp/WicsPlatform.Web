using WicsPlatform.Server.Middleware;

namespace WicsPlatform.Server.Contracts;

/// <summary>
/// 방송 관리 서비스 인터페이스
/// 방송 초기화, 콘텐츠 재생, 종료 등 방송 라이프사이클을 관리합니다.
/// </summary>
public interface IBroadcastManagementService
{
    /// <summary>
    /// 미디어 재생 및 완료 대기
    /// </summary>
    Task PlayMediaAndWaitAsync(ulong broadcastId, ulong mediaId, IEnumerable<WebSocketMiddleware.MediaInfo> availableMedia, ulong channelId);

    /// <summary>
    /// TTS 재생 및 완료 대기
    /// </summary>
    Task PlayTtsAndWaitAsync(ulong broadcastId, ulong ttsId, IEnumerable<WebSocketMiddleware.TtsInfo> availableTts, ulong channelId);

    /// <summary>
    /// 방송 종료 및 리소스 정리
    /// </summary>
    /// <param name="channelId">종료할 채널 ID</param>
    Task FinalizeBroadcastAsync(ulong channelId);
}
