using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using WicsPlatform.Server.Data;

namespace WicsPlatform.Server.Middleware;

public partial class WebSocketMiddleware
{
    // 미디어 재생 완료 시 처리
    private async void OnMediaPlaybackCompleted(string broadcastId)
    {
        try
        {
            if (_broadcastSessions.TryGetValue(broadcastId, out var session))
            {
                // WebSocket 연결이 이미 끊어진 상태인지 확인
                if (session.WebSocket == null)
                {
                    // 클라이언트도 없고 미디어도 끝났으므로 완전 정리
                    logger.LogInformation($"Media playback completed, cleaning up broadcast: {broadcastId}");

                    await CleanupBroadcastSessionAsync(broadcastId);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, $"Error handling media playback completion for broadcast {broadcastId}");
        }
    }


    /// <summary>
    /// 브로드캐스트 세션을 완전히 정리합니다.
    /// </summary>
    /// <param name="broadcastId">정리할 브로드캐스트 ID</param>
    /// <param name="forceCleanup">강제 정리 여부 (true: 미디어 상관없이 정리)</param>
    /// <param name="updateDatabase">DB 업데이트 여부 (기본값: true)</param>
    /// <returns>정리 성공 여부</returns>
    private async Task<bool> CleanupBroadcastSessionAsync(string broadcastId, bool forceCleanup = false, bool updateDatabase = true)
    {
        try
        {
            // 강제 정리가 아닌 경우에만 미디어 재생 확인
            if (!forceCleanup)
            {
                var mediaStatus = await mediaBroadcastService.GetStatusByBroadcastIdAsync(broadcastId);
                if (mediaStatus?.IsPlaying == true)
                {
                    logger.LogInformation($"Media is still playing, skipping cleanup: {broadcastId}");
                    return false;
                }
            }

            // 세션 가져오기
            _broadcastSessions.TryGetValue(broadcastId, out var session);

            // DB 업데이트 (channelId만 전달)
            if (updateDatabase && session != null)
            {
                await UpdateBroadcastStatusInDatabase(session.ChannelId, false);
            }

            // 1. 오디오 믹서 정리
            await audioMixingService.StopMixer(broadcastId);

            // 2. 미디어 재생 중지
            await mediaBroadcastService.StopMediaByBroadcastIdAsync(broadcastId);

            // 3. 세션 제거
            bool removed = _broadcastSessions.TryRemove(broadcastId, out _);

            if (removed && session != null)
            {
                logger.LogInformation($"Broadcast session cleaned up: {broadcastId}, " +
                    $"Channel: {session.ChannelId}, " +
                    $"Packets: {session.PacketCount}, " +
                    $"Duration: {(DateTime.UtcNow - session.StartTime):hh\\:mm\\:ss}");
            }
            else if (!removed)
            {
                logger.LogWarning($"Broadcast session not found for cleanup: {broadcastId}");
            }

            return removed;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, $"Error cleaning up broadcast session: {broadcastId}");
            return false;
        }
    }

    /// <summary>
    /// DB의 브로드캐스트 상태를 업데이트합니다.
    /// </summary>
    /// <param name="channelId">채널 ID</param>
    /// <param name="isOngoing">진행 중 여부</param>
    private async Task UpdateBroadcastStatusInDatabase(ulong channelId, bool isOngoing)
    {
        try
        {
            using var scope = serviceScopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<wicsContext>();

            var broadcasts = await context.Broadcasts
                .Where(b => b.ChannelId == channelId && b.OngoingYn == "Y")
                .ToListAsync();

            foreach (var broadcast in broadcasts)
            {
                broadcast.OngoingYn = isOngoing ? "Y" : "N";
                broadcast.UpdatedAt = DateTime.Now;
            }

            await context.SaveChangesAsync();

            logger.LogDebug($"Updated {broadcasts.Count} broadcast records for channel {channelId}");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, $"Failed to update broadcast status for channel: {channelId}");
        }
    }


    public void Dispose()
    {
        // 이벤트 구독 해제
        if (mediaBroadcastService != null)
        {
            mediaBroadcastService.OnPlaybackCompleted -= OnMediaPlaybackCompleted;
        }
    }
}
