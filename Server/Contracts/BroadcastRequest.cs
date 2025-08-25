using System.Text.Json.Serialization;

namespace WicsPlatform.Server.Contracts
{
    public sealed class BroadcastRequest
    {
        [JsonPropertyName("broadcastId")]
        public required string BroadcastId { get; init; }

        [JsonPropertyName("selectedGroupIds")]
        public required List<ulong> SelectedGroupIds { get; init; }
    }
}
