namespace WicsPlatform.Server.Middleware;

public partial class WebSocketMiddleware
{
    public class TtsInfo
    {
        public ulong Id { get; set; }
        public string Name { get; set; }
        public string Content { get; set; }
    }
}