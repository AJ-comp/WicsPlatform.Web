using Microsoft.EntityFrameworkCore;
using WicsPlatform.Server.Data;
using WicsPlatform.Server.Middleware;
using WicsPlatform.Server.Models.wics;

namespace WicsPlatform.Server.Services;

public class BroadcastPreparationService : IBroadcastPreparationService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BroadcastPreparationService> _logger;

    public BroadcastPreparationService(IServiceScopeFactory scopeFactory, ILogger<BroadcastPreparationService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task<PreparedBroadcast> PrepareAsync(ulong channelId, IEnumerable<ulong>? selectedGroupIds = null, CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<wicsContext>();

        var channel = await GetChannelAsync(db, channelId, ct);
        var candidates = selectedGroupIds != null
            ? await GetCandidateSpeakersFromSelectedGroupsAsync(db, channelId, selectedGroupIds, ct)
            : await GetCandidateSpeakersFromChannelMappingsAsync(db, channelId, ct);

        var takeovers = await ResolveOwnershipAsync(db, channel, candidates, ct);

        List<WebSocketMiddleware.MediaInfo> media;
        List<WebSocketMiddleware.TtsInfo> tts;
        List<PlaylistEntry>? ordered = null;

        if (channel.ScheduleId.HasValue)
        {
            // 예약 방송: schedule_play 테이블에서 순서를 유지하며 로드
            (media, tts, ordered) = await LoadContentFromSchedulePlayAsync(db, channel.ScheduleId.Value, ct);
        }
        else
        {
            media = await LoadMediaAsync(db, channelId, ct);
            tts = await LoadTtsAsync(db, channelId, ct);
        }

        var finalSpeakers = candidates.Where(s => s.Active).ToList();
        return new PreparedBroadcast(channel, finalSpeakers, media, tts, takeovers)
        {
            OrderedPlaylist = ordered // (확장: PreparedBroadcast 모델에 OrderedPlaylist 프로퍼티가 있어야 함)
        };
    }

    // -------- Channel --------
    private static async Task<Channel> GetChannelAsync(wicsContext db, ulong channelId, CancellationToken ct)
    {
        return await db.Channels.AsNoTracking().FirstOrDefaultAsync(c => c.Id == channelId, ct)
               ?? throw new InvalidOperationException($"Channel {channelId} not found");
    }

    // -------- Speaker Candidates (Selected Groups) --------
    private static async Task<List<SpeakerInfo>> GetCandidateSpeakersFromSelectedGroupsAsync(
        wicsContext db,
        ulong channelId,
        IEnumerable<ulong> selectedGroupIds,
        CancellationToken ct)
    {
        return await (
            from msg in db.MapSpeakerGroups.AsNoTracking()
            join s in db.Speakers.AsNoTracking() on msg.SpeakerId equals s.Id
            where selectedGroupIds.Contains(msg.GroupId) && msg.LastYn == "Y" && s.State == 1 && s.DeleteYn == "N"
            select new SpeakerInfo
            {
                Id = s.Id,
                Ip = s.VpnUseYn == "Y" ? s.VpnIp : s.Ip,
                Name = s.Name,
                ChannelId = channelId,
                UseVpn = s.VpnUseYn == "Y",
                Active = false
            }
        ).Distinct().ToListAsync(ct);
    }

    // -------- Speaker Candidates (Channel Mappings) --------
    private static async Task<List<SpeakerInfo>> GetCandidateSpeakersFromChannelMappingsAsync(
        wicsContext db,
        ulong channelId,
        CancellationToken ct)
    {
        var groupSpeakerIds = await (
            from mcg in db.MapChannelGroups.AsNoTracking()
            join msg in db.MapSpeakerGroups.AsNoTracking() on mcg.GroupId equals msg.GroupId
            join s in db.Speakers.AsNoTracking() on msg.SpeakerId equals s.Id
            where mcg.ChannelId == channelId && mcg.DeleteYn != "Y" && msg.LastYn == "Y" && s.State == 1 && s.DeleteYn == "N"
            select s.Id
        ).Distinct().ToListAsync(ct);

        var directSpeakerIds = await (
            from mcs in db.MapChannelSpeakers.AsNoTracking()
            join s in db.Speakers.AsNoTracking() on mcs.SpeakerId equals s.Id
            where mcs.ChannelId == channelId && s.State == 1 && s.DeleteYn == "N"
            select s.Id
        ).Distinct().ToListAsync(ct);

        var allIds = groupSpeakerIds.Union(directSpeakerIds).Distinct().ToList();

        return await (
            from s in db.Speakers.AsNoTracking()
            where allIds.Contains(s.Id)
            select new SpeakerInfo
            {
                Id = s.Id,
                Ip = s.VpnUseYn == "Y" ? s.VpnIp : s.Ip,
                Name = s.Name,
                ChannelId = channelId,
                UseVpn = s.VpnUseYn == "Y",
                Active = false
            }
        ).ToListAsync(ct);
    }

    // -------- Ownership Resolution --------
    private async Task<List<TakeoverInfo>> ResolveOwnershipAsync(
        wicsContext db,
        Channel channel,
        List<SpeakerInfo> candidates,
        CancellationToken ct)
    {
        var takeovers = new List<TakeoverInfo>();

        foreach (var sp in candidates)
        {
            var current = await db.SpeakerOwnershipStates.FirstOrDefaultAsync(o => o.SpeakerId == sp.Id && o.Ownership == "Y", ct);

            if (current == null)
            {
                await GrantOwnershipAsync(db, sp.Id, channel.Id, ct);
                sp.Active = true;
                continue;
            }

            if (current.ChannelId == channel.Id)
            {
                sp.Active = true; // already owned
                continue;
            }

            var otherChannel = await db.Channels.AsNoTracking().FirstOrDefaultAsync(c => c.Id == current.ChannelId, ct);
            if (otherChannel == null)
            {
                // release zombie & grant
                current.Ownership = "N";
                current.UpdatedAt = DateTime.UtcNow;
                db.SpeakerOwnershipStates.Update(current);
                await db.SaveChangesAsync(ct);

                await GrantOwnershipAsync(db, sp.Id, channel.Id, ct);
                sp.Active = true;
                continue;
            }

            if (channel.Priority > otherChannel.Priority)
            {
                // takeover
                current.Ownership = "N";
                current.UpdatedAt = DateTime.UtcNow;
                db.SpeakerOwnershipStates.Update(current);
                await db.SaveChangesAsync(ct);

                await GrantOwnershipAsync(db, sp.Id, channel.Id, ct);
                takeovers.Add(new TakeoverInfo(sp.Id, otherChannel.Id));
                sp.Active = true;
            }
            else
            {
                sp.Active = false; // keep inactive
            }
        }

        return takeovers;
    }

    private static async Task GrantOwnershipAsync(wicsContext db, ulong speakerId, ulong channelId, CancellationToken ct)
    {
        await db.SpeakerOwnershipStates.AddAsync(new SpeakerOwnershipState
        {
            SpeakerId = speakerId,
            ChannelId = channelId,
            Ownership = "Y",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        }, ct);
        await db.SaveChangesAsync(ct);
    }

    // -------- Media / TTS Loading --------
    private static async Task<List<WebSocketMiddleware.MediaInfo>> LoadMediaAsync(wicsContext db, ulong channelId, CancellationToken ct)
    {
        return await (
            from mcm in db.MapChannelMedia.AsNoTracking()
            join m in db.Media.AsNoTracking() on mcm.MediaId equals m.Id
            where mcm.ChannelId == channelId && mcm.DeleteYn != "Y" && m.DeleteYn != "Y"
            orderby mcm.Id
            select new WebSocketMiddleware.MediaInfo { Id = m.Id, FileName = m.FileName, FullPath = m.FullPath }
        ).ToListAsync(ct);
    }

    private static async Task<List<WebSocketMiddleware.TtsInfo>> LoadTtsAsync(wicsContext db, ulong channelId, CancellationToken ct)
    {
        return await (
            from mct in db.MapChannelTts.AsNoTracking()
            join t in db.Tts.AsNoTracking() on mct.TtsId equals t.Id
            where mct.ChannelId == channelId && mct.DeleteYn != "Y" && t.DeleteYn != "Y"
            orderby mct.Id
            select new WebSocketMiddleware.TtsInfo { Id = t.Id, Name = t.Name, Content = t.Content }
        ).ToListAsync(ct);
    }

    private static async Task<(List<WebSocketMiddleware.MediaInfo> media, List<WebSocketMiddleware.TtsInfo> tts, List<PlaylistEntry> ordered)> LoadContentFromSchedulePlayAsync(
        wicsContext db, ulong scheduleId, CancellationToken ct)
    {
        var plays = await db.SchedulePlays.AsNoTracking()
            .Where(p => p.ScheduleId == scheduleId && p.DeleteYn != "Y")
            .OrderBy(p => p.Id) // Id 오름차순이 재생 순서
            .ToListAsync(ct);

        // Collect unique ids
        var mediaIds = plays.Where(p => p.MediaId.HasValue).Select(p => p.MediaId!.Value).Distinct().ToList();
        var ttsIds = plays.Where(p => p.TtsId.HasValue).Select(p => p.TtsId!.Value).Distinct().ToList();

        var mediaDict = await db.Media.AsNoTracking()
            .Where(m => mediaIds.Contains(m.Id) && m.DeleteYn != "Y")
            .Select(m => new WebSocketMiddleware.MediaInfo { Id = m.Id, FileName = m.FileName, FullPath = m.FullPath })
            .ToDictionaryAsync(m => m.Id, ct);

        var ttsDict = await db.Tts.AsNoTracking()
            .Where(t => ttsIds.Contains(t.Id) && t.DeleteYn != "Y")
            .Select(t => new WebSocketMiddleware.TtsInfo { Id = t.Id, Name = t.Name, Content = t.Content })
            .ToDictionaryAsync(t => t.Id, ct);

        var ordered = new List<PlaylistEntry>();
        foreach (var p in plays)
        {
            if (p.MediaId.HasValue && mediaDict.TryGetValue(p.MediaId.Value, out var mi))
            {
                ordered.Add(new PlaylistEntry(mi));
            }
            else if (p.TtsId.HasValue && ttsDict.TryGetValue(p.TtsId.Value, out var ti))
            {
                ordered.Add(new PlaylistEntry(ti));
            }
        }

        var media = mediaDict.Values.ToList();
        var tts = ttsDict.Values.ToList();
        return (media, tts, ordered);
    }
}
