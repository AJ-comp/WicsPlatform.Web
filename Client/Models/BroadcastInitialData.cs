using WicsPlatform.Server.Models.wics;

namespace WicsPlatform.Client.Models
{
    public class BroadcastInitialData
    {
        public IEnumerable<Channel> Channels { get; set; }
        public IEnumerable<Group> SpeakerGroups { get; set; }
        public IEnumerable<Speaker> AllSpeakers { get; set; }
        public IEnumerable<MapSpeakerGroup> SpeakerGroupMappings { get; set; }
    }
}
