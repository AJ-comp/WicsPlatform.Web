using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using WicsPlatform.Server.Contracts;
using WicsPlatform.Server.Data;
using WicsPlatform.Server.Models.wics;
using WicsPlatform.Shared;

namespace WicsPlatform.Server.Services;

public class ScheduleExecutionService : IScheduleExecutionService
{
    private readonly System.Threading.Channels.Channel<ulong> _queue;
    private readonly ILogger<ScheduleExecutionService> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IAudioMixingService _mixer;
    private readonly IMediaBroadcastService _mediaService;
    private readonly ITtsBroadcastService _ttsService;
    private readonly IBroadcastPreparationService prepService;

    public ScheduleExecutionService(
        ILogger<ScheduleExecutionService> logger,
        IServiceScopeFactory scopeFactory,
        IAudioMixingService mixer,
        IMediaBroadcastService mediaService,
        ITtsBroadcastService ttsService,
        IBroadcastPreparationService prepService)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
        _mixer = mixer;
        _mediaService = mediaService;
        _ttsService = ttsService;
        this.prepService = prepService;
        _queue = System.Threading.Channels.Channel.CreateUnbounded<ulong>();
        _ = Task.Run(WorkerLoop);
    }

    public async Task EnqueueAsync(ulong scheduleId, CancellationToken cancellationToken = default)
    {
        await _queue.Writer.WriteAsync(scheduleId, cancellationToken);
    }

    private async Task WorkerLoop()
    {
        await foreach (var scheduleId in _queue.Reader.ReadAllAsync())
        {
            try { await ExecuteScheduleAsync(scheduleId); }
            catch (Exception ex) { _logger.LogError(ex, "[ScheduleExecution] Error executing schedule {ScheduleId}", scheduleId); }
        }
    }

    private async Task ExecuteScheduleAsync(ulong scheduleId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<wicsContext>();
        var channels = await GetScheduleChannelsAsync(db, scheduleId);
        if (channels.Count == 0)
        {
            _logger.LogWarning("[ScheduleExecution] No channel mapped to schedule {ScheduleId}", scheduleId);
            return;
        }

        foreach (var ch in channels)
        {
            if (await ChannelHasOngoingAsync(db, ch.Id))
            {
                _logger.LogInformation("[ScheduleExecution] Channel {ChannelId} already has an ongoing broadcast. Skipping.", ch.Id);
                continue;
            }

            var prepared = await PrepareForChannelAsync(scope, ch.Id);
            if (prepared.Speakers.Count == 0)
            {
                _logger.LogWarning("[ScheduleExecution] No available speakers after ownership for channel {ChannelId}", ch.Id);
                continue;
            }

            var (broadcastId, channelEntity) = await CreateBroadcastAsync(scope, ch.Id, prepared);
            _logger.LogInformation("[ScheduleExecution] Broadcast {BroadcastId} created for channel {ChannelId}", broadcastId, ch.Id);
            try { await RunBroadcastAsync(broadcastId, ch.Id, prepared, channelEntity); }
            catch (Exception ex) { _logger.LogError(ex, "[ScheduleExecution] Error during mixer init or playback for broadcast {BroadcastId}", broadcastId); }
            finally { await FinalizeBroadcastAsync(scope, broadcastId, ch.Id); }
        }
    }

    private static Task<List<Channel>> GetScheduleChannelsAsync(wicsContext db, ulong scheduleId)
        => db.Channels.AsNoTracking().Where(c => c.ScheduleId == scheduleId && c.DeleteYn != "Y").ToListAsync();

    private static Task<bool> ChannelHasOngoingAsync(wicsContext db, ulong channelId)
        => db.Broadcasts.AsNoTracking().AnyAsync(b => b.ChannelId == channelId && b.OngoingYn == "Y");

    private async Task<PreparedChannelResources> PrepareForChannelAsync(IServiceScope scope, ulong channelId)
    {
        var prepared = await prepService.PrepareAsync(channelId, null);
        List<PlaylistEntry> playlist;

        if (prepared.OrderedPlaylist != null && prepared.OrderedPlaylist.Count > 0)
        {
            playlist = prepared.OrderedPlaylist.Select(p => p).ToList();
        }
        else
        {
            playlist = new List<PlaylistEntry>();
            foreach (var m in prepared.Media) playlist.Add(new PlaylistEntry(m));
            foreach (var t in prepared.Tts) playlist.Add(new PlaylistEntry(t));
        }
        return new PreparedChannelResources(prepared.Speakers, playlist);
    }

    private async Task<(ulong BroadcastId, Channel ChannelEntity)> CreateBroadcastAsync(IServiceScope scope, ulong channelId, PreparedChannelResources prepared)
    {
        var db = scope.ServiceProvider.GetRequiredService<wicsContext>();
        // For DB record we still need flattened media/tts ids
        var mediaIds = prepared.Media.Select(m => m.Id);
        var ttsIds = prepared.Tts.Select(t => t.Id);
        var broadcast = new Broadcast
        {
            ChannelId = channelId,
            SpeakerIdList = string.Join(' ', prepared.Speakers.Select(s => s.Id)),
            MediaIdList = string.Join(' ', mediaIds),
            TtsIdList = string.Join(' ', ttsIds),
            LoopbackYn = "N",
            OngoingYn = "Y",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        await db.Broadcasts.AddAsync(broadcast);
        var channelEntity = await db.Channels.FirstOrDefaultAsync(c => c.Id == channelId);
        if (channelEntity != null)
        {
            channelEntity.State = 1;
            channelEntity.UpdatedAt = DateTime.UtcNow;
        }
        await db.SaveChangesAsync();
        return (broadcast.Id, channelEntity!);
    }

    private async Task RunBroadcastAsync(ulong broadcastId, ulong channelId, PreparedChannelResources prepared, Channel channelEntity)
    {
        await _mixer.InitializeMixer(broadcastId, channelId, prepared.Speakers);
        await _mixer.SetVolume(broadcastId, AudioSource.Media, channelEntity?.MediaVolume ?? 1f);
        await _mixer.SetVolume(broadcastId, AudioSource.TTS, channelEntity?.TtsVolume ?? 1f);

        _logger.LogInformation("[ScheduleExecution] Starting ordered playlist for broadcast {BroadcastId} (items={Count})", broadcastId, prepared.OrderedPlaylist.Count);
        foreach (var entry in prepared.OrderedPlaylist)
        {
            try
            {
                if (entry.IsMedia)
                {
                    var jsonMedia = JsonSerializer.SerializeToElement(new { broadcastId, mediaIds = new List<ulong> { entry.Id } });
                    _ = await _mediaService.HandlePlayRequestAsync(broadcastId, jsonMedia, prepared.Media.Where(m => m.Id == entry.Id).ToList(), new List<SpeakerInfo>(), channelId);
                    _logger.LogInformation("[ScheduleExecution] Playing media {MediaId} in ordered playlist (broadcast {BroadcastId})", entry.Id, broadcastId);
                    // Wait until media finished
                    while (_mixer.HasActiveMediaStream(broadcastId))
                    {
                        await Task.Delay(200);
                    }
                }
                else
                {
                    var jsonTts = JsonSerializer.SerializeToElement(new { broadcastId, ttsIds = new List<ulong> { entry.Id } });
                    _ = await _ttsService.HandlePlayRequestAsync(broadcastId, jsonTts, prepared.Tts.Where(t => t.Id == entry.Id).ToList(), new List<SpeakerInfo>(), channelId);
                    _logger.LogInformation("[ScheduleExecution] Playing TTS {TtsId} in ordered playlist (broadcast {BroadcastId})", entry.Id, broadcastId);
                    while (_mixer.HasActiveTtsStream(broadcastId))
                    {
                        await Task.Delay(200);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ScheduleExecution] Error playing playlist entry {EntryId} (broadcast {BroadcastId})", entry.Id, broadcastId);
            }
            _logger.LogInformation("[ScheduleExecution] Ordered playlist finished for broadcast {BroadcastId}", broadcastId);
        }

        // 3. 재생 종료 감시 (주문형 플레이리스트 사용 시 마지막 항목 종료 후 빠르게 통과)
        _logger.LogInformation("[ScheduleExecution] Waiting for playback completion for broadcast {BroadcastId}", broadcastId);
    }

    private async Task FinalizeBroadcastAsync(IServiceScope scope, ulong broadcastId, ulong channelId)
    {
        try { await _mediaService.StopMediaByBroadcastIdAsync(broadcastId); } catch { }
        try { await _ttsService.StopTtsByBroadcastIdAsync(broadcastId); } catch { }
        await _mixer.StopMixer(broadcastId);

        var db = scope.ServiceProvider.GetRequiredService<wicsContext>();
        var b = await db.Broadcasts.FirstOrDefaultAsync(x => x.Id == broadcastId);
        if (b != null) { b.OngoingYn = "N"; b.UpdatedAt = DateTime.UtcNow; }

        var ch = await db.Channels.FirstOrDefaultAsync(c => c.Id == channelId);
        if (ch != null) { ch.State = 0; ch.UpdatedAt = DateTime.UtcNow; }

        // (변경) 해당 채널의 스피커 소유권 레코드 하드삭제
        var removeOwnerships = await db.SpeakerOwnershipStates
            .Where(o => o.ChannelId == channelId)
            .ToListAsync();
        if (removeOwnerships.Count > 0)
        {
            db.SpeakerOwnershipStates.RemoveRange(removeOwnerships);
            _logger.LogInformation("[ScheduleExecution] Deleted {Count} speaker ownership record(s) for channel {ChannelId}", removeOwnerships.Count, channelId);
        }

        await db.SaveChangesAsync();
        _logger.LogInformation("[ScheduleExecution] Broadcast {BroadcastId} finalized for channel {ChannelId}", broadcastId, channelId);
    }
}
