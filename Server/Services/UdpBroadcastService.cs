using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace WicsPlatform.Server.Services
{
    public interface IUdpBroadcastService
    {
        Task SendAudioToSpeakers(List<SpeakerInfo> speakers, byte[] audioData);
    }

    public class UdpBroadcastService : IUdpBroadcastService, IDisposable
    {
        private readonly ILogger<UdpBroadcastService> _logger;
        private readonly IConfiguration _configuration;
        private readonly ConcurrentDictionary<string, UdpClient> _udpClients = new();
        private readonly int _speakerPort;

        public UdpBroadcastService(ILogger<UdpBroadcastService> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
            _speakerPort = _configuration.GetValue<int>("UdpBroadcast:SpeakerPort", 3000); // 기본값 3000
            _logger.LogInformation($"UDP Broadcast Service initialized with speaker port: {_speakerPort}");
        }

        public async Task SendAudioToSpeakers(List<SpeakerInfo> speakers, byte[] audioData)
        {
            var tasks = new List<Task>();

            foreach (var speaker in speakers)
            {
                tasks.Add(SendToSpeaker(speaker, audioData));
            }

            await Task.WhenAll(tasks);
        }

        private async Task SendToSpeaker(SpeakerInfo speaker, byte[] audioData)
        {
            try
            {
                // IP별로 UdpClient 재사용
                var udpClient = _udpClients.GetOrAdd(speaker.Ip, ip =>
                {
                    var client = new UdpClient();
                    client.Connect(ip, _speakerPort);
                    return client;
                });

                // UDP 패킷 헤더 추가 (채널 ID, 타임스탬프 등)
                var packet = CreateAudioPacket(speaker.ChannelId, audioData);
                await udpClient.SendAsync(packet, packet.Length);

                _logger.LogDebug($"Sent {packet.Length} bytes to speaker {speaker.Name} ({speaker.Ip}:{_speakerPort})");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to send audio to speaker {speaker.Ip}:{_speakerPort}");

                // 실패한 클라이언트 제거
                _udpClients.TryRemove(speaker.Ip, out _);
            }
        }

        private byte[] CreateAudioPacket(ulong channelId, byte[] audioData)
        {
            // 패킷 구조: [헤더 16바이트][오디오 데이터]
            // 헤더: [채널ID 8바이트][타임스탬프 8바이트]
            var packet = new byte[16 + audioData.Length];

            // 채널 ID
            BitConverter.GetBytes(channelId).CopyTo(packet, 0);

            // 타임스탬프 (Unix 밀리초)
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            BitConverter.GetBytes(timestamp).CopyTo(packet, 8);

            // 오디오 데이터
            audioData.CopyTo(packet, 16);

            return packet;
        }

        public void Dispose()
        {
            foreach (var client in _udpClients.Values)
            {
                try
                {
                    client.Close();
                    client.Dispose();
                }
                catch { }
            }
            _udpClients.Clear();
        }
    }

    public class SpeakerInfo
    {
        public ulong Id { get; set; }
        public string Ip { get; set; }
        public string Name { get; set; }
        public ulong ChannelId { get; set; }
        public bool UseVpn { get; set; }  // ⭐ VPN 사용 여부 추가 (선택사항)
    }
}