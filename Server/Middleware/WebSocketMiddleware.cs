using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using WicsPlatform.Server.Contracts;
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

            await CloseWebSocketOnError(webSocket, ex);
        }
        finally
        {
            Debug.WriteLine($"[WebSocket.finally] ========== WebSocket 종료 처리 시작 ==========");
            Debug.WriteLine($"[WebSocket.finally] ConnectionId: {connectionId}");
            var sessionsToRemove = _broadcastSessions
                .Where(kvp => kvp.Value.ConnectionId == connectionId)
                .Select(kvp => kvp.Key)
                .ToList();
            Debug.WriteLine($"[WebSocket.finally] 제거 대상 세션 수: {sessionsToRemove.Count}");

            foreach (var broadcastId in sessionsToRemove)
            {
                Debug.WriteLine($"[WebSocket.finally] BroadcastId {broadcastId} 처리 중...");
                if (_broadcastSessions.TryGetValue(broadcastId, out var session))
                {
                    Debug.WriteLine($"[WebSocket.finally] 세션 발견: BroadcastId={broadcastId}");
                    // WebSocket 연결만 null로 설정
                    session.WebSocket = null;
                    Debug.WriteLine($"[WebSocket.finally] WebSocket null로 설정");

                    // 마이크 스트림만 제거
                    await audioMixingService.RemoveMicrophoneStream(broadcastId);
                    Debug.WriteLine($"[WebSocket.finally] 마이크 스트림 제거 완료");

                    // 미디어 재생 확인
                    Debug.WriteLine($"[WebSocket.finally] 미디어 재생 상태 확인 중...");
                    var mediaStatus = await mediaBroadcastService.GetStatusByBroadcastIdAsync(broadcastId);
                    Debug.WriteLine($"[WebSocket.finally] 미디어 재생 상태: {mediaStatus?.IsPlaying}");

                    if (mediaStatus?.IsPlaying == true)
                    {
                        // 미디어 재생 중 - 세션 유지, WebSocket만 null
                        logger.LogInformation($"Client disconnected but media continues: {broadcastId}");
                        Debug.WriteLine($"[WebSocket.finally] ✅ 미디어 재생 중 → 세션 유지 (WebSocket만 null)");
                    }
                    else
                    {
                        // 미디어도 없으면 전체 정리
                        Debug.WriteLine($"[WebSocket.finally] 미디어 재생 안함 → 전체 정리 시작");
                        await audioMixingService.StopMixer(broadcastId);
                        _broadcastSessions.TryRemove(broadcastId, out _);
                        logger.LogInformation($"Broadcast session fully cleaned up: {broadcastId}");
                        Debug.WriteLine($"[WebSocket.finally] ✅ 세션 완전 정리 완료");
                    }
                }
            }
            Debug.WriteLine($"[WebSocket.finally] ========== WebSocket 종료 처리 완료 ==========");
        }
    }

    /// <summary>
    /// 예외 발생 시 WebSocket을 안전하게 종료
    /// </summary>
    private async Task CloseWebSocketOnError(WebSocket webSocket, Exception ex)
    {
        try
        {
            if (webSocket.State == WebSocketState.Open)
            {
                // 에러 메시지 전송
                var errorMessage = JsonSerializer.Serialize(new
                {
                    type = "error",
                    message = "Server error occurred",
                    details = ex.Message
                });

                var bytes = Encoding.UTF8.GetBytes(errorMessage);
                await webSocket.SendAsync(
                    new ArraySegment<byte>(bytes),
                    WebSocketMessageType.Text,
                    true,
                    CancellationToken.None);

                // WebSocket 종료
                await webSocket.CloseAsync(
                    WebSocketCloseStatus.InternalServerError,
                    "Server error",
                    CancellationToken.None);
            }
        }
        catch (Exception closeEx)
        {
            logger.LogError(closeEx, "Failed to close WebSocket after error");
        }
    }

    private async Task ProcessMessageAsync(WebSocket webSocket, string connectionId, ulong channelId, string message)
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

    private async Task SendMessageAsync(WebSocket webSocket, string message)
    {
        if (webSocket.State == WebSocketState.Open)
        {
            var bytes = Encoding.UTF8.GetBytes(message);
            await webSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
        }
    }
}