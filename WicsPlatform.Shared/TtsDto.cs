namespace WicsPlatform.Shared
{
    public class TtsPlayRequest
    {
        public string BroadcastId { get; set; }
        public List<ulong> TtsIds { get; set; }
    }

    public class TtsPlayResponse
    {
        public bool Success { get; set; }
        public string SessionId { get; set; }
        public string Message { get; set; }
        public List<TtsFileInfo> TtsFiles { get; set; } = new();
    }

    public class TtsFileInfo
    {
        public ulong Id { get; set; }
        public string Name { get; set; }
        public string Status { get; set; }
    }

    public class TtsStopRequest
    {
        public string BroadcastId { get; set; }
    }

    public class TtsStopResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; }
    }
}