using WicsPlatform.Server.Services;
using WicsPlatform.Shared;

namespace WicsPlatform.Server.Contracts
{
    public interface IAudioMixingService
    {
        Task<bool> InitializeMixer(string broadcastId, ulong channelId, List<SpeakerInfo> speakers);
        Task AddMicrophoneData(string broadcastId, byte[] pcmData);

        // 미디어 관련
        Task AddMediaStream(string broadcastId, string mediaPath);
        Task RemoveMediaStream(string broadcastId);

        // TTS 관련 (새로 추가)
        Task<int> AddTtsStream(string broadcastId, string ttsPath);
        Task RemoveTtsStream(string broadcastId, int ttsStreamId);
        Task RemoveAllTtsStreams(string broadcastId);

        // 볼륨 설정
        Task SetVolume(string broadcastId, AudioSource source, float volume);

        // 기타
        Task<bool> StopMixer(string broadcastId);
        bool IsMixerActive(string broadcastId);
        Task RemoveMicrophoneStream(string broadcastId);
        bool HasActiveMediaStream(string broadcastId);
        bool HasActiveTtsStream(string broadcastId);
        Task<bool> InitializeMicStream(string broadcastId);
    }
}