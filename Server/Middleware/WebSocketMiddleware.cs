using Microsoft.EntityFrameworkCore;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using WicsPlatform.Audio;
using WicsPlatform.Server.Contracts;
using WicsPlatform.Server.Data;
using WicsPlatform.Server.Services;

namespace WicsPlatform.Server.Middleware
{
    public partial class WebSocketMiddleware
    {
        private readonly RequestDelegate next;
        private readonly OpusCodec opusCodec;
        private readonly ILogger<WebSocketMiddleware> logger;
        private readonly IServiceScopeFactory serviceScopeFactory;
        private readonly IUdpBroadcastService udpService;
        private readonly IMediaBroadcastService mediaBroadcastService;
        private readonly IAudioMixingService audioMixingService;
        private static readonly ConcurrentDictionary<string, BroadcastSession> _broadcastSessions = new();

        private readonly ushort MaxBuffer = 10000; // 최대 패킷 크기

        public WebSocketMiddleware(
            RequestDelegate next,
            OpusCodec opusCodec,
            ILogger<WebSocketMiddleware> logger,
            IServiceScopeFactory serviceScopeFactory,
            IUdpBroadcastService udpService,
            IMediaBroadcastService mediaBroadcastService,
            IAudioMixingService audioMixingService)
        {
            this.next = next;
            this.opusCodec = opusCodec;
            this.logger = logger;
            this.serviceScopeFactory = serviceScopeFactory;
            this.udpService = udpService;
            this.mediaBroadcastService = mediaBroadcastService;
            this.audioMixingService = audioMixingService;
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
                await next(context);
            }
        }

        private async Task HandleWebSocketAsync(HttpContext context, ulong channelId)
        {
            using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
            var connectionId = Guid.NewGuid().ToString();

            logger.LogInformation($"WebSocket connected: {connectionId} for channel: {channelId}");

            var buffer = new ArraySegment<byte>(new byte[MaxBuffer]);
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
                logger.LogError(ex, $"WebSocket error for connection: {connectionId}");
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
                        logger.LogInformation($"Removed broadcast session and cleaned up OpusCodec: {broadcastId}");
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
                        case "media_play":
                            await HandleMediaPlayAsync(webSocket, root);
                            break;
                        case "media_stop":
                            await HandleMediaStopAsync(webSocket, root);
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error processing message");
            }
        }

        private async Task HandleAudioDataAsync(WebSocket webSocket, JsonElement root)
        {
            if (!root.TryGetProperty("broadcastId", out var broadcastIdElement)) return;
            var broadcastId = broadcastIdElement.GetString();
            if (!_broadcastSessions.TryGetValue(broadcastId, out var session)) return;

            session.PacketCount++;

            if (!root.TryGetProperty("data", out var dataElement)) return;

            var base64Data = dataElement.GetString();
            var opusData = Convert.FromBase64String(base64Data);
            session.TotalBytes += opusData.Length;

            if (session.OnlineSpeakers?.Any() != true) return;

            try
            {
                // UDP로 압축된 데이터 전송
                await udpService.SendAudioToSpeakers(session.OnlineSpeakers, opusData);
//                await audioMixingService.AddMicrophoneData(broadcastId, opusCodec.Decode(opusData));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, $"Opus encoding failed for broadcast {broadcastId}");
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
    }
}