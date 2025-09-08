using WicsPlatform.Server.Services;
using WicsPlatform.Shared;

namespace WicsPlatform.Server.Contracts
{
    public interface IAudioMixingService
    {
        Task<bool> InitializeMixer(ulong broadcastId, ulong channelId, List<SpeakerInfo> speakers);
        Task AddMicrophoneData(ulong broadcastId, byte[] pcmData);

        // 미디어 관련
        Task AddMediaStream(ulong broadcastId, string mediaPath);
        Task RemoveMediaStream(ulong broadcastId);

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