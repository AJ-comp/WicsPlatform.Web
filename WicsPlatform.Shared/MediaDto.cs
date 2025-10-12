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
        // When true, server should shuffle the playback order
        public bool Shuffle { get; set; }
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

    // Status response for simple now-playing UI
    public class MediaStatusResponse
    {
        public bool Success { get; set; }
        public ulong BroadcastId { get; set; }
        public bool IsPlaying { get; set; }
        public int CurrentTrackIndex { get; set; }
        public string CurrentPosition { get; set; }
        public string TotalDuration { get; set; }
        public ulong? CurrentMediaId { get; set; }
        public string CurrentMediaFileName { get; set; }
    }
}
