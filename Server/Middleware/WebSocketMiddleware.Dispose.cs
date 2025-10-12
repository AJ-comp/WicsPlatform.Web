using Microsoft.EntityFrameworkCore;
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
                await HandleSpeakerOwnershipOnBroadcastEnd(session.ChannelId);
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


    /// <summary>
    /// 방송 종료 시 스피커 소유권 처리
    /// </summary>
    private async Task HandleSpeakerOwnershipOnBroadcastEnd(ulong endingChannelId)
    {
        try
        {
            using var scope = serviceScopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<wicsContext>();

            // 1. 종료되는 채널이 점유하거나 점유예정인 스피커 리스트 가져오기
            var channelOwnerships = await context.SpeakerOwnershipStates
                .Where(sos => sos.ChannelId == endingChannelId)
                .ToListAsync();

            if (!channelOwnerships.Any())
            {
                logger.LogDebug($"No speaker ownerships found for channel {endingChannelId}");
                return;
            }

            logger.LogInformation($"Processing {channelOwnerships.Count} speaker ownerships for ending channel {endingChannelId}");

            foreach (var ownership in channelOwnerships)
            {
                // 2. 점유예정(N)인 경우 - 그냥 삭제
                if (ownership.Ownership == "N")
                {
                    context.SpeakerOwnershipStates.Remove(ownership);
                    logger.LogDebug($"Deleted pending ownership: Speaker {ownership.SpeakerId} for channel {endingChannelId}");
                    continue;
                }

                // 3. 점유중(Y)인 경우 - 인계 대상 탐색
                if (ownership.Ownership == "Y")
                {
                    await ProcessActiveOwnershipTransfer(context, ownership);
                }
            }

            await context.SaveChangesAsync();
            logger.LogInformation($"Completed ownership cleanup for channel {endingChannelId}");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, $"Failed to handle speaker ownership for ending channel {endingChannelId}");
        }
    }

    /// <summary>
    /// 활성 소유권 인계 처리
    /// </summary>
    private async Task ProcessActiveOwnershipTransfer(wicsContext context, SpeakerOwnershipState activeOwnership)
    {
        // 3. 해당 스피커를 점유예정인 다른 채널 탐색
        var pendingOwnership = await context.SpeakerOwnershipStates
            .Where(sos =>
                sos.SpeakerId == activeOwnership.SpeakerId &&
                sos.ChannelId != activeOwnership.ChannelId &&
                sos.Ownership == "N")
            .OrderBy(sos => sos.CreatedAt)  // 가장 먼저 대기한 채널 우선
            .FirstOrDefaultAsync();

        if (pendingOwnership == null)
        {
            // 4. 인계 대상이 없는 경우 - 레코드 삭제
            context.SpeakerOwnershipStates.Remove(activeOwnership);
            logger.LogInformation($"Deleted active ownership: Speaker {activeOwnership.SpeakerId} " +
                $"from channel {activeOwnership.ChannelId} (no pending channels)");
        }
        else
        {
            // 5. 인계 대상이 있는 경우 - 소유권 인계
            await TransferSpeakerOwnership(
                context,
                activeOwnership,
                pendingOwnership);
        }
    }

    /// <summary>
    /// 스피커 소유권 인계
    /// </summary>
    private async Task TransferSpeakerOwnership(
        wicsContext context,
        SpeakerOwnershipState fromOwnership,
        SpeakerOwnershipState toOwnership)
    {
        var speakerId = fromOwnership.SpeakerId;
        var fromChannelId = fromOwnership.ChannelId;
        var toChannelId = toOwnership.ChannelId;

        // 1. 대기 중인 소유권을 활성화
        toOwnership.Ownership = "Y";
        toOwnership.UpdatedAt = DateTime.Now;
        context.SpeakerOwnershipStates.Update(toOwnership);

        // 2. 해당 채널의 BroadcastSession에서 스피커 활성화
        if (_broadcastSessions.TryGetValue(toChannelId, out var toSession))
        {
            toSession.ActivateSpeaker(speakerId);
        }
        else
        {
            logger.LogWarning($"BroadcastSession not found for channel {toChannelId} during ownership transfer");
        }

        // 3. 기존 소유권 레코드 삭제
        context.SpeakerOwnershipStates.Remove(fromOwnership);
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