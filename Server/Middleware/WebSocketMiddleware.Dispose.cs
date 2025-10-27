using Microsoft.EntityFrameworkCore;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Text.Json;
using WicsPlatform.Server.Data;
using WicsPlatform.Server.Models.wics;

namespace WicsPlatform.Server.Middleware;

public partial class WebSocketMiddleware
{
    // 미디어/TTS 재생 완료 시 처리 (통합)
    private async void OnPlaybackCompleted(ulong broadcastId)
    {
        try
        {
            if (_broadcastSessions.TryGetValue(broadcastId, out var session))
            {
                // 현재 재생 상태 조회
                var mediaStatus = await mediaBroadcastService.GetStatusByBroadcastIdAsync(broadcastId);
                var ttsStatus = await ttsBroadcastService.GetStatusByBroadcastIdAsync(broadcastId);

                // 클라이언트에 브로드캐스트: 재생 완료 알림 (UI 자동 갱신용)
                if (session.WebSocket != null)
                {
                    try
                    {
                        var payload = new
                        {
                            type = "playbackCompleted",
                            broadcastId = broadcastId,
                            mediaPlaying = mediaStatus?.IsPlaying == true,
                            ttsPlaying = ttsStatus?.IsPlaying == true
                        };
                        var json = JsonSerializer.Serialize(payload);
                        await SendMessageAsync(session.WebSocket, json);
                        logger.LogInformation($"Sent playbackCompleted to client (broadcast {broadcastId})");
                    }
                    catch (Exception sendEx)
                    {
                        logger.LogError(sendEx, $"Failed to send playbackCompleted for broadcast {broadcastId}");
                    }
                }

                // WebSocket 연결이 이미 끊어진 상태인지 확인 → 끊어졌다면 자원 정리 시도
                if (session.WebSocket == null)
                {
                    // 둘 다 재생이 끝났을 때만 정리
                    if (mediaStatus?.IsPlaying != true && ttsStatus?.IsPlaying != true)
                    {
                        logger.LogInformation($"All playback completed (Media & TTS), cleaning up broadcast: {broadcastId}");
                        await CleanupBroadcastSessionAsync(broadcastId);
                    }
                    else
                    {
                        // 아직 재생 중인 것이 있으면 로그만 남김
                        if (mediaStatus?.IsPlaying == true)
                        {
                            logger.LogInformation($"Media still playing, waiting for all playback to complete: {broadcastId}");
                        }
                        if (ttsStatus?.IsPlaying == true)
                        {
                            logger.LogInformation($"TTS still playing, waiting for all playback to complete: {broadcastId}");
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, $"Error handling playback completion for broadcast {broadcastId}");
        }
    }

    /// <summary>
    /// 브로드캐스트 세션을 완전히 정리합니다.
    /// </summary>
    /// <param name="broadcastId">정리할 브로드캐스트 ID</param>
    /// <param name="forceCleanup">강제 정리 여부 (true: 미디어/TTS 상관없이 정리)</param>
    /// <param name="updateDatabase">DB 업데이트 여부 (기본값: true)</param>
    /// <returns>정리 성공 여부</returns>
    private async Task<bool> CleanupBroadcastSessionAsync(ulong broadcastId, bool forceCleanup = false, bool updateDatabase = true)
    {
        Debug.WriteLine($"[CleanupBroadcastSessionAsync] ========== 시작 ==========");
        Debug.WriteLine($"[CleanupBroadcastSessionAsync] BroadcastId: {broadcastId}");
        Debug.WriteLine($"[CleanupBroadcastSessionAsync] forceCleanup: {forceCleanup}");
        Debug.WriteLine($"[CleanupBroadcastSessionAsync] updateDatabase: {updateDatabase}");
        try
        {
            // 강제 정리가 아닌 경우에만 미디어/TTS 재생 확인
            if (!forceCleanup)
            {
                Debug.WriteLine($"[CleanupBroadcastSessionAsync] forceCleanup=false → 미디어/TTS 상태 확인");
                var mediaStatus = await mediaBroadcastService.GetStatusByBroadcastIdAsync(broadcastId);
                Debug.WriteLine($"[CleanupBroadcastSessionAsync] 미디어 재생 상태: {mediaStatus?.IsPlaying}");
                if (mediaStatus?.IsPlaying == true)
                {
                    logger.LogInformation($"Media is still playing, skipping cleanup: {broadcastId}");
                    Debug.WriteLine($"[CleanupBroadcastSessionAsync] ❌ 미디어 재생 중 → 정리 중단");
                    return false;
                }

                // TTS 재생 상태도 확인
                var ttsStatus = await ttsBroadcastService.GetStatusByBroadcastIdAsync(broadcastId);
                Debug.WriteLine($"[CleanupBroadcastSessionAsync] TTS 재생 상태: {ttsStatus?.IsPlaying}");
                if (ttsStatus?.IsPlaying == true)
                {
                    logger.LogInformation($"TTS is still playing, skipping cleanup: {broadcastId}");
                    Debug.WriteLine($"[CleanupBroadcastSessionAsync] ❌ TTS 재생 중 → 정리 중단");
                    return false;
                }
            }
            else
            {
                Debug.WriteLine($"[CleanupBroadcastSessionAsync] forceCleanup=true → 미디어/TTS 상태 무시하고 강제 정리");
            }

            // 세션 가져오기
            _broadcastSessions.TryGetValue(broadcastId, out var session);
            Debug.WriteLine($"[CleanupBroadcastSessionAsync] 세션 조회 결과: {(session != null ? "존재" : "없음")}");

            // ✅ BroadcastManagementService에 위임
            if (updateDatabase && session != null)
            {
                Debug.WriteLine($"[CleanupBroadcastSessionAsync] DB 업데이트 시작 (ChannelId: {session.ChannelId})");
                await broadcastMgmt.FinalizeBroadcastAsync(session.ChannelId);
                Debug.WriteLine($"[CleanupBroadcastSessionAsync] DB 업데이트 완료");
            }

            // 세션 제거
            Debug.WriteLine($"[CleanupBroadcastSessionAsync] 세션 제거 시작");
            bool removed = _broadcastSessions.TryRemove(broadcastId, out _);

            if (removed && session != null)
            {
                logger.LogInformation($"Broadcast session cleaned up: {broadcastId}, " +
                    $"Channel: {session.ChannelId}, " +
                    $"Packets: {session.PacketCount}, " +
                    $"Duration: {(DateTime.UtcNow - session.StartTime):hh\\:mm\\:ss}");
                Debug.WriteLine($"[CleanupBroadcastSessionAsync] ✅ 세션 정리 완료");
            }
            else if (!removed)
            {
                logger.LogWarning($"Broadcast session not found for cleanup: {broadcastId}");
                Debug.WriteLine($"[CleanupBroadcastSessionAsync] ⚠️ 세션을 찾을 수 없음");
            }

            Debug.WriteLine($"[CleanupBroadcastSessionAsync] ========== 완료 ==========");
            return removed;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, $"Error cleaning up broadcast session: {broadcastId}");
            Debug.WriteLine($"[CleanupBroadcastSessionAsync] ❌ 예외 발생: {ex.Message}");
            return false;
        }
    }

    public void Dispose()
    {
        // 이벤트 구독 해제
        if (mediaBroadcastService != null)
        {
            mediaBroadcastService.OnPlaybackCompleted -= OnPlaybackCompleted;
        }

        // TTS 이벤트 구독 해제
        if (ttsBroadcastService != null)
        {
            ttsBroadcastService.OnPlaybackCompleted -= OnPlaybackCompleted;
        }
    }
}