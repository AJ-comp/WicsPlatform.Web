using System.Net.WebSockets;
using WicsPlatform.Server.Services;

namespace WicsPlatform.Server.Middleware;

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

        public List<SpeakerInfo> ActiveSpeakers => OnlineSpeakers?.Where(s => s.Active).ToList() ?? new List<SpeakerInfo>();


        /// <summary>
        /// OnlineSpeakers 리스트에서 매칭되는 SpeakerInfo를 찾아 Active를 true로 설정합니다.
        /// </summary>
        /// <param name="speaker">활성화할 SpeakerInfo</param>
        /// <returns>활성화 성공 여부</returns>
        public bool ActivateSpeaker(SpeakerInfo speaker)
        {
            if (speaker == null || OnlineSpeakers == null)
                return false;

            var targetSpeaker = OnlineSpeakers.FirstOrDefault(s => s.Equals(speaker));
            if (targetSpeaker != null)
            {
                targetSpeaker.Active = true;
                return true;
            }

            return false;
        }


        // 또는 Id로 직접 찾는 버전
        public bool ActivateSpeaker(ulong speakerId)
        {
            if (OnlineSpeakers == null)
                return false;

            var targetSpeaker = OnlineSpeakers.FirstOrDefault(s => s.Id == speakerId);
            if (targetSpeaker != null)
            {
                targetSpeaker.Active = true;
                return true;
            }

            return false;
        }
    }
}