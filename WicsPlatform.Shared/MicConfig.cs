using System;
using System.Linq;

namespace WicsPlatform.Shared
{
    /// <summary>
    /// 마이크 설정 (서버/클라이언트 공통)
    /// </summary>
    public class MicConfig
    {
        public int SampleRate { get; private set; }
        public int Channels { get; private set; }
        public int TimesliceMs { get; private set; }
        public int Bitrate { get; private set; }
        public bool EchoCancellation { get; private set; }
        public bool NoiseSuppression { get; private set; }
        public bool AutoGainControl { get; private set; }
        public bool LocalPlayback { get; private set; }

        /// <summary>
        /// 생성자
        /// </summary>
        public MicConfig(int sampleRate = 16000, int channels = 1)
        {
            SampleRate = sampleRate;
            Channels = channels;
            TimesliceMs = 60;        // 고정
            Bitrate = 32000;         // 고정
            EchoCancellation = true;
            NoiseSuppression = true;
            AutoGainControl = true;
            LocalPlayback = false;
        }

        /// <summary>
        /// 타임슬라이스당 샘플 수
        /// </summary>
        public int GetSamplesPerTimeslice()
        {
            return (SampleRate * TimesliceMs) / 1000;
        }

        /// <summary>
        /// JavaScript 전달용 객체
        /// </summary>
        public object ToJavaScriptConfig()
        {
            return new
            {
                sampleRate = SampleRate,
                channels = Channels,
                timeslice = TimesliceMs,
                bitrate = Bitrate,
                echoCancellation = EchoCancellation,
                noiseSuppression = NoiseSuppression,
                autoGainControl = AutoGainControl,
                localPlayback = LocalPlayback,
                samplesPerSend = GetSamplesPerTimeslice()
            };
        }
    }
}