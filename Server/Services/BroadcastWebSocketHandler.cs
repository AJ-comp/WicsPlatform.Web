using System.Collections.Concurrent;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using WicsPlatform.Server.Data;
using WicsPlatform.Shared.Broadcast;

namespace WicsPlatform.Server.Services
{
    public class BroadcastWebSocketHandler
    {
        private readonly ILogger<BroadcastWebSocketHandler> _logger;
        private readonly wicsContext _context;
        private static readonly ConcurrentDictionary<string, BroadcastSession> _sessions = new();

        public BroadcastWebSocketHandler(ILogger<BroadcastWebSocketHandler> logger, wicsContext context)
        {
            _logger = logger;
            _context = context;
        }

        public async Task HandleAsync(HttpContext context)
        {
            if (!context.WebSockets.IsWebSocketRequest)
            {
                context.Response.StatusCode = 400;
                return;
            }

            using var socket = await context.WebSockets.AcceptWebSocketAsync();
            await ProcessSocketAsync(socket);
        }

        private async Task ProcessSocketAsync(WebSocket socket)
        {
            ulong? broadcastId = null;
            var buffer = new byte[8192];

            while (socket.State == WebSocketState.Open)
            {
                var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    break;
                }

                if (result.MessageType == WebSocketMessageType.Text)
                {
                    var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;
                    var action = root.GetProperty("Action").GetString();
                    switch (action)
                    {
                        case "StartBroadcast":
                            var request = JsonSerializer.Deserialize<StartBroadcastRequest>(json);
                            var response = await StartBroadcastAsync(request, socket);
                            broadcastId = response.BroadcastId;
                            await SendAsync(socket, response);
                            break;
                        case "StopBroadcast":
                            var stopId = root.GetProperty("BroadcastId").GetUInt64();
                            var stopResp = StopBroadcast(stopId);
                            await SendAsync(socket, stopResp);
                            break;
                    }
                }
                else if (result.MessageType == WebSocketMessageType.Binary && broadcastId.HasValue)
                {
                    if (_sessions.TryGetValue(broadcastId.Value.ToString(), out var session))
                    {
                        session.PacketCount++;
                        session.TotalBytes += result.Count;
                        var status = new BroadcastStatus
                        {
                            BroadcastId = broadcastId.Value,
                            PacketCount = session.PacketCount,
                            TotalBytes = session.TotalBytes,
                            Duration = DateTime.UtcNow - session.StartTime
                        };
                        await SendAsync(socket, status);
                    }
                }
            }

            if (broadcastId.HasValue)
            {
                _sessions.TryRemove(broadcastId.Value.ToString(), out _);
            }
        }

        private async Task<StartBroadcastResponse> StartBroadcastAsync(StartBroadcastRequest request, WebSocket socket)
        {
            var channel = await _context.Channels.FindAsync(request.ChannelId);
            if (channel == null)
            {
                return new StartBroadcastResponse { Success = false, Error = "Channel not found" };
            }

            var id = request.ChannelId.ToString();
            var session = new BroadcastSession
            {
                BroadcastId = id,
                ChannelId = request.ChannelId,
                StartTime = DateTime.UtcNow,
                SelectedGroupIds = request.SelectedGroupIds,
                Socket = socket
            };
            _sessions[id] = session;
            _logger.LogInformation($"Broadcast started {id}");
            return new StartBroadcastResponse { Success = true, BroadcastId = request.ChannelId };
        }

        private StopBroadcastResponse StopBroadcast(ulong id)
        {
            if (_sessions.TryRemove(id.ToString(), out var _))
            {
                _logger.LogInformation($"Broadcast stopped {id}");
                return new StopBroadcastResponse { Success = true };
            }
            return new StopBroadcastResponse { Success = false, Error = "Not found" };
        }

        private static async Task SendAsync<T>(WebSocket socket, T obj)
        {
            var json = JsonSerializer.Serialize(obj);
            var bytes = Encoding.UTF8.GetBytes(json);
            await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
        }

        private class BroadcastSession
        {
            public string BroadcastId { get; set; }
            public ulong ChannelId { get; set; }
            public DateTime StartTime { get; set; }
            public List<ulong> SelectedGroupIds { get; set; }
            public long PacketCount { get; set; }
            public long TotalBytes { get; set; }
            public WebSocket Socket { get; set; }
        }
    }
}
