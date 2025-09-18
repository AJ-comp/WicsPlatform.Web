using System.Collections.Concurrent;
using System.Net.Sockets;

namespace WicsPlatform.Server.Services;

public interface IBroadcastManager
{
    Task ConnectToSpeakers(ulong channelId, List<string> speakerIPs);
    Task SendAudioData(ulong channelId, byte[] audioData);
    Task DisconnectChannel(ulong channelId);
}

public class BroadcastManager
{
    // ChannelId -> TCP연결들
    private readonly ConcurrentDictionary<ulong, List<TcpClient>> _broadcasts;
}
