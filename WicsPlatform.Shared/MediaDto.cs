using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WicsPlatform.Shared
{
    // Request/Response DTOs
    public class MediaPlayRequest
    {
        public ulong BroadcastId { get; set; }
        public List<ulong> MediaIds { get; set; }
    }

    public class MediaPlayResponse
    {
        public bool Success { get; set; }
        public string SessionId { get; set; }
        public string Message { get; set; }
        public List<MediaFileInfo> MediaFiles { get; set; }
    }

    public class MediaFileInfo
    {
        public ulong Id { get; set; }
        public string FileName { get; set; }
        public string Status { get; set; }
    }

    public class MediaStopRequest
    {
        public ulong BroadcastId { get; set; }
    }

    public class MediaStopResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; }
    }
}
