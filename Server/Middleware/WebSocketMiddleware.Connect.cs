using Microsoft.EntityFrameworkCore;
using System.Net.WebSockets;
using System.Text.Json;
using WicsPlatform.Server.Contracts;
using WicsPlatform.Server.Data;
using WicsPlatform.Server.Models.wics;
using WicsPlatform.Server.Services;
using WicsPlatform.Shared;

namespace WicsPlatform.Server.Middleware;

public partial class WebSocketMiddleware
{
    private async Task HandleConnectAsync(WebSocket webSocket, string connectionId, ulong channelId, JsonElement root)
    {
        var req = JsonSerializer.Deserialize<ConnectBroadcastRequest>(root);
        if (req is null) return;

        var broadcastId = req.BroadcastId;
        var selectedGroupIds = req.SelectedGroupIds;

        using var scope = serviceScopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<wicsContext>();

        var onlineSpeakers = await GetOnlineSpeakersAsync(context, selectedGroupIds, channelId);
        var selectedMedia = await GetSelectedMediaAsync(context, channelId);
        var selectedTts = await GetSelectedTtsAsync(context, channelId);

        // 2. ✅ 각 스피커의 소유권 검증 및 Active 상태 설정
        await ValidateAndSetSpeakerOwnership(context, onlineSpeakers, channelId, broadcastId);

        // 3. 검증된 스피커가 없으면 연결 실패 처리
        if (!onlineSpeakers.Any())
        {
            logger.LogWarning($"No speakers available for broadcast {broadcastId}");
            await SendMessageAsync(webSocket, JsonSerializer.Serialize(new
            {
                type = "error",
                message = "No speakers available for broadcast"
            }));
            return;
        }

        logger.LogInformation($"Broadcast {broadcastId} - Found {onlineSpeakers.Count} online speakers for channel {channelId}");
        logger.LogInformation($"  - 선택된 미디어: {selectedMedia.Count}개");
        logger.LogInformation($"  - 선택된 TTS: {selectedTts.Count}개");

        // VPN 사용 스피커 로그 (디버깅용)
        var vpnSpeakers = onlineSpeakers.Where(s => s.UseVpn).ToList();
        if (vpnSpeakers.Any())
        {
            logger.LogInformation($"VPN speakers: {string.Join(", ", vpnSpeakers.Select(s => $"{s.Name}({s.Ip})"))}");
        }

        var session = new BroadcastSession
        {
            BroadcastId = broadcastId,
            ChannelId = channelId,
            ConnectionId = connectionId,
            StartTime = DateTime.UtcNow,
            SelectedGroupIds = selectedGroupIds,
            WebSocket = webSocket,
            OnlineSpeakers = onlineSpeakers,
            SelectedMedia = selectedMedia,
            SelectedTts = selectedTts,
        };

        _broadcastSessions[broadcastId] = session;


        // 믹서 초기화
        await audioMixingService.InitializeMixer(broadcastId, channelId, onlineSpeakers);

        // ✅ DB에서 채널의 볼륨 설정 읽어서 적용
        var channel = await context.Channels
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == channelId);

        if (channel != null)
        {
            await audioMixingService.SetVolume(broadcastId, AudioSource.Microphone, channel.MicVolume);
            await audioMixingService.SetVolume(broadcastId, AudioSource.Media, channel.MediaVolume);
            await audioMixingService.SetVolume(broadcastId, AudioSource.TTS, channel.TtsVolume);

            logger.LogInformation($"Volume settings applied: Mic={channel.MicVolume:F2}, Media={channel.MediaVolume:F2}, TTS={channel.TtsVolume:F2}");
        }

        logger.LogInformation($"Broadcast session created: {broadcastId}");
    }


    /// <summary>
    /// 스피커 소유권을 검증하고 Active 상태 설정
    /// </summary>
    private async Task ValidateAndSetSpeakerOwnership(
        wicsContext context,
        List<SpeakerInfo> onlineSpeakers,
        ulong requestingChannelId,
        ulong broadcastId)
    {
        var requestingChannel = await context.Channels
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == requestingChannelId);

        if (requestingChannel == null)
        {
            logger.LogError($"Channel {requestingChannelId} not found");
            return;
        }

        foreach (var speaker in onlineSpeakers)
        {
            // 기본값은 false (이미 설정되어 있음)
            speaker.Active = false;

            var result = await TryAcquireSpeakerOwnership(
                context, speaker, requestingChannel, broadcastId);

            if (result.Acquired)
            {
                speaker.Active = true;  // ✅ 소유권 획득한 경우만 true
                logger.LogInformation($"Speaker {speaker.Name} activated");
            }
            else
            {
                logger.LogInformation($"Speaker {speaker.Name} inactive: {result.Reason}");
            }
        }
    }

    /// <summary>
    /// 개별 스피커에 대한 소유권 획득 시도
    /// </summary>
    private async Task<(bool Acquired, string Reason)> TryAcquireSpeakerOwnership(
        wicsContext context,
        SpeakerInfo speaker,
        Channel requestingChannel,
        ulong broadcastId)
    {
        // 현재 소유권 상태 확인
        var currentOwnership = await context.SpeakerOwnershipStates
            .FirstOrDefaultAsync(sos =>
                sos.SpeakerId == speaker.Id &&
                sos.Ownership == "Y");

        // 점유한 채널이 없는 경우
        if (currentOwnership == null)
        {
            await CreateSpeakerOwnership(context, speaker.Id, requestingChannel.Id);
            return (true, "Ownership acquired");
        }

        // 점유한 채널이 있는 경우 - 우선순위 비교
        var occupyingChannel = await context.Channels
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == currentOwnership.ChannelId);

        if (occupyingChannel == null)
        {
            // 점유 채널이 없어진 경우 - 소유권 해제 후 재획득
            await ReleaseSpeakerOwnership(context, currentOwnership);
            await CreateSpeakerOwnership(context, speaker.Id, requestingChannel.Id);
            return (true, "Ownership acquired (previous channel not found)");
        }

        // 5. 우선순위 비교
        if (occupyingChannel.Priority <= requestingChannel.Priority)
        {
            // 기존 채널의 우선순위가 같거나 높음 - Skip
            return (false, $"Lower or equal priority than channel {occupyingChannel.Id} (Priority: {occupyingChannel.Priority})");
        }

        // 6. 요청 채널의 우선순위가 더 높음 - 기존 채널에서 스피커 비활성화
        await HandlePriorityTakeover(
            context, currentOwnership, speaker, requestingChannel, occupyingChannel.Id);

        return (true, $"Ownership taken from channel {occupyingChannel.Id}");
    }

    /// <summary>
    /// 우선순위에 의한 스피커 인계 처리
    /// </summary>
    private async Task HandlePriorityTakeover(
        wicsContext context,
        SpeakerOwnershipState currentOwnership,
        SpeakerInfo speaker,
        Channel requestingChannel,
        ulong previousChannelId)
    {
        // 1. 기존 소유권 해제
        await ReleaseSpeakerOwnership(context, currentOwnership);

        // 2. 메모리에서 기존 채널의 스피커 비활성화
        if (_broadcastSessions.TryGetValue(previousChannelId, out var previousSession))
        {
            var speakerToDeactivate = previousSession.OnlineSpeakers
                .FirstOrDefault(s => s.Id == speaker.Id);

            if (speakerToDeactivate != null)
            {
                speakerToDeactivate.Active = false;
                logger.LogInformation($"Speaker {speaker.Name} deactivated in channel {previousChannelId}");
            }
        }

        // 4. 새로운 소유권 생성
        await CreateSpeakerOwnership(context, speaker.Id, requestingChannel.Id);
    }

    /// <summary>
    /// 스피커 소유권 생성
    /// </summary>
    private async Task CreateSpeakerOwnership(wicsContext context, ulong speakerId, ulong channelId)
    {
        var ownership = new SpeakerOwnershipState
        {
            SpeakerId = speakerId,
            ChannelId = channelId,
            Ownership = "Y",
            CreatedAt = DateTime.Now,
            UpdatedAt = DateTime.Now
        };

        await context.SpeakerOwnershipStates.AddAsync(ownership);
        await context.SaveChangesAsync();
    }

    /// <summary>
    /// 스피커 소유권 해제
    /// </summary>
    private async Task ReleaseSpeakerOwnership(wicsContext context, SpeakerOwnershipState ownership)
    {
        ownership.Ownership = "N";
        ownership.UpdatedAt = DateTime.Now;

        context.SpeakerOwnershipStates.Update(ownership);
        await context.SaveChangesAsync();

        logger.LogDebug($"Released ownership: Speaker {ownership.SpeakerId} from Channel {ownership.ChannelId}");
    }


    private async Task HandleDisconnectAsync(JsonElement root)
    {
        var req = JsonSerializer.Deserialize<DisconnectBroadcastRequest>(root);

        await CleanupBroadcastSessionAsync(req.BroadcastId, true);
    }


    private async Task StartBroadCastAsync(ulong channelId)
    {
        using var scope = serviceScopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<wicsContext>();

        // ✅ 채널 상태를 1(방송 중)로 업데이트
        var channel = await context.Channels.FirstOrDefaultAsync(c => c.Id == channelId);
        if (channel != null)
        {
            channel.State = 1;  // 1 = 방송 중 상태
            channel.UpdatedAt = DateTime.Now;
            await context.SaveChangesAsync();

            logger.LogInformation($"Channel {channelId} state updated to 1 (Broadcasting)");
        }
    }


    // 선택된 그룹의 "온라인" 스피커만 조회해서 리턴
    private static Task<List<SpeakerInfo>> GetOnlineSpeakersAsync(
        wicsContext context, IEnumerable<ulong> selectedGroupIds, ulong channelId, CancellationToken ct = default)
    {
        return (
            from msg in context.MapSpeakerGroups.AsNoTracking()
            join s in context.Speakers.AsNoTracking() on msg.SpeakerId equals s.Id
            where selectedGroupIds.Contains(msg.GroupId)
                  && msg.LastYn == "Y"
                  && s.State == 1          // 온라인
                  && s.DeleteYn == "N"
            select new SpeakerInfo
            {
                Id = s.Id,
                Ip = s.VpnUseYn == "Y" ? s.VpnIp : s.Ip,
                Name = s.Name,
                ChannelId = channelId,
                UseVpn = s.VpnUseYn == "Y"
            }
        ).Distinct().ToListAsync(ct);
    }

    // 채널에 선택된 미디어 리스트 조회해서 리턴
    private static Task<List<MediaInfo>> GetSelectedMediaAsync(
        wicsContext context, ulong channelId, CancellationToken ct = default)
    {
        return (
            from mcm in context.MapChannelMedia.AsNoTracking()
            join m in context.Media.AsNoTracking() on mcm.MediaId equals m.Id
            where mcm.ChannelId == channelId
                  && mcm.DeleteYn != "Y"
                  && m.DeleteYn != "Y"
            orderby mcm.Id
            select new MediaInfo
            {
                Id = m.Id,
                FileName = m.FileName,
                FullPath = m.FullPath
            }
        ).ToListAsync(ct);
    }

    // 채널에 선택된 TTS 리스트 조회해서 리턴
    private static Task<List<TtsInfo>> GetSelectedTtsAsync(
        wicsContext context, ulong channelId, CancellationToken ct = default)
    {
        return (
            from mct in context.MapChannelTts.AsNoTracking()
            join t in context.Tts.AsNoTracking() on mct.TtsId equals t.Id
            where mct.ChannelId == channelId
                  && mct.DeleteYn != "Y"
                  && t.DeleteYn != "Y"
            orderby mct.Id
            select new TtsInfo
            {
                Id = t.Id,
                Name = t.Name,
                Content = t.Content
            }
        ).ToListAsync(ct);
    }
}
