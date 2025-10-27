using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using WicsPlatform.Server.Contracts;
using WicsPlatform.Server.Data;
using WicsPlatform.Server.Models.wics;
using WicsPlatform.Server.Middleware;

namespace WicsPlatform.Server.Services;

/// <summary>
/// 방송 관리 서비스
/// 방송 라이프사이클 관리 및 스피커 소유권 복귀 처리
/// </summary>
public class BroadcastManagementService : IBroadcastManagementService
{
    private readonly IAudioMixingService _mixer;
    private readonly IMediaBroadcastService _mediaService;
    private readonly ITtsBroadcastService _ttsService;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BroadcastManagementService> _logger;

    public BroadcastManagementService(
        IAudioMixingService mixer,
        IMediaBroadcastService mediaService,
        ITtsBroadcastService ttsService,
        IServiceScopeFactory scopeFactory,
        ILogger<BroadcastManagementService> logger)
    {
        _mixer = mixer;
        _mediaService = mediaService;
        _ttsService = ttsService;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <summary>
    /// 미디어 재생 및 완료 대기
    /// </summary>
    public async Task PlayMediaAndWaitAsync(ulong broadcastId, ulong mediaId, IEnumerable<WebSocketMiddleware.MediaInfo> availableMedia, ulong channelId)
    {
        var jsonMedia = JsonSerializer.SerializeToElement(new { broadcastId, mediaIds = new List<ulong> { mediaId } });
        var targetMedia = availableMedia.Where(m => m.Id == mediaId).ToList();
        
        await _mediaService.HandlePlayRequestAsync(broadcastId, jsonMedia, targetMedia, new List<SpeakerInfo>(), channelId);
        _logger.LogInformation("[BroadcastManagement] Playing media {MediaId} (broadcast {BroadcastId})", mediaId, broadcastId);
        
        // Wait until media finished
        while (_mixer.HasActiveMediaStream(broadcastId))
        {
            await Task.Delay(200);
        }
        
        _logger.LogInformation("[BroadcastManagement] Media {MediaId} playback completed (broadcast {BroadcastId})", mediaId, broadcastId);
    }

    /// <summary>
    /// TTS 재생 및 완료 대기
    /// </summary>
    public async Task PlayTtsAndWaitAsync(ulong broadcastId, ulong ttsId, IEnumerable<WebSocketMiddleware.TtsInfo> availableTts, ulong channelId)
    {
        var jsonTts = JsonSerializer.SerializeToElement(new { broadcastId, ttsIds = new List<ulong> { ttsId } });
        var targetTts = availableTts.Where(t => t.Id == ttsId).ToList();
        
        await _ttsService.HandlePlayRequestAsync(broadcastId, jsonTts, targetTts, new List<SpeakerInfo>(), channelId);
        _logger.LogInformation("[BroadcastManagement] Playing TTS {TtsId} (broadcast {BroadcastId})", ttsId, broadcastId);
        
        // Wait until TTS finished
        while (_mixer.HasActiveTtsStream(broadcastId))
        {
            await Task.Delay(200);
        }
        
        _logger.LogInformation("[BroadcastManagement] TTS {TtsId} playback completed (broadcast {BroadcastId})", ttsId, broadcastId);
    }

    /// <summary>
    /// 방송 종료 및 리소스 정리
    /// </summary>
    public async Task FinalizeBroadcastAsync(ulong channelId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<wicsContext>();

        var broadcast = await FindOngoingBroadcastAsync(db, channelId);
        if (broadcast == null) return;

        // ✅ 수정: channelId를 broadcastId로 사용 (세션 키와 일치)
        var broadcastId = channelId;
        
        _logger.LogInformation(
            $"[BroadcastManagement] Starting finalization - BroadcastId={broadcastId} (ChannelId), DB_Broadcast_Id={broadcast.Id}");

        // 1. 콘텐츠 중지 (channelId 사용)
        await StopBroadcastContentAsync(broadcastId);

        // 2. 믹서 중지 (channelId 사용)
        await _mixer.StopMixer(broadcastId);
        _logger.LogInformation($"[BroadcastManagement] Mixer stopped for broadcast {broadcastId}");

        // 3. DB 상태 업데이트
        await UpdateBroadcastStateAsync(db, broadcast, channelId);

        // 4. 스피커 소유권 복귀 (통합 로직)
        var ownershipBroker = scope.ServiceProvider.GetRequiredService<SpeakerOwnershipBroker>();
        await RestoreSpeakerOwnershipsAsync(db, ownershipBroker, channelId);

        _logger.LogInformation($"[BroadcastManagement] Broadcast {broadcastId} finalized for channel {channelId}");
    }

    #region Private Methods

    /// <summary>
    /// 진행 중인 방송 조회
    /// </summary>
    private async Task<Broadcast?> FindOngoingBroadcastAsync(wicsContext db, ulong channelId)
    {
        var broadcast = await db.Broadcasts
            .Where(x => x.ChannelId == channelId && x.OngoingYn == "Y")
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync();

        if (broadcast == null)
        {
            _logger.LogWarning($"[BroadcastManagement] No ongoing broadcast found for channel {channelId}");
        }

        return broadcast;
    }

    /// <summary>
    /// 방송 콘텐츠 중지 (미디어 + TTS)
    /// </summary>
    private async Task StopBroadcastContentAsync(ulong broadcastId)
    {
        try
        {
            await _mediaService.StopMediaByBroadcastIdAsync(broadcastId);
            _logger.LogInformation("[BroadcastManagement] Media stopped for broadcast {BroadcastId}", broadcastId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[BroadcastManagement] Failed to stop media for broadcast {BroadcastId}", broadcastId);
        }

        try
        {
            await _ttsService.StopTtsByBroadcastIdAsync(broadcastId);
            _logger.LogInformation("[BroadcastManagement] TTS stopped for broadcast {BroadcastId}", broadcastId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[BroadcastManagement] Failed to stop TTS for broadcast {BroadcastId}", broadcastId);
        }
    }

    /// <summary>
    /// 방송 및 채널 상태 업데이트
    /// </summary>
    private async Task UpdateBroadcastStateAsync(wicsContext db, Broadcast broadcast, ulong channelId)
    {
        broadcast.OngoingYn = "N";
        broadcast.UpdatedAt = DateTime.UtcNow;
        _logger.LogInformation("[BroadcastManagement] Broadcast {BroadcastId} marked as not ongoing", broadcast.Id);

        var ch = await db.Channels.FirstOrDefaultAsync(c => c.Id == channelId);
        if (ch != null)
        {
            ch.State = 0;
            ch.UpdatedAt = DateTime.UtcNow;
            _logger.LogInformation("[BroadcastManagement] Channel {ChannelId} state set to 0 (inactive)", channelId);
        }

        await db.SaveChangesAsync();
    }

    /// <summary>
    /// 스피커 소유권 복귀 처리 (통합 로직: N 삭제 + Y 이전)
    /// </summary>
    private async Task RestoreSpeakerOwnershipsAsync(
        wicsContext db,
        SpeakerOwnershipBroker ownershipBroker,
        ulong channelId)
    {
        // Y와 N 모두 조회
        var channelOwnerships = await db.SpeakerOwnershipStates
            .Where(o => o.ChannelId == channelId)
            .ToListAsync();

        if (!channelOwnerships.Any())
        {
            _logger.LogDebug("[BroadcastManagement] No speaker ownerships found for channel {ChannelId}", channelId);
            return;
        }

        _logger.LogInformation("[BroadcastManagement] Processing {Count} speaker ownerships for channel {ChannelId}",
            channelOwnerships.Count, channelId);

        foreach (var ownership in channelOwnerships)
        {
            // N 삭제 (대기 소유권 정리)
            if (ownership.Ownership == "N")
            {
                db.SpeakerOwnershipStates.Remove(ownership);
                _logger.LogDebug("[BroadcastManagement] Deleted pending ownership: Speaker {SpeakerId}",
                    ownership.SpeakerId);
                continue;
            }

            // Y 이전 (활성 소유권 복귀)
            if (ownership.Ownership == "Y")
            {
                await ProcessOwnershipTransferAsync(db, ownershipBroker, ownership);
            }
        }

        await db.SaveChangesAsync();
        _logger.LogInformation("[BroadcastManagement] Completed ownership cleanup for channel {ChannelId}", channelId);
    }

    /// <summary>
    /// 활성 소유권 이전 처리
    /// </summary>
    private async Task ProcessOwnershipTransferAsync(
        wicsContext db,
        SpeakerOwnershipBroker ownershipBroker,
        SpeakerOwnershipState ownership)
    {
        var speakerId = ownership.SpeakerId;

        // 우선순위 기반으로 대기 중인 채널 찾기
        var waitingOwnership = await db.SpeakerOwnershipStates
            .Include(o => o.Channel)
            .Where(o => o.SpeakerId == speakerId && o.Ownership == "N")
            .OrderByDescending(o => o.Channel.Priority)
            .FirstOrDefaultAsync();

        if (waitingOwnership != null)
        {
            // 복귀할 채널 존재
            var targetChannelId = waitingOwnership.ChannelId;

            // DB 업데이트
            db.SpeakerOwnershipStates.Remove(ownership);
            waitingOwnership.Ownership = "Y";
            waitingOwnership.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();

            // 이벤트 발행
            await ownershipBroker.PublishAsync(new SpeakerOwnershipChangedEvent(
                targetChannelId, speakerId, IsActive: true, DateTime.UtcNow));

            _logger.LogInformation(
                "[BroadcastManagement] Speaker {SpeakerId} transferred to channel {ChannelId} (priority {Priority})",
                speakerId, targetChannelId, waitingOwnership.Channel.Priority);
        }
        else
        {
            // 복귀할 채널 없음
            db.SpeakerOwnershipStates.Remove(ownership);
            await db.SaveChangesAsync();

            _logger.LogInformation(
                "[BroadcastManagement] Speaker {SpeakerId} released (no waiting channels)", speakerId);
        }
    }

    #endregion
}
