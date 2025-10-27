using System.Threading.Channels;

namespace WicsPlatform.Server.Services;

/// <summary>
/// 스피커 소유권 변경 이벤트
/// </summary>
public sealed record SpeakerOwnershipChangedEvent(
    ulong ChannelId,
    ulong SpeakerId,
    bool IsActive,
    DateTime Timestamp
);

/// <summary>
/// 스피커 소유권 변경 메시지 브로커 (Singleton)
/// System.Threading.Channels 기반으로 Thread-Safe하게 이벤트를 전달합니다.
/// </summary>
public sealed class SpeakerOwnershipBroker
{
    private readonly Channel<SpeakerOwnershipChangedEvent> _channel;
    private readonly ILogger<SpeakerOwnershipBroker> _logger;

    public SpeakerOwnershipBroker(ILogger<SpeakerOwnershipBroker> logger)
    {
        _logger = logger;
        
        // Unbounded Channel 생성 (무제한 큐)
        // SingleReader=false: 여러 구독자 가능 (향후 확장성)
        // SingleWriter=false: 여러 발행자 가능
        _channel = Channel.CreateUnbounded<SpeakerOwnershipChangedEvent>(new UnboundedChannelOptions
        {
            SingleReader = false,
            SingleWriter = false
        });
    }

    /// <summary>
    /// 소유권 변경 이벤트 발행
    /// </summary>
    /// <param name="evt">소유권 변경 이벤트</param>
    /// <param name="ct">취소 토큰</param>
    public async ValueTask PublishAsync(SpeakerOwnershipChangedEvent evt, CancellationToken ct = default)
    {
        await _channel.Writer.WriteAsync(evt, ct);
        
        _logger.LogDebug(
            "[OwnershipBroker] Published: Channel={ChannelId}, Speaker={SpeakerId}, Active={IsActive}",
            evt.ChannelId, evt.SpeakerId, evt.IsActive);
    }

    /// <summary>
    /// 구독자용 리더 (여러 구독자가 동시에 읽기 가능)
    /// </summary>
    public ChannelReader<SpeakerOwnershipChangedEvent> Reader => _channel.Reader;
}
