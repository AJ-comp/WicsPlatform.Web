namespace WicsPlatform.Server.Middleware;

public partial class WebSocketMiddleware
{
    public class MediaInfo
    {
        public ulong Id { get; set; }
        public string FileName { get; set; }
        public string FullPath { get; set; }
    }
}