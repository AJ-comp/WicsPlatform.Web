namespace WicsPlatform.Shared
{
    // Request DTOs
    public class VolumeRequest
    {
        public ulong? BroadcastId { get; set; }  // nullable - 방송 중이 아니면 null
        public ulong ChannelId { get; set; }     // 필수 - DB 저장용
        public AudioSource Source { get; set; }
        public float Volume { get; set; }
    }

    // Response DTOs
    public class VolumeSetResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public ulong? BroadcastId { get; set; }  // ulong? 타입으로 변경
        public ulong ChannelId { get; set; }
        public string Source { get; set; }
        public float Volume { get; set; }
        public bool SavedToDb { get; set; }
    }
}