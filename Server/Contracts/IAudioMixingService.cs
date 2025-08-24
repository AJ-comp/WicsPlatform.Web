using WicsPlatform.Server.Services;

namespace WicsPlatform.Server.Contracts
{
    public interface IAudioMixingService
    {
        Task<bool> InitializeMixer(string broadcastId, List<SpeakerInfo> speakers);
        Task AddMicrophoneData(string broadcastId, byte[] pcmData);
        Task AddMediaStream(string broadcastId, string mediaPath);
        Task RemoveMediaStream(string broadcastId);
        Task SetVolume(string broadcastId, AudioSource source, float volume);
        Task<bool> StopMixer(string broadcastId);
        bool IsMixerActive(string broadcastId);
    }

    public enum AudioSource
    {
        Microphone,
        Media,
        TTS,
        Master
    }
}
