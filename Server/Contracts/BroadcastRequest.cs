using System.Text.Json.Serialization;

namespace WicsPlatform.Server.Contracts
{
    // Base request
    public abstract class BaseBroadcastRequest
    {
        [JsonPropertyName("broadcastId")]
        public required string BroadcastId { get; init; }
    }

    // Connect request
    public sealed class ConnectBroadcastRequest : BaseBroadcastRequest
    {
        [JsonPropertyName("selectedGroupIds")]
        public required List<ulong> SelectedGroupIds { get; init; }
    }

    // Disconnect request
    public sealed class DisconnectBroadcastRequest : BaseBroadcastRequest
    {
        // broadcastId만 필요
    }
}
