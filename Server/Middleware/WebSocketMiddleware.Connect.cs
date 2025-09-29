using Microsoft.EntityFrameworkCore;
using System.Net.WebSockets;
using System.Text.Json;
using WicsPlatform.Server.Contracts;
using WicsPlatform.Server.Data;
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

        // 공통 준비 로직 호출 (스피커 소유권/우선순위/콘텐츠 조회)
        var prepService = scope.ServiceProvider.GetRequiredService<IBroadcastPreparationService>();
        var prepared = await prepService.PrepareAsync(channelId, selectedGroupIds);

        var onlineSpeakers = prepared.Speakers;
        var selectedMedia = prepared.Media;
        var selectedTts = prepared.Tts;

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

        if (prepared.Takeovers.Any())
        {
            foreach (var t in prepared.Takeovers)
            {
                logger.LogInformation($"Speaker {t.SpeakerId} taken over from channel {t.PreviousChannelId}");
            }
        }

        logger.LogInformation($"Broadcast session created: {broadcastId}");
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
}
