using System.Net.WebSockets;
using System.Text.Json;

namespace WicsPlatform.Server.Middleware;

public partial class WebSocketMiddleware
{
    private async Task HandleAudioDataAsync(WebSocket webSocket, JsonElement root)
    {
        if (!root.TryGetProperty("broadcastId", out var broadcastIdElement)) return;
        var broadcastId = broadcastIdElement.GetUInt64();
        if (!_broadcastSessions.TryGetValue(broadcastId, out var session)) return;

        session.PacketCount++;

        if (!root.TryGetProperty("data", out var dataElement)) return;

        var base64Data = dataElement.GetString();
        var audioData = Convert.FromBase64String(base64Data);
        session.TotalBytes += audioData.Length;

        if (session.OnlineSpeakers?.Any() != true) return;

        try
        {
            // UDP로 압축된 데이터 전송
            //                await udpService.SendAudioToSpeakers(session.OnlineSpeakers, opusData);
            await audioMixingService.AddMicrophoneData(broadcastId, audioData);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, $"Opus encoding failed for broadcast {broadcastId}");
        }
    }
}
