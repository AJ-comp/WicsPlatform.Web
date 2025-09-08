using Microsoft.EntityFrameworkCore;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using WicsPlatform.Audio;
using WicsPlatform.Server.Contracts;
using WicsPlatform.Server.Data;
using WicsPlatform.Server.Services;

namespace WicsPlatform.Server.Middleware;

public partial class WebSocketMiddleware
{
    private readonly RequestDelegate next;
    private readonly ILogger<WebSocketMiddleware> logger;
    private readonly IServiceScopeFactory serviceScopeFactory;
    private readonly IUdpBroadcastService udpService;
    private readonly IMediaBroadcastService mediaBroadcastService;
    private readonly ITtsBroadcastService ttsBroadcastService;
    private readonly IAudioMixingService audioMixingService;
    private static readonly ConcurrentDictionary<ulong, BroadcastSession> _broadcastSessions = new();

    private readonly ushort MaxBuffer = 10000; // 최대 패킷 크기

    public WebSocketMiddleware(
        RequestDelegate next,
        ILogger<WebSocketMiddleware> logger,
        IServiceScopeFactory serviceScopeFactory,
        IUdpBroadcastService udpService,
        IMediaBroadcastService mediaBroadcastService,
        ITtsBroadcastService ttsBroadcastService,
        IAudioMixingService audioMixingService)
    {
        this.next = next;
        this.logger = logger;
        this.serviceScopeFactory = serviceScopeFactory;
        this.udpService = udpService;
        this.mediaBroadcastService = mediaBroadcastService;
        this.ttsBroadcastService = ttsBroadcastService;
        this.audioMixingService = audioMixingService;

        mediaBroadcastService.OnPlaybackCompleted += OnPlaybackCompleted;
        ttsBroadcastService.OnPlaybackCompleted += OnPlaybackCompleted;
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
            var sessionsToRemove = _broadcastSessions
                .Where(kvp => kvp.Value.ConnectionId == connectionId)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var broadcastId in sessionsToRemove)
            {
                if (_broadcastSessions.TryGetValue(broadcastId, out var session))
                {
                    // WebSocket 연결만 null로 설정
                    session.WebSocket = null;

                    // 마이크 스트림만 제거
                    await audioMixingService.RemoveMicrophoneStream(broadcastId);

                    // 미디어 재생 확인
                    var mediaStatus = await mediaBroadcastService.GetStatusByBroadcastIdAsync(broadcastId);

                    if (mediaStatus?.IsPlaying == true)
                    {
                        // 미디어 재생 중 - 세션 유지, WebSocket만 null
                        logger.LogInformation($"Client disconnected but media continues: {broadcastId}");
                    }
                    else
                    {
                        // 미디어도 없으면 전체 정리
                        await audioMixingService.StopMixer(broadcastId);
                        _broadcastSessions.TryRemove(broadcastId, out _);
                        logger.LogInformation($"Broadcast session fully cleaned up: {broadcastId}");
                    }
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
            logger.LogError(ex, "Error processing message");
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