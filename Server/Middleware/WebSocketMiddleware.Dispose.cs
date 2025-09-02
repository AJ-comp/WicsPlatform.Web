using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using WicsPlatform.Server.Data;

namespace WicsPlatform.Server.Middleware;

public partial class WebSocketMiddleware
{
    // 미디어/TTS 재생 완료 시 처리 (통합)
    private async void OnPlaybackCompleted(string broadcastId)
    {
        try
        {
            if (_broadcastSessions.TryGetValue(broadcastId, out var session))
            {
                // WebSocket 연결이 이미 끊어진 상태인지 확인
                if (session.WebSocket == null)
                {
                    // 미디어와 TTS 둘 다 재생 중인지 확인
                    var mediaStatus = await mediaBroadcastService.GetStatusByBroadcastIdAsync(broadcastId);
                    var ttsStatus = await ttsBroadcastService.GetStatusByBroadcastIdAsync(broadcastId);

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
    private async Task<bool> CleanupBroadcastSessionAsync(string broadcastId, bool forceCleanup = false, bool updateDatabase = true)
    {
        try
        {
            // 강제 정리가 아닌 경우에만 미디어/TTS 재생 확인
            if (!forceCleanup)
            {
                var mediaStatus = await mediaBroadcastService.GetStatusByBroadcastIdAsync(broadcastId);
                if (mediaStatus?.IsPlaying == true)
                {
                    logger.LogInformation($"Media is still playing, skipping cleanup: {broadcastId}");
                    return false;
                }

                // TTS 재생 상태도 확인
                var ttsStatus = await ttsBroadcastService.GetStatusByBroadcastIdAsync(broadcastId);
                if (ttsStatus?.IsPlaying == true)
                {
                    logger.LogInformation($"TTS is still playing, skipping cleanup: {broadcastId}");
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

            // 3. TTS 재생 중지
            await ttsBroadcastService.StopTtsByBroadcastIdAsync(broadcastId);

            // 4. 세션 제거
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

            // ✅ 채널 상태 업데이트
            var channel = await context.Channels.FirstOrDefaultAsync(c => c.Id == channelId);
            if (channel != null)
            {
                channel.State = isOngoing ? (sbyte)1 : (sbyte)0;  // 1=방송중, 0=대기
                channel.UpdatedAt = DateTime.Now;
            }

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
            mediaBroadcastService.OnPlaybackCompleted -= OnPlaybackCompleted;
        }

        // TTS 이벤트 구독 해제
        if (ttsBroadcastService != null)
        {
            ttsBroadcastService.OnPlaybackCompleted -= OnPlaybackCompleted;
        }
    }
}