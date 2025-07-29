using System;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using WicsPlatform.Server.Data;
using WicsPlatform.Server.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace WicsPlatform.Server.Middleware
{
    public class WebSocketMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<WebSocketMiddleware> _logger;
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly IUdpBroadcastService _udpService;
        private static readonly ConcurrentDictionary<string, BroadcastSession> _broadcastSessions = new();

        public WebSocketMiddleware(
            RequestDelegate next,
            ILogger<WebSocketMiddleware> logger,
            IServiceScopeFactory serviceScopeFactory,
            IUdpBroadcastService udpService)
        {
            _next = next;
            _logger = logger;
            _serviceScopeFactory = serviceScopeFactory;
            _udpService = udpService;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            if (context.Request.Path.StartsWithSegments("/broadcast") && context.WebSockets.IsWebSocketRequest)
            {
                var pathSegments = context.Request.Path.Value.Split('/');
                if (pathSegments.Length >= 3 && ulong.TryParse(pathSegments[2], out var channelId))
                {
                    await HandleWebSocketAsync(context, channelId);
                }
                else
                {
                    context.Response.StatusCode = 400;
                }
            }
            else
            {
                await _next(context);
            }
        }

        private async Task HandleWebSocketAsync(HttpContext context, ulong channelId)
        {
            using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
            var connectionId = Guid.NewGuid().ToString();

            _logger.LogInformation($"WebSocket connected: {connectionId} for channel: {channelId}");

            var buffer = new ArraySegment<byte>(new byte[4096]);
            var cancellationToken = context.RequestAborted;

            try
            {
                while (webSocket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
                {
                    var result = await webSocket.ReceiveAsync(buffer, cancellationToken);

                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        var message = Encoding.UTF8.GetString(buffer.Array, 0, result.Count);
                        await ProcessMessageAsync(webSocket, connectionId, channelId, message);
                    }
                    else if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await webSocket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"WebSocket error for connection: {connectionId}");
            }
            finally
            {
                // 연결 종료 시 세션 정리
                var sessionsToRemove = _broadcastSessions
                    .Where(kvp => kvp.Value.ConnectionId == connectionId)
                    .Select(kvp => kvp.Key)
                    .ToList();

                foreach (var broadcastId in sessionsToRemove)
                {
                    if (_broadcastSessions.TryRemove(broadcastId, out var session))
                    {
                        _logger.LogInformation($"Removed broadcast session: {broadcastId}");
                    }
                }
            }
        }

        private async Task ProcessMessageAsync(WebSocket webSocket, string connectionId, ulong channelId, string message)
        {
            try
            {
                var jsonDoc = JsonDocument.Parse(message);
                var root = jsonDoc.RootElement;

                if (root.TryGetProperty("type", out var typeElement))
                {
                    var messageType = typeElement.GetString();

                    switch (messageType)
                    {
                        case "connect":
                            await HandleConnectAsync(webSocket, connectionId, channelId, root);
                            break;
                        case "disconnect":
                            await HandleDisconnectAsync(root);
                            break;
                        case "audio":
                            await HandleAudioDataAsync(webSocket, root);
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing message");
            }
        }

        private async Task HandleConnectAsync(WebSocket webSocket, string connectionId, ulong channelId, JsonElement root)
        {
            if (root.TryGetProperty("broadcastId", out var broadcastIdElement))
            {
                var broadcastId = broadcastIdElement.GetString();
                var selectedGroupIds = root.GetProperty("selectedGroupIds").EnumerateArray()
                    .Select(e => e.GetUInt64())
                    .ToList();

                // ⭐ DB에서 온라인 스피커 정보를 미리 로드
                List<SpeakerInfo> onlineSpeakers;
                using (var scope = _serviceScopeFactory.CreateScope())
                {
                    var context = scope.ServiceProvider.GetRequiredService<wicsContext>();

                    // 선택된 그룹의 온라인 스피커만 조회
                    onlineSpeakers = await (
                        from msg in context.MapSpeakerGroups
                        join s in context.Speakers on msg.SpeakerId equals s.Id
                        where selectedGroupIds.Contains(msg.GroupId)
                              && msg.LastYn == "Y"
                              && s.State == 1  // 온라인 상태
                              && s.DeleteYn == "N"
                        select new SpeakerInfo
                        {
                            Id = s.Id,
                            Ip = s.VpnUseYn == "Y" ? s.VpnIp : s.Ip,  // ⭐ VPN 사용 여부에 따라 IP 선택
                            Name = s.Name,
                            ChannelId = channelId,
                            UseVpn = s.VpnUseYn == "Y"  // ⭐ VPN 사용 여부 정보도 저장 (디버깅용)
                        }
                    ).Distinct().ToListAsync();
                }

                _logger.LogInformation($"Broadcast {broadcastId} - Found {onlineSpeakers.Count} online speakers for channel {channelId}");

                // VPN 사용 스피커 로그 (디버깅용)
                var vpnSpeakers = onlineSpeakers.Where(s => s.UseVpn).ToList();
                if (vpnSpeakers.Any())
                {
                    _logger.LogInformation($"VPN speakers: {string.Join(", ", vpnSpeakers.Select(s => $"{s.Name}({s.Ip})"))}");
                }

                var session = new BroadcastSession
                {
                    BroadcastId = broadcastId,
                    ChannelId = channelId,
                    ConnectionId = connectionId,
                    StartTime = DateTime.UtcNow,
                    SelectedGroupIds = selectedGroupIds,
                    WebSocket = webSocket,
                    OnlineSpeakers = onlineSpeakers  // ⭐ 스피커 정보 저장
                };

                _broadcastSessions[broadcastId] = session;

                // 연결 확인 메시지 전송
                var response = new
                {
                    type = "connected",
                    broadcastId = broadcastId,
                    channelId = channelId,
                    onlineSpeakerCount = onlineSpeakers.Count,
                    status = "ok"
                };

                await SendMessageAsync(webSocket, JsonSerializer.Serialize(response));
            }
        }

        private async Task HandleDisconnectAsync(JsonElement root)
        {
            if (root.TryGetProperty("broadcastId", out var broadcastIdElement))
            {
                var broadcastId = broadcastIdElement.GetString();

                if (_broadcastSessions.TryRemove(broadcastId, out var session))
                {
                    _logger.LogInformation($"Broadcast disconnected: {broadcastId}");
                }
            }
        }

        private async Task HandleAudioDataAsync(WebSocket webSocket, JsonElement root)
        {
            if (root.TryGetProperty("broadcastId", out var broadcastIdElement))
            {
                var broadcastId = broadcastIdElement.GetString();

                if (_broadcastSessions.TryGetValue(broadcastId, out var session))
                {
                    session.PacketCount++;

                    if (root.TryGetProperty("data", out var dataElement))
                    {
                        var base64Data = dataElement.GetString();
                        var audioData = Convert.FromBase64String(base64Data);
                        session.TotalBytes += audioData.Length;

                        // ⭐ UDP로 스피커에 오디오 전송 (DB 조회 없이 메모리에서 직접)
                        if (session.OnlineSpeakers?.Any() == true)
                        {
                            await _udpService.SendAudioToSpeakers(session.OnlineSpeakers, audioData);
                        }
                    }

                    // 주기적으로 상태 업데이트 전송 (10번째 패킷마다)
                    if (session.PacketCount % 10 == 0)
                    {
                        var status = new
                        {
                            type = "status",
                            broadcastId = broadcastId,
                            packetCount = session.PacketCount,
                            totalBytes = session.TotalBytes,
                            durationSeconds = (DateTime.UtcNow - session.StartTime).TotalSeconds,
                            speakersActive = session.OnlineSpeakers?.Count ?? 0
                        };

                        await SendMessageAsync(webSocket, JsonSerializer.Serialize(status));
                    }
                }
            }
        }

        private async Task SendMessageAsync(WebSocket webSocket, string message)
        {
            if (webSocket.State == WebSocketState.Open)
            {
                var bytes = Encoding.UTF8.GetBytes(message);
                await webSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
            }
        }

        private class BroadcastSession
        {
            public string BroadcastId { get; set; }
            public ulong ChannelId { get; set; }
            public string ConnectionId { get; set; }
            public DateTime StartTime { get; set; }
            public List<ulong> SelectedGroupIds { get; set; }
            public long PacketCount { get; set; }
            public long TotalBytes { get; set; }
            public WebSocket WebSocket { get; set; }
            public List<SpeakerInfo> OnlineSpeakers { get; set; }  // ⭐ 온라인 스피커 정보
        }
    }
}