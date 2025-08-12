using WicsPlatform.Client.Models;
using WicsPlatform.Server.Models.wics;

namespace WicsPlatform.Client.Services.Interfaces
{
    public interface IBroadcastDataService
    {
        Task<BroadcastInitialData> LoadInitialDataAsync();
        Task<Channel> CreateChannelAsync(string channelName);
        Task<Channel> UpdateChannelAudioSettingsAsync(Channel channel, int? sampleRate, int? channels);
    }
}
