using System.Net.WebSockets;
using System.Text.Json;

namespace WicsPlatform.Server.Middleware
{
    public partial class WebSocketMiddleware
    {
        private async Task HandleMediaPlayAsync(WebSocket webSocket, JsonElement root)
        {
            try
            {
                if (!root.TryGetProperty("broadcastId", out var broadcastIdElement))
                {
                    // 기존 SendMessageAsync 사용
                    var errorResponse = new
                    {
                        type = "error",
                        message = "Missing broadcastId"
                    };
                    await SendMessageAsync(webSocket, JsonSerializer.Serialize(errorResponse));
                    return;
                }

                var broadcastId = broadcastIdElement.GetString();

                if (!_broadcastSessions.TryGetValue(broadcastId, out var session))
                {
                    // 기존 SendMessageAsync 사용
                    var errorResponse = new
                    {
                        type = "error",
                        message = "Session not found"
                    };
                    await SendMessageAsync(webSocket, JsonSerializer.Serialize(errorResponse));
                    return;
                }

                // MediaBroadcastService에 직접 전달
                var result = await mediaBroadcastService.HandlePlayRequestAsync(
                    broadcastId,
                    root,
                    session.SelectedMedia,
                    session.OnlineSpeakers,
                    session.ChannelId
                );

                // 응답 전송 (기존 SendMessageAsync 사용)
                var response = new
                {
                    type = "media_play_started",
                    broadcastId,
                    sessionId = result.SessionId,
                    success = result.Success,
                    message = result.Message,
                    mediaFiles = result.MediaFiles
                };

                await SendMessageAsync(webSocket, JsonSerializer.Serialize(response));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in HandleMediaPlayAsync");

                // 에러 응답도 기존 SendMessageAsync 사용
                var errorResponse = new
                {
                    type = "error",
                    message = ex.Message
                };
                await SendMessageAsync(webSocket, JsonSerializer.Serialize(errorResponse));
            }
        }

        private async Task HandleMediaStopAsync(WebSocket webSocket, JsonElement root)
        {
            try
            {
                if (!root.TryGetProperty("broadcastId", out var broadcastIdElement))
                {
                    // 기존 SendMessageAsync 사용
                    var errorResponse = new
                    {
                        type = "error",
                        message = "Missing broadcastId"
                    };
                    await SendMessageAsync(webSocket, JsonSerializer.Serialize(errorResponse));
                    return;
                }

                var broadcastId = broadcastIdElement.GetString();

                // MediaBroadcastService에 위임
                var success = await mediaBroadcastService.StopMediaByBroadcastIdAsync(broadcastId);

                var response = new
                {
                    type = "media_stopped",
                    broadcastId = broadcastId,
                    success = success
                };

                await SendMessageAsync(webSocket, JsonSerializer.Serialize(response));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in HandleMediaStopAsync");

                // 에러 응답도 기존 SendMessageAsync 사용
                var errorResponse = new
                {
                    type = "error",
                    message = ex.Message
                };
                await SendMessageAsync(webSocket, JsonSerializer.Serialize(errorResponse));
            }
        }
    }
}
