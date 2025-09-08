namespace WicsPlatform.Shared.Broadcast
{
    public class BroadcastInfo
    {
        public ulong BroadcastId { get; set; }
        public ulong ChannelId { get; set; }
        public System.DateTime StartTime { get; set; }
    }

    public class StartBroadcastRequest
    {
        public ulong ChannelId { get; set; }
        public System.Collections.Generic.List<ulong> SelectedGroupIds { get; set; }
    }

    public class StartBroadcastResponse
    {
        public bool Success { get; set; }
        public ulong BroadcastId { get; set; }
        public string Error { get; set; }
    }

    public class StopBroadcastResponse
    {
        public bool Success { get; set; }
        public string Error { get; set; }
    }

    public class AudioDataPacket
    {
        public ulong BroadcastId { get; set; }
        public byte[] AudioData { get; set; }
        public System.DateTime Timestamp { get; set; }
    }

    public class BroadcastStatus
    {
        public ulong BroadcastId { get; set; }
        public long PacketCount { get; set; }
        public long TotalBytes { get; set; }
        public System.TimeSpan Duration { get; set; }
    }
}
