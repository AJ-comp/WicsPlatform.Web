using WicsPlatform.Server.Services;
using WicsPlatform.Shared;

namespace WicsPlatform.Server.Contracts
{
    public interface IAudioMixingService
    {
        // 이벤트: 단일 미디어 트랙 종료 알림
        event Action<ulong> OnMediaEnded;

        Task<bool> InitializeMixer(ulong broadcastId, ulong channelId, List<SpeakerInfo> speakers);
        Task AddMicrophoneData(ulong broadcastId, byte[] pcmData);

        // 미디어 관련
        Task AddMediaStream(ulong broadcastId, string mediaPath);
        Task RemoveMediaStream(ulong broadcastId);

        // 간단 플레이어 컨트롤
        Task<bool> SeekMediaAsync(ulong broadcastId, double seconds); // +앞으로, -뒤로
        (TimeSpan current, TimeSpan total) GetMediaTimes(ulong broadcastId);

        // TTS 관련 (새로 추가)
        Task<int> AddTtsStream(ulong broadcastId, string ttsPath);
        Task RemoveTtsStream(ulong broadcastId, int ttsStreamId);
        Task RemoveAllTtsStreams(ulong broadcastId);

        // 볼륨 설정
        Task SetVolume(ulong broadcastId, AudioSource source, float volume);

        // 기타
        Task<bool> StopMixer(ulong broadcastId);
        bool IsMixerActive(ulong broadcastId);
        Task RemoveMicrophoneStream(ulong broadcastId);
        bool HasActiveMediaStream(ulong broadcastId);
        bool HasActiveTtsStream(ulong broadcastId);
        Task<bool> InitializeMicStream(ulong broadcastId);
    }
}