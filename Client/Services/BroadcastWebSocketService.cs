using System;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;

namespace WicsPlatform.Client.Services
{
    public class BroadcastWebSocketService : IAsyncDisposable
    {
        private readonly NavigationManager _navigationManager;
        private readonly ILogger<BroadcastWebSocketService> _logger;
        private readonly Dictionary<string, ChannelWebSocket> _channelWebSockets = new();

        public event Action<string, BroadcastStatus> OnBroadcastStatusReceived;
        public event Action<string, string> OnConnectionStatusChanged;

        public BroadcastWebSocketService(NavigationManager navigationManager, ILogger<BroadcastWebSocketService> logger)
        {
            _navigationManager = navigationManager;
            _logger = logger;
        }

        public async Task<StartBroadcastResponse> StartBroadcastAsync(ulong channelId, List<ulong> selectedGroupIds)
        {
            try
            {
                var broadcastId = Guid.NewGuid().ToString();
                var wsUrl = GetWebSocketUrl($"broadcast/{channelId}");

                var channelWs = new ChannelWebSocket
                {
                    BroadcastId = broadcastId,
                    ChannelId = channelId,
                    SelectedGroupIds = selectedGroupIds
                };

                // WebSocket 연결
                channelWs.WebSocket = new ClientWebSocket();
                await channelWs.WebSocket.ConnectAsync(new Uri(wsUrl), channelWs.CancellationTokenSource.Token);

                _channelWebSockets[broadcastId] = channelWs;

                // 연결 성공 메시지 전송
                var connectMessage = new
                {
                    type = "connect",
                    broadcastId = broadcastId,
                    channelId = channelId,
                    selectedGroupIds = selectedGroupIds
                };

                await SendMessageAsync(channelWs.WebSocket, JsonSerializer.Serialize(connectMessage));

                // 수신 작업 시작
                _ = Task.Run(() => ReceiveLoop(broadcastId, channelWs), channelWs.CancellationTokenSource.Token);

                OnConnectionStatusChanged?.Invoke(broadcastId, "Connected");

                return new StartBroadcastResponse
                {
                    Success = true,
                    BroadcastId = broadcastId
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start broadcast");
                return new StartBroadcastResponse
                {
                    Success = false,
                    Error = ex.Message
                };
            }
        }

        public async Task<StopBroadcastResponse> StopBroadcastAsync(string broadcastId)
        {
            try
            {
                if (_channelWebSockets.TryGetValue(broadcastId, out var channelWs))
                {
                    // 연결 종료 메시지 전송
                    if (channelWs.WebSocket.State == WebSocketState.Open)
                    {
                        var disconnectMessage = new
                        {
                            type = "disconnect",
                            broadcastId = broadcastId
                        };

                        await SendMessageAsync(channelWs.WebSocket, JsonSerializer.Serialize(disconnectMessage));
                        await channelWs.WebSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Broadcast stopped", CancellationToken.None);
                    }

                    channelWs.CancellationTokenSource.Cancel();
                    channelWs.WebSocket?.Dispose();
                    _channelWebSockets.Remove(broadcastId);

                    OnConnectionStatusChanged?.Invoke(broadcastId, "Disconnected");
                }

                return new StopBroadcastResponse { Success = true };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to stop broadcast");
                return new StopBroadcastResponse
                {
                    Success = false,
                    Error = ex.Message
                };
            }
        }

        public async Task SendAudioDataAsync(string broadcastId, byte[] audioData)
        {
            if (_channelWebSockets.TryGetValue(broadcastId, out var channelWs))
            {
                if (channelWs.WebSocket.State == WebSocketState.Open)
                {
                    var message = new
                    {
                        type = "audio",
                        broadcastId = broadcastId,
                        data = Convert.ToBase64String(audioData),
                        timestamp = DateTime.UtcNow
                    };

                    await SendMessageAsync(channelWs.WebSocket, JsonSerializer.Serialize(message));
                }
            }
        }

        private async Task SendMessageAsync(ClientWebSocket webSocket, string message)
        {
            var bytes = Encoding.UTF8.GetBytes(message);
            await webSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
        }

        private async Task ReceiveLoop(string broadcastId, ChannelWebSocket channelWs)
        {
            var buffer = new ArraySegment<byte>(new byte[4096]);

            try
            {
                while (channelWs.WebSocket.State == WebSocketState.Open && !channelWs.CancellationTokenSource.Token.IsCancellationRequested)
                {
                    var result = await channelWs.WebSocket.ReceiveAsync(buffer, channelWs.CancellationTokenSource.Token);

                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        var message = Encoding.UTF8.GetString(buffer.Array, 0, result.Count);
                        ProcessMessage(broadcastId, message);
                    }
                    else if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await channelWs.WebSocket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error in receive loop for broadcast {broadcastId}");
            }
            finally
            {
                OnConnectionStatusChanged?.Invoke(broadcastId, "Disconnected");
            }
        }

        private void ProcessMessage(string broadcastId, string message)
        {
            try
            {
                var jsonDoc = JsonDocument.Parse(message);
                var root = jsonDoc.RootElement;

                if (root.TryGetProperty("type", out var typeElement))
                {
                    var messageType = typeElement.GetString();

                    if (messageType == "status" && _channelWebSockets.TryGetValue(broadcastId, out var channelWs))
                    {
                        var status = new BroadcastStatus
                        {
                            BroadcastId = broadcastId,
                            PacketCount = root.GetProperty("packetCount").GetInt64(),
                            TotalBytes = root.GetProperty("totalBytes").GetInt64(),
                            Duration = TimeSpan.FromSeconds(root.GetProperty("durationSeconds").GetDouble())
                        };

                        OnBroadcastStatusReceived?.Invoke(broadcastId, status);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing message");
            }
        }

        private string GetWebSocketUrl(string path)
        {
            var uri = new Uri(_navigationManager.BaseUri);
            var scheme = uri.Scheme == "https" ? "wss" : "ws";
            return $"{scheme}://{uri.Host}:{uri.Port}/{path}";
        }

        public async ValueTask DisposeAsync()
        {
            foreach (var kvp in _channelWebSockets)
            {
                await StopBroadcastAsync(kvp.Key);
            }
            _channelWebSockets.Clear();
        }

        private class ChannelWebSocket
        {
            public string BroadcastId { get; set; }
            public ulong ChannelId { get; set; }
            public List<ulong> SelectedGroupIds { get; set; }
            public ClientWebSocket WebSocket { get; set; }
            public CancellationTokenSource CancellationTokenSource { get; set; } = new();
        }
    }

    // DTO 클래스들 (기존과 동일)
    public class StartBroadcastRequest
    {
        public ulong ChannelId { get; set; }
        public List<ulong> SelectedGroupIds { get; set; }
    }

    public class StartBroadcastResponse
    {
        public bool Success { get; set; }
        public string BroadcastId { get; set; }
        public string Error { get; set; }
    }

    public class StopBroadcastResponse
    {
        public bool Success { get; set; }
        public string Error { get; set; }
    }

    public class BroadcastStatus
    {
        public string BroadcastId { get; set; }
        public long PacketCount { get; set; }
        public long TotalBytes { get; set; }
        public TimeSpan Duration { get; set; }
    }
}
