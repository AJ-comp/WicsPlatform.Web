using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WicsPlatform.Server.Data;
using WicsPlatform.Server.Models.wics;
using WicsPlatform.Server.Middleware;
using WicsPlatform.Shared;

namespace WicsPlatform.Server.Services
{
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

            var channel = await db.Channels.AsNoTracking().FirstOrDefaultAsync(c => c.Id == channelId, ct)
                          ?? throw new InvalidOperationException($"Channel {channelId} not found");

            // Speakers: from selected groups (if provided) else from channel mappings
            List<SpeakerInfo> candidates;
            if (selectedGroupIds != null)
            {
                candidates = await (
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
            else
            {
                // Channel mappings: groups + direct speakers
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

                candidates = await (
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

            // Ownership resolution (priority based)
            var takeovers = new List<TakeoverInfo>();
            foreach (var sp in candidates)
            {
                var current = await db.SpeakerOwnershipStates.FirstOrDefaultAsync(o => o.SpeakerId == sp.Id && o.Ownership == "Y", ct);
                if (current == null)
                {
                    await db.SpeakerOwnershipStates.AddAsync(new SpeakerOwnershipState
                    {
                        SpeakerId = sp.Id,
                        ChannelId = channelId,
                        Ownership = "Y",
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    }, ct);
                    await db.SaveChangesAsync(ct);
                    sp.Active = true;
                    continue;
                }

                if (current.ChannelId == channelId)
                {
                    sp.Active = true; // already owned by this channel
                    continue;
                }

                var other = await db.Channels.AsNoTracking().FirstOrDefaultAsync(c => c.Id == current.ChannelId, ct);
                if (other == null)
                {
                    // release zombie ownership
                    current.Ownership = "N";
                    current.UpdatedAt = DateTime.UtcNow;
                    db.SpeakerOwnershipStates.Update(current);
                    await db.SaveChangesAsync(ct);

                    await db.SpeakerOwnershipStates.AddAsync(new SpeakerOwnershipState
                    {
                        SpeakerId = sp.Id,
                        ChannelId = channelId,
                        Ownership = "Y",
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    }, ct);
                    await db.SaveChangesAsync(ct);
                    sp.Active = true;
                    continue;
                }

                // priority comparison: higher value wins
                if (channel.Priority > other.Priority)
                {
                    // takeover
                    current.Ownership = "N";
                    current.UpdatedAt = DateTime.UtcNow;
                    db.SpeakerOwnershipStates.Update(current);
                    await db.SaveChangesAsync(ct);

                    await db.SpeakerOwnershipStates.AddAsync(new SpeakerOwnershipState
                    {
                        SpeakerId = sp.Id,
                        ChannelId = channelId,
                        Ownership = "Y",
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    }, ct);
                    await db.SaveChangesAsync(ct);

                    takeovers.Add(new TakeoverInfo(sp.Id, other.Id));
                    sp.Active = true;
                }
                else
                {
                    sp.Active = false; // keep inactive; other has equal/higher priority
                }
            }

            // Load media/tts
            var media = await (
                from mcm in db.MapChannelMedia.AsNoTracking()
                join m in db.Media.AsNoTracking() on mcm.MediaId equals m.Id
                where mcm.ChannelId == channelId && mcm.DeleteYn != "Y" && m.DeleteYn != "Y"
                orderby mcm.Id
                select new WebSocketMiddleware.MediaInfo { Id = m.Id, FileName = m.FileName, FullPath = m.FullPath }
            ).ToListAsync(ct);

            var tts = await (
                from mct in db.MapChannelTts.AsNoTracking()
                join t in db.Tts.AsNoTracking() on mct.TtsId equals t.Id
                where mct.ChannelId == channelId && mct.DeleteYn != "Y" && t.DeleteYn != "Y"
                orderby mct.Id
                select new WebSocketMiddleware.TtsInfo { Id = t.Id, Name = t.Name, Content = t.Content }
            ).ToListAsync(ct);

            // keep only active speakers for output
            var finalSpeakers = candidates.Where(s => s.Active).ToList();
            return new PreparedBroadcast(channel, finalSpeakers, media, tts, takeovers);
        }
    }
}
