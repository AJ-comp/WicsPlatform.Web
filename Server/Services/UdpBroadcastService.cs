using System.Collections.Concurrent;
using System.Net.Sockets;

namespace WicsPlatform.Server.Services;

public interface IUdpBroadcastService
{
    Task SendAudioToSpeakers(IEnumerable<SpeakerInfo> speakers, byte[] audioData);
}

public class UdpBroadcastService : IUdpBroadcastService, IDisposable
{
    private readonly ILogger<UdpBroadcastService> _logger;
    private readonly IConfiguration _configuration;
    private readonly ConcurrentDictionary<string, UdpClient> _udpClients = new();
    private readonly int _speakerPort;
    
    // ✅ UDP 송신 로그 빈도 조절을 위한 패킷 카운터
    private readonly ConcurrentDictionary<string, long> _packetCounters = new();

    public UdpBroadcastService(ILogger<UdpBroadcastService> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
        
        // ✅ 설정 읽기 방식 개선
        _speakerPort = GetSpeakerPortFromConfiguration();
        _logger.LogInformation($"UDP Broadcast Service initialized with speaker port: {_speakerPort}");
    }

    private int GetSpeakerPortFromConfiguration()
    {
        try
        {
            // 1. 먼저 UdpBroadcast:SpeakerPort 경로로 시도
            var port = _configuration.GetValue<int?>("UdpBroadcast:SpeakerPort");
            if (port.HasValue && port.Value > 0)
            {
                _logger.LogDebug($"Found UdpBroadcast:SpeakerPort = {port.Value}");
                return port.Value;
            }

            // 2. 섹션 바인딩으로 시도
            var udpSection = _configuration.GetSection("UdpBroadcast");
            if (udpSection.Exists())
            {
                var sectionPort = udpSection.GetValue<int?>("SpeakerPort");
                if (sectionPort.HasValue && sectionPort.Value > 0)
                {
                    _logger.LogDebug($"Found UdpBroadcast section SpeakerPort = {sectionPort.Value}");
                    return sectionPort.Value;
                }
            }

            // 3. 모든 설정 키 로그 출력 (디버깅용)
            _logger.LogWarning("UdpBroadcast:SpeakerPort not found, checking all configuration keys:");
            LogAllConfigurationKeys(_configuration, "");

            // 4. 기본값 6001 사용 (설정에서 읽지 못한 경우)
            _logger.LogWarning($"Could not read UdpBroadcast:SpeakerPort from configuration, using default port 6001");
            return 6001; // ⭐ 기본값을 6001로 변경
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading speaker port configuration, using default port 6001");
            return 6001; // ⭐ 오류 시에도 6001 사용
        }
    }

    private void LogAllConfigurationKeys(IConfiguration config, string prefix)
    {
        foreach (var kvp in config.AsEnumerable())
        {
            if (!string.IsNullOrEmpty(kvp.Value))
            {
                _logger.LogDebug($"Config: {kvp.Key} = {kvp.Value}");
            }
        }
    }

    public async Task SendAudioToSpeakers(IEnumerable<SpeakerInfo> speakers, byte[] audioData)
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

            // ✅ UDP 송신 로그 추가 - 빈도 조절 (10번째 패킷마다만 로그)
            var packetCount = _packetCounters.AddOrUpdate(speaker.Ip, 1, (key, value) => value + 1);
            if (packetCount % 10 == 0) // 10번째 패킷마다만 로그 출력 (약 500ms마다)
            {
                _logger.LogInformation($"UDP 송신: {speaker.Ip}:{_speakerPort} → {packet.Length} bytes (스피커: {speaker.Name}) [#{packetCount}]");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Failed to send audio to speaker {speaker.Ip}:{_speakerPort}");

            // 실패한 클라이언트 제거
            _udpClients.TryRemove(speaker.Ip, out _);
            _packetCounters.TryRemove(speaker.Ip, out _); // ✅ 패킷 카운터도 제거
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
        _packetCounters.Clear(); // ✅ 패킷 카운터도 정리
    }
}

public class SpeakerInfo
{
    public ulong Id { get; set; }
    public string Ip { get; set; }
    public string Name { get; set; }
    public ulong ChannelId { get; set; }
    public bool UseVpn { get; set; } = false;
    public bool Active { get; set; } = false;

    public override bool Equals(object obj)
    {
        return obj is SpeakerInfo info &&
               Id == info.Id;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Id);
    }

    public static bool operator ==(SpeakerInfo left, SpeakerInfo right)
    {
        return EqualityComparer<SpeakerInfo>.Default.Equals(left, right);
    }

    public static bool operator !=(SpeakerInfo left, SpeakerInfo right)
    {
        return !(left == right);
    }
}