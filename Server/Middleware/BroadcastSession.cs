using System.Net.WebSockets;
using WicsPlatform.Server.Services;

namespace WicsPlatform.Server.Middleware
{
    public partial class WebSocketMiddleware
    {
        private class BroadcastSession
        {
            public ulong BroadcastId { get; set; }
            public ulong ChannelId { get; set; }
            public string ConnectionId { get; set; }
            public DateTime StartTime { get; set; }
            public List<ulong> SelectedGroupIds { get; set; }
            public long PacketCount { get; set; }
            public long TotalBytes { get; set; }
            public WebSocket WebSocket { get; set; }
            public List<SpeakerInfo> OnlineSpeakers { get; set; }
            public List<MediaInfo> SelectedMedia { get; set; }
            public List<TtsInfo> SelectedTts { get; set; }
        }
    }
}