namespace WicsPlatform.Shared
{
    // Request DTOs
    public class VolumeRequest
    {
        public string BroadcastId { get; set; }  // nullable - 방송 중이 아니면 null
        public ulong ChannelId { get; set; }     // 필수 - DB 저장용
        public AudioSource Source { get; set; }
        public float Volume { get; set; }
    }
}