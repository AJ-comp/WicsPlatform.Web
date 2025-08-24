using Concentus;
using Concentus.Enums;
using Concentus.Structs;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;

namespace WicsPlatform.Audio
{
    /// <summary>
    /// Opus 압축/해제를 간단하게 처리하는 클래스
    /// </summary>
    public class OpusCodec : IDisposable
    {
        private readonly OpusEncoder _encoder;
        private readonly OpusDecoder _decoder;
        private readonly int _sampleRate;
        private readonly int _channels;
        private readonly ILogger _logger;

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
        /// PCM 데이터를 Opus로 압축
        /// </summary>
        /// <param name="pcmBytes">PCM 바이트 배열 (16bit)</param>
        /// <returns>압축된 Opus 데이터</returns>
        public byte[] Encode(byte[] pcmBytes)
        {
            // PCM bytes를 short 배열로 변환
            var samples = new short[pcmBytes.Length / 2];
            Buffer.BlockCopy(pcmBytes, 0, samples, 0, pcmBytes.Length);

            // 60ms = 2880 샘플 (48kHz 기준)
            // Opus가 완벽하게 지원하는 프레임 크기
            if (samples.Length != 2880)
            {
                _logger?.LogWarning($"Expected 2880 samples (60ms), but got {samples.Length} samples");
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
            // 프레임 크기 자동 계산 (60ms 기준)
            if (frameSize == 0)
                frameSize = _sampleRate * 60 / 1000;  // 60ms = 2880 samples @ 48kHz

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
            if (frameSize == 0)
                frameSize = _sampleRate * 60 / 1000;  // 60ms = 2880 samples

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