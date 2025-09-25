using Microsoft.EntityFrameworkCore;
using WicsPlatform.Server.Contracts;
using WicsPlatform.Server.Data;
using WicsPlatform.Server.Models.wics;
using WicsPlatform.Shared;
using WicsPlatform.Server.Middleware;

namespace WicsPlatform.Server.Services;

public class ScheduleExecutionService : IScheduleExecutionService
{
    private readonly System.Threading.Channels.Channel<ulong> _queue;
    private readonly ILogger<ScheduleExecutionService> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IAudioMixingService _mixer;

    public ScheduleExecutionService(
        ILogger<ScheduleExecutionService> logger,
        IServiceScopeFactory scopeFactory,
        IAudioMixingService mixer)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
        _mixer = mixer;
        _queue = System.Threading.Channels.Channel.CreateUnbounded<ulong>();
        _ = Task.Run(WorkerLoop); // background consumer
    }

    public async Task EnqueueAsync(ulong scheduleId, CancellationToken cancellationToken = default)
    {
        await _queue.Writer.WriteAsync(scheduleId, cancellationToken);
    }

    private async Task WorkerLoop()
    {
        await foreach (var scheduleId in _queue.Reader.ReadAllAsync())
        {
            try
            {
                await ExecuteScheduleAsync(scheduleId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ScheduleExecution] Error executing schedule {ScheduleId}", scheduleId);
            }
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

            try
            {
                await RunBroadcastAsync(broadcastId, ch.Id, prepared, channelEntity);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ScheduleExecution] Error during mixer init or playback for broadcast {BroadcastId}", broadcastId);
            }
            finally
            {
                await FinalizeBroadcastAsync(scope, broadcastId, ch.Id);
            }
        }
    }

    // Helpers
    private static Task<List<WicsPlatform.Server.Models.wics.Channel>> GetScheduleChannelsAsync(wicsContext db, ulong scheduleId)
    {
        return db.Channels.AsNoTracking()
            .Where(c => c.ScheduleId == scheduleId && c.DeleteYn != "Y")
            .ToListAsync();
    }

    private static Task<bool> ChannelHasOngoingAsync(wicsContext db, ulong channelId)
    {
        return db.Broadcasts.AsNoTracking().AnyAsync(b => b.ChannelId == channelId && b.OngoingYn == "Y");
    }

    private async Task<(List<SpeakerInfo> Speakers, List<WebSocketMiddleware.MediaInfo> Media, List<WebSocketMiddleware.TtsInfo> Tts)> PrepareForChannelAsync(IServiceScope scope, ulong channelId)
    {
        var prepSvc = scope.ServiceProvider.GetRequiredService<IBroadcastPreparationService>();
        var prepared = await prepSvc.PrepareAsync(channelId, null);
        return (prepared.Speakers, prepared.Media, prepared.Tts);
    }

    private async Task<(ulong BroadcastId, WicsPlatform.Server.Models.wics.Channel ChannelEntity)> CreateBroadcastAsync(IServiceScope scope, ulong channelId,
        (List<SpeakerInfo> Speakers, List<WebSocketMiddleware.MediaInfo> Media, List<WebSocketMiddleware.TtsInfo> Tts) prepared)
    {
        var db = scope.ServiceProvider.GetRequiredService<wicsContext>();

        var broadcast = new Broadcast
        {
            ChannelId = channelId,
            SpeakerIdList = string.Join(' ', prepared.Speakers.Select(s => s.Id)),
            MediaIdList = string.Join(' ', prepared.Media.Select(m => m.Id)),
            TtsIdList = string.Join(' ', prepared.Tts.Select(t => t.Id)),
            LoopbackYn = "N",
            OngoingYn = "Y",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await db.Broadcasts.AddAsync(broadcast);
        var channelEntity = await db.Channels.FirstOrDefaultAsync(c => c.Id == channelId);
        if (channelEntity != null)
        {
            channelEntity.State = 1; // broadcasting
            channelEntity.UpdatedAt = DateTime.UtcNow;
        }
        await db.SaveChangesAsync();

        return (broadcast.Id, channelEntity!);
    }

    private async Task RunBroadcastAsync(ulong broadcastId, ulong channelId,
        (List<SpeakerInfo> Speakers, List<WebSocketMiddleware.MediaInfo> Media, List<WebSocketMiddleware.TtsInfo> Tts) prepared,
        WicsPlatform.Server.Models.wics.Channel channelEntity)
    {
        await _mixer.InitializeMixer(broadcastId, channelId, prepared.Speakers);
        await _mixer.SetVolume(broadcastId, AudioSource.Media, channelEntity?.MediaVolume ?? 1f);
        await _mixer.SetVolume(broadcastId, AudioSource.TTS, channelEntity?.TtsVolume ?? 1f);

        _logger.LogInformation("[ScheduleExecution] Holding mixer open (infinite wait) for channel {ChannelId}", channelId);

        while (_mixer.IsMixerActive(broadcastId))
        {
            await Task.Delay(1000);
        }
    }

    private async Task FinalizeBroadcastAsync(IServiceScope scope, ulong broadcastId, ulong channelId)
    {
        // Ensure mixer stopped
        await _mixer.StopMixer(broadcastId);

        var db = scope.ServiceProvider.GetRequiredService<wicsContext>();
        var b = await db.Broadcasts.FirstOrDefaultAsync(x => x.Id == broadcastId);
        if (b != null)
        {
            b.OngoingYn = "N";
            b.UpdatedAt = DateTime.UtcNow;
        }
        var ch = await db.Channels.FirstOrDefaultAsync(c => c.Id == channelId);
        if (ch != null)
        {
            ch.State = 0; // idle
            ch.UpdatedAt = DateTime.UtcNow;
        }
        await db.SaveChangesAsync();

        _logger.LogInformation("[ScheduleExecution] Broadcast {BroadcastId} finalized for channel {ChannelId}", broadcastId, channelId);
    }
}
