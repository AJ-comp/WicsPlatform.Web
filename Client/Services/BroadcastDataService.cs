using Radzen;
using WicsPlatform.Client.Models;
using WicsPlatform.Client.Services.Interfaces;
using WicsPlatform.Server.Models.wics;
using Group = WicsPlatform.Server.Models.wics.Group;

namespace WicsPlatform.Client.Services
{
    public class BroadcastDataService : IBroadcastDataService
    {
        private readonly wicsService _wicsService;
        private readonly NotificationService _notificationService;
        private readonly ILogger<BroadcastDataService> _logger;

        public BroadcastDataService(wicsService wicsService, NotificationService notificationService, ILogger<BroadcastDataService> logger)
        {
            _wicsService = wicsService;
            _notificationService = notificationService;
            _logger = logger;
        }

        public async Task<BroadcastInitialData> LoadInitialDataAsync()
        {
            var (channels, speakerGroups, allSpeakers, speakerGroupMappings) = await LoadAllDataAsync();
            return new BroadcastInitialData
            {
                Channels = channels,
                SpeakerGroups = speakerGroups,
                AllSpeakers = allSpeakers,
                SpeakerGroupMappings = speakerGroupMappings
            };
        }

        public async Task<Channel> CreateChannelAsync(string channelName)
        {
            if (string.IsNullOrWhiteSpace(channelName))
            {
                NotifyWarn("�Է� �ʿ�", "ä�θ��� �Է����ּ���.");
                return null;
            }

            try
            {
                var newChannel = new Channel
                {
                    Name = channelName.Trim(),
                    Type = 0,
                    State = 0,
                    MicVolume = 0.5f,
                    TtsVolume = 0.5f,
                    MediaVolume = 0.5f,
                    Volume = 0.5f,
                    SamplingRate = 48000,
                    AudioMethod = 0,
                    Codec = "webm",
                    Channel1 = "stereo",
                    Bit = 16,
                    Description = "",
                    DeleteYn = "N",
                    CreatedAt = DateTime.Now,
                    UpdatedAt = DateTime.Now
                };

                var createdChannel = await _wicsService.CreateChannel(newChannel);
                NotifySuccess("���� �Ϸ�", $"'{channelName}' ä���� �����Ǿ����ϴ�.");
                return createdChannel;
            }
            catch (Exception ex)
            {
                NotifyError("ä�� ����", ex);
                return null;
            }
        }

        public async Task<Channel> UpdateChannelAudioSettingsAsync(Channel channel, int? sampleRate, int? channels)
        {
            if (channel == null) return null;

            bool updated = false;
            if (sampleRate.HasValue && channel.SamplingRate != sampleRate.Value)
            {
                channel.SamplingRate = (uint)sampleRate.Value;
                updated = true;
            }

            if (channels.HasValue)
            {
                var channelString = channels.Value == 1 ? "mono" : "stereo";
                if (channel.Channel1 != channelString)
                {
                    channel.Channel1 = channelString;
                    updated = true;
                }
            }

            if (updated)
            {
                try
                {
                    channel.UpdatedAt = DateTime.Now;
                    await _wicsService.UpdateChannel(channel.Id, channel);
                    NotifySuccess("���� ����", $"ä�� '{channel.Name}'�� ����� ������ ����Ǿ����ϴ�.");
                    _logger.LogInformation($"Channel {channel.Id} audio settings updated.");
                }
                catch (Exception ex)
                {
                    NotifyError("���� ����", ex);
                    _logger.LogError(ex, $"Failed to update channel {channel.Id} audio settings.");
                    return null;
                }
            }
            return channel;
        }


        private async Task<(IEnumerable<Channel>, IEnumerable<Group>, IEnumerable<Speaker>, IEnumerable<MapSpeakerGroup>)> LoadAllDataAsync()
        {
            var channelsTask = LoadChannels();
            var speakerGroupsTask = LoadSpeakerGroups();
            var speakersTask = LoadSpeakers();
            var mappingsTask = LoadSpeakerGroupMappings();

            await Task.WhenAll(channelsTask, speakerGroupsTask, speakersTask, mappingsTask);

            return (await channelsTask, await speakerGroupsTask, await speakersTask, await mappingsTask);
        }

        private async Task<IEnumerable<Channel>> LoadChannels()
        {
            try
            {
                var query = new Query
                {
                    Filter = "DeleteYn eq 'N' or DeleteYn eq null",
                    OrderBy = "CreatedAt desc"
                };
                var result = await _wicsService.GetChannels(query);
                return result.Value.AsODataEnumerable();
            }
            catch (Exception ex)
            {
                NotifyError("ä�� ��� �ε�", ex);
                return Enumerable.Empty<Channel>();
            }
        }

        private async Task<IEnumerable<Group>> LoadSpeakerGroups()
        {
            try
            {
                var result = await _wicsService.GetGroups(new Query
                {
                    Filter = "DeleteYn eq 'N' or DeleteYn eq null"
                });
                return result.Value.AsODataEnumerable();
            }
            catch (Exception ex)
            {
                NotifyError("����Ŀ �׷� ��� �ε�", ex);
                return Enumerable.Empty<Group>();
            }
        }

        private async Task<IEnumerable<Speaker>> LoadSpeakers()
        {
            try
            {
                var q = new Query
                {
                    Filter = "DeleteYn eq 'N' or DeleteYn eq null",
                    Expand = "Channel"
                };
                var result = await _wicsService.GetSpeakers(q);
                return result.Value.AsODataEnumerable();
            }
            catch (Exception ex)
            {
                NotifyError("����Ŀ ��� �ε�", ex);
                return Enumerable.Empty<Speaker>();
            }
        }

        private async Task<IEnumerable<MapSpeakerGroup>> LoadSpeakerGroupMappings()
        {
            try
            {
                var result = await _wicsService.GetMapSpeakerGroups(new Query
                {
                    Expand = "Group,Speaker",
                    Filter = "LastYn eq 'Y'"
                });
                return result.Value.AsODataEnumerable();
            }
            catch (Exception ex)
            {
                NotifyError("���� ���� �ε�", ex);
                return Enumerable.Empty<MapSpeakerGroup>();
            }
        }

        private void Notify(NotificationSeverity severity, string summary, string detail, int duration = 4000) =>
            _notificationService.Notify(new NotificationMessage { Severity = severity, Summary = summary, Detail = detail, Duration = duration });

        private void NotifySuccess(string summary, string detail) =>
            Notify(NotificationSeverity.Success, summary, detail);

        private void NotifyError(string summary, Exception ex) =>
            Notify(NotificationSeverity.Error, "����", $"{summary} �� ����: {ex.Message}");

        private void NotifyWarn(string summary, string detail) =>
            Notify(NotificationSeverity.Warning, summary, detail);
    }
}
