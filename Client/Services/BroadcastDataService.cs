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
                NotifyWarn("입력 필요", "채널명을 입력해주세요.");
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
                    AudioMethod = 0,
                    Description = "",
                    DeleteYn = "N",
                    CreatedAt = DateTime.Now,
                    UpdatedAt = DateTime.Now
                };

                var createdChannel = await _wicsService.CreateChannel(newChannel);
                NotifySuccess("생성 완료", $"'{channelName}' 채널이 생성되었습니다.");
                return createdChannel;
            }
            catch (Exception ex)
            {
                NotifyError("채널 생성", ex);
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
                channel.ChannelCount = (byte)channels.Value;
                updated = true;
            }

            if (updated)
            {
                try
                {
                    channel.UpdatedAt = DateTime.Now;
                    await _wicsService.UpdateChannel(channel.Id, channel);
                    NotifySuccess("설정 저장", $"채널 '{channel.Name}'의 오디오 설정이 저장되었습니다.");
                    _logger.LogInformation($"Channel {channel.Id} audio settings updated.");
                }
                catch (Exception ex)
                {
                    NotifyError("설정 저장", ex);
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
                    // 예약방송 채널 제외: ScheduleId eq null 만 표시, 소프트삭제 제외
                    Filter = "(DeleteYn eq 'N' or DeleteYn eq null) and ScheduleId eq null",
                    OrderBy = "CreatedAt desc"
                };
                var result = await _wicsService.GetChannels(query);
                return result.Value.AsODataEnumerable();
            }
            catch (Exception ex)
            {
                NotifyError("채널 목록 로딩", ex);
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
                NotifyError("스피커 그룹 목록 로딩", ex);
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
                NotifyError("스피커 목록 로딩", ex);
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
                NotifyError("매핑 정보 로딩", ex);
                return Enumerable.Empty<MapSpeakerGroup>();
            }
        }

        private void Notify(NotificationSeverity severity, string summary, string detail, int duration = 4000) =>
            _notificationService.Notify(new NotificationMessage { Severity = severity, Summary = summary, Detail = detail, Duration = duration });

        private void NotifySuccess(string summary, string detail) =>
            Notify(NotificationSeverity.Success, summary, detail);

        private void NotifyError(string summary, Exception ex) =>
            Notify(NotificationSeverity.Error, "오류", $"{summary} 중 오류: {ex.Message}");

        private void NotifyWarn(string summary, string detail) =>
            Notify(NotificationSeverity.Warning, summary, detail);
    }
}
