using Concentus.Enums;
using Concentus.Structs;

namespace WicsPlatform.Audio
{
    /// <summary>
    /// Opus 압축/해제를 간단하게 처리하는 클래스
    /// </summary>
    public class OpusCodec : IDisposable
    {
        private OpusEncoder _encoder;
        private OpusDecoder _decoder;
        private int _sampleRate;
        private int _channels;
        private ILogger _logger;
        private readonly int _frameDurationMs = 60;  // 60ms 프레임 고정

        /// <summary>
        /// OpusCodec 생성자
        /// </summary>
        /// <param name="sampleRate">샘플레이트 (기본: 48000)</param>
        /// <param name="channels">채널 수 (기본: 1 모노)</param>
        /// <param name="bitrate">비트레이트 (기본: 32000)</param>
        /// <param name="logger">로거 (선택사항)</param>
        public OpusCodec(int sampleRate = 48000, int channels = 1, int bitrate = 32000, ILogger logger = null)
        {
            _sampleRate = sampleRate;
            _channels = channels;
            _logger = logger;

            // 인코더 초기화
            _encoder = new OpusEncoder(sampleRate, channels, OpusApplication.OPUS_APPLICATION_VOIP);
            _encoder.Bitrate = bitrate;
            _encoder.SignalType = OpusSignal.OPUS_SIGNAL_VOICE;
            _encoder.UseInbandFEC = true;

            // 디코더 초기화
            _decoder = new OpusDecoder(sampleRate, channels);
        }

        /// <summary>
        /// 코덱 설정을 동적으로 변경
        /// </summary>
        /// <param name="sampleRate">새로운 샘플레이트</param>
        /// <param name="channels">새로운 채널 수</param>
        /// <param name="bitrate">새로운 비트레이트</param>
        public void UpdateSettings(int sampleRate, int channels, int bitrate)
        {
            // 기존 인코더/디코더 정리
            _encoder?.Dispose();
            _decoder?.Dispose();

            // 새로운 설정 저장
            _sampleRate = sampleRate;
            _channels = channels;

            // 새로운 인코더 초기화
            _encoder = new OpusEncoder(sampleRate, channels, OpusApplication.OPUS_APPLICATION_VOIP);
            _encoder.Bitrate = bitrate;
            _encoder.SignalType = OpusSignal.OPUS_SIGNAL_VOICE;
            _encoder.UseInbandFEC = true;

            // 새로운 디코더 초기화
            _decoder = new OpusDecoder(sampleRate, channels);

            _logger?.LogInformation($"OpusCodec settings updated - SampleRate: {sampleRate}, Channels: {channels}, Bitrate: {bitrate}");
        }

        /// <summary>
        /// PCM 데이터를 Opus로 압축
        /// </summary>
        /// <param name="pcmBytes">PCM 바이트 배열 (16bit)</param>
        /// <returns>압축된 Opus 데이터</returns>
        public byte[] Encode(byte[] pcmBytes)
        {
            // PCM bytes를 short 배열로 변환
            var samples = new short[pcmBytes.Length / 2];
            Buffer.BlockCopy(pcmBytes, 0, samples, 0, pcmBytes.Length);

            // 샘플레이트에 따른 60ms 프레임 크기 동적 계산
            // 16000Hz: 960 샘플
            // 24000Hz: 1440 샘플
            // 48000Hz: 2880 샘플
            int expectedSamples = _sampleRate * _frameDurationMs / 1000;

            if (samples.Length != expectedSamples)
            {
                _logger?.LogWarning($"Expected {expectedSamples} samples ({_frameDurationMs}ms @ {_sampleRate}Hz), but got {samples.Length} samples");
            }

            // Opus 인코딩
            var output = new byte[1000];
            int encodedLength = _encoder.Encode(samples, 0, samples.Length, output, 0, output.Length);

            // 실제 크기만큼만 반환
            return output.Take(encodedLength).ToArray();
        }

        /// <summary>
        /// Opus 데이터를 PCM으로 해제
        /// </summary>
        /// <param name="opusBytes">압축된 Opus 데이터</param>
        /// <param name="frameSize">프레임 크기 (기본: 자동 계산)</param>
        /// <returns>PCM 바이트 배열</returns>
        public byte[] Decode(byte[] opusBytes, int frameSize = 0)
        {
            // 프레임 크기 자동 계산 (60ms 기준, 샘플레이트에 맞게)
            if (frameSize == 0)
                frameSize = _sampleRate * _frameDurationMs / 1000;

            // Opus 디코딩
            var samples = new short[frameSize * _channels];
            int decodedSamples = _decoder.Decode(opusBytes, 0, opusBytes.Length, samples, 0, frameSize);

            // short 배열을 byte 배열로 변환
            var pcmBytes = new byte[decodedSamples * 2 * _channels];
            Buffer.BlockCopy(samples, 0, pcmBytes, 0, pcmBytes.Length);

            return pcmBytes;
        }

        /// <summary>
        /// 패킷 손실 시 복구 (PLC - Packet Loss Concealment)
        /// </summary>
        /// <param name="frameSize">프레임 크기</param>
        /// <returns>복구된 PCM 데이터</returns>
        public byte[] DecodeLostPacket(int frameSize = 0)
        {
            // 프레임 크기 자동 계산 (60ms 기준, 샘플레이트에 맞게)
            if (frameSize == 0)
                frameSize = _sampleRate * _frameDurationMs / 1000;

            var samples = new short[frameSize * _channels];
            int decodedSamples = _decoder.Decode(null, 0, 0, samples, 0, frameSize);

            var pcmBytes = new byte[decodedSamples * 2 * _channels];
            Buffer.BlockCopy(samples, 0, pcmBytes, 0, pcmBytes.Length);

            return pcmBytes;
        }

        /// <summary>
        /// 리소스 정리
        /// </summary>
        public void Dispose()
        {
            _encoder?.Dispose();
            _decoder?.Dispose();
        }
    }
}