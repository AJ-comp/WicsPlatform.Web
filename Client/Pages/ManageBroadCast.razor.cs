using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Radzen;
using System.Net.Http.Json;
using WicsPlatform.Audio;
using WicsPlatform.Client.Dialogs;
using WicsPlatform.Client.Pages.SubPages;
using WicsPlatform.Client.Services;
using WicsPlatform.Client.Services.Interfaces;
using WicsPlatform.Shared;

namespace WicsPlatform.Client.Pages
{
    public partial class ManageBroadCast : IDisposable
    {
        #region Dependency Injection
        [Inject] protected IJSRuntime JSRuntime { get; set; }
        [Inject] protected NavigationManager NavigationManager { get; set; }
        [Inject] protected DialogService DialogService { get; set; }
        [Inject] protected TooltipService TooltipService { get; set; }
        [Inject] protected ContextMenuService ContextMenuService { get; set; }
        [Inject] protected NotificationService NotificationService { get; set; }
        [Inject] protected SecurityService Security { get; set; }
        [Inject] protected wicsService WicsService { get; set; }
        [Inject] protected HttpClient Http { get; set; }
        [Inject] protected BroadcastWebSocketService WebSocketService { get; set; }
        [Inject] protected ILogger<ManageBroadCast> _logger { get; set; }
        [Inject] protected IBroadcastDataService BroadcastDataService { get; set; }
        [Inject] protected BroadcastRecordingService RecordingService { get; set; }
        [Inject] protected BroadcastLoggingService LoggingService { get; set; }
        [Inject] protected OpusCodec OpusCodec { get; set; }
        #endregion

        #region Fields & Properties
        // 채널 관련
        protected string newChannelName = "";
        protected IEnumerable<WicsPlatform.Server.Models.wics.Channel> channels = new List<WicsPlatform.Server.Models.wics.Channel>();
        protected WicsPlatform.Server.Models.wics.Channel selectedChannel = null;
        protected bool isLoading = true;
        protected Dictionary<ulong, bool> isChannelBroadcasting = new Dictionary<ulong, bool>();

        // UI 상태
        protected bool speakerGroupPanelCollapsed = false;
        protected bool playlistPanelCollapsed = false;
        protected bool ttsPanelCollapsed = false;
        protected bool monitoringPanelCollapsed = false;

        // 방송 상태
        protected bool isBroadcasting = false;
        protected ulong? currentBroadcastId = null;
        protected DateTime broadcastStartTime = DateTime.Now;
        protected string broadcastDuration = "00:00:00";
        protected int totalDataPackets = 0;
        protected double totalDataSize = 0.0;
        protected double audioLevel = 0.0;
        protected double averageBitrate = 0.0;
        protected int sampleRate = 44100;
        private System.Threading.Timer _broadcastTimer;

        // 볼륨 설정
        protected int micVolume = 50;
        protected int mediaVolume = 50;
        protected int ttsVolume = 50;

        // 오디오 설정 (간소화)
        private int _preferredSampleRate = 48000;
        private int _preferredChannels = 2;

        // JS Interop
        private IJSObjectReference _mixerModule;
        private IJSObjectReference _jsModule;
        private IJSObjectReference _speakerModule;
        private DotNetObjectReference<ManageBroadCast> _dotNetRef;

        // SubPage 참조
        protected BroadcastMonitoringSection monitoringSection;
        protected BroadcastPlaylistSection playlistSection;
        protected BroadcastTtsSection ttsSection;
        protected BroadcastSpeakerSection speakerSection;

        // Broadcast 관련
        private bool _currentLoopbackSetting = false;
        #endregion

        #region Lifecycle Methods
        protected override async Task OnInitializedAsync()
        {
            SubscribeToWebSocketEvents();
            SubscribeToRecordingEvents();
            await LoadInitialData();
        }

        protected override async Task OnParametersSetAsync()
        {
            if (selectedChannel != null)
            {
                micVolume = (int)(selectedChannel.MicVolume * 100);
                mediaVolume = (int)(selectedChannel.MediaVolume * 100);
                ttsVolume = (int)(selectedChannel.TtsVolume * 100);
            }
        }

        public void Dispose()
        {
            UnsubscribeFromWebSocketEvents();
            UnsubscribeFromRecordingEvents();
            DisposeResources();
        }
        #endregion

        #region Data Loading Methods
        private async Task LoadInitialData()
        {
            isLoading = true;
            StateHasChanged();

            var initialData = await BroadcastDataService.LoadInitialDataAsync();
            if (initialData != null)
            {
                channels = initialData.Channels;
            }

            isLoading = false;
            StateHasChanged();
        }
        #endregion

        #region Channel Management
        protected async Task CreateChannel()
        {
            var createdChannel = await BroadcastDataService.CreateChannelAsync(newChannelName);
            if (createdChannel != null)
            {
                newChannelName = "";
                await LoadInitialData();
            }
        }

        protected async Task SelectChannel(WicsPlatform.Server.Models.wics.Channel channel)
        {
            selectedChannel = channel;
            ResetAllPanels();

            if (channel != null)
            {
                micVolume = (int)(channel.MicVolume * 100);
                mediaVolume = (int)(channel.MediaVolume * 100);
                ttsVolume = (int)(channel.TtsVolume * 100);

                // 단순히 채널의 값을 저장
                _preferredSampleRate = channel.SamplingRate > 0 ? (int)channel.SamplingRate : 48000;
                _preferredChannels = channel.ChannelCount;

                _logger.LogInformation($"Channel selected: {channel.Name}, Settings: {_preferredSampleRate}Hz, {_preferredChannels}ch");

                await CheckAndRecoverIfNeeded(channel.Id);
            }

            if (speakerSection != null)
            {
                speakerSection.ClearSelection();
            }

            await InvokeAsync(StateHasChanged);
        }

        protected async Task ToggleBroadcast(ulong channelId)
        {
            if (!isChannelBroadcasting.ContainsKey(channelId))
                isChannelBroadcasting[channelId] = false;

            isChannelBroadcasting[channelId] = !isChannelBroadcasting[channelId];

            var message = isChannelBroadcasting[channelId]
                ? $"채널 {channelId}의 방송이 시작되었습니다."
                : $"채널 {channelId}의 방송이 중지되었습니다.";

            var notifyAction = isChannelBroadcasting[channelId]
                ? (Action<string, string>)NotifySuccess
                : NotifyInfo;

            notifyAction(isChannelBroadcasting[channelId] ? "방송 시작" : "방송 중지", message);
            await InvokeAsync(StateHasChanged);
        }
        #endregion

        #region Audio Settings Dialog
        protected async Task OpenAudioSettingsDialog()
        {
            if (selectedChannel == null)
            {
                NotifyWarn("채널 선택", "먼저 채널을 선택하세요.");
                return;
            }

            var parameters = new Dictionary<string, object>
            {
                { "Channel", selectedChannel },
                { "IsBroadcasting", isBroadcasting },
                { "PreferredSampleRate", _preferredSampleRate },
                { "PreferredChannels", _preferredChannels }
            };

            var result = await DialogService.OpenAsync<AudioSettingsDialog>(
                "오디오 설정",
                parameters,
                new DialogOptions
                {
                    Width = "500px",
                    Height = "auto",
                    Resizable = false,
                    Draggable = true
                });

            if (result is AudioSettingsResult audioSettings)
            {
                // 설정 적용
                _preferredSampleRate = audioSettings.SampleRate;
                _preferredChannels = audioSettings.Channels;

                // 채널에 저장
                if (selectedChannel != null)
                {
                    await BroadcastDataService.UpdateChannelAudioSettingsAsync(
                        selectedChannel,
                        audioSettings.SampleRate,
                        audioSettings.Channels);
                }

                NotifySuccess("오디오 설정",
                    $"설정이 변경되었습니다. (샘플레이트: {audioSettings.SampleRate}Hz, 채널: {audioSettings.Channels}ch)");
            }
        }
        #endregion

        #region Volume Control Dialog
        protected async Task OpenVolumeControlDialog()
        {
            if (selectedChannel == null)
            {
                NotifyWarn("채널 선택", "먼저 채널을 선택하세요.");
                return;
            }

            var parameters = new Dictionary<string, object>
            {
                { "Channel", selectedChannel },
                { "CurrentBroadcastId", currentBroadcastId?.ToString() },
                { "IsBroadcasting", isBroadcasting }
            };

            var result = await DialogService.OpenAsync<VolumeControlDialog>(
                "볼륨 제어",
                parameters,
                new DialogOptions
                {
                    Width = "600px",
                    Height = "auto",
                    Resizable = false,
                    Draggable = true
                });

            if (result is bool saved && saved)
            {
                // 볼륨이 저장되었으면 현재 채널 정보를 다시 로드
                if (selectedChannel != null)
                {
                    var query = new Radzen.Query
                    {
                        Filter = $"Id eq {selectedChannel.Id}"
                    };
                    var updatedChannel = await WicsService.GetChannels(query);
                    if (updatedChannel.Value.Any())
                    {
                        selectedChannel = updatedChannel.Value.First();
                        micVolume = (int)(selectedChannel.MicVolume * 100);
                        mediaVolume = (int)(selectedChannel.MediaVolume * 100);
                        ttsVolume = (int)(selectedChannel.TtsVolume * 100);
                    }
                }

                NotifyInfo("볼륨 설정", "볼륨 설정이 업데이트되었습니다.");
            }
        }
        #endregion

        #region Broadcast Control
        protected async Task StartBroadcast()
        {
            _logger.LogInformation("StartBroadcast 메서드 호출됨");

            if (!ValidateBroadcastPrerequisites()) return;

            try
            {
                var onlineSpeakers = speakerSection.GetOnlineSpeakers();
                var offlineSpeakers = speakerSection.GetOfflineSpeakers();

                if (!await ValidateAndNotifyOnlineStatus(onlineSpeakers, offlineSpeakers))
                    return;

                var onlineGroups = speakerSection.GetSelectedGroups();

                // DB 저장 작업
                _logger.LogInformation("1단계: DB 저장 작업");
                LoggingService.AddLog("INFO", "미디어 선택사항 DB 저장");
                await SaveSelectedMediaToChannel();
                LoggingService.AddLog("INFO", "TTS 선택사항 DB 저장");
                await SaveSelectedTtsToChannel();

                // 마이크 초기화
                _logger.LogInformation("2단계: 오디오 믹서 초기화 (필요시)");
                if (!await InitializeAudioMixer())
                    return;

                _logger.LogInformation("3단계: WebSocket 연결 시작");
                LoggingService.AddLog("INFO", "WebSocket 연결 중...");

                if (!await InitializeWebSocketBroadcast(onlineGroups))
                    return;

                // 마이크 활성화
                _logger.LogInformation("4단계: 마이크 활성화 (필요시)");
                if (!await EnableMicrophone())
                {
                    await CleanupFailedBroadcast();
                    return;
                }

                InitializeBroadcastState();

                await CreateBroadcastRecords(onlineSpeakers);

                NotifyBroadcastStarted(onlineSpeakers, offlineSpeakers);

                _jsModule = _mixerModule;

                await InvokeAsync(StateHasChanged);
            }
            catch (Exception ex)
            {
                await HandleBroadcastError(ex);
            }
        }

        private bool ValidateBroadcastPrerequisites()
        {
            if (selectedChannel == null)
            {
                NotifyWarn("채널 선택", "먼저 방송할 채널을 선택하세요.");
                return false;
            }

            if (speakerSection == null || !speakerSection.GetSelectedGroups().Any())
            {
                NotifyWarn("스피커 그룹 선택", "방송할 스피커 그룹을 선택하세요.");
                return false;
            }

            return true;
        }

        private async Task<bool> ValidateAndNotifyOnlineStatus(
            List<WicsPlatform.Server.Models.wics.Speaker> onlineSpeakers,
            List<WicsPlatform.Server.Models.wics.Speaker> offlineSpeakers)
        {
            if (!onlineSpeakers.Any())
            {
                NotifyError("방송 불가", new Exception("온라인 상태인 스피커가 없습니다. 스피커 상태를 확인해 주세요."));
                return false;
            }

            if (offlineSpeakers.Any())
            {
                var offlineNames = string.Join(", ", offlineSpeakers.Select(s => s.Name));
                NotifyWarn("오프라인 스피커 제외", $"다음 스피커는 오프라인 상태여서 방송에서 제외됩니다: {offlineNames}");
                await Task.Delay(2000);
            }

            return true;
        }

        private async Task CreateBroadcastRecords(List<WicsPlatform.Server.Models.wics.Speaker> onlineSpeakers)
        {
            try
            {
                var selectedMediaIds = new List<ulong>();
                if (playlistSection != null)
                {
                    var selectedMedia = playlistSection.GetSelectedMedia();
                    selectedMediaIds = selectedMedia.Select(m => m.Id).ToList();
                }

                var selectedTtsIds = new List<ulong>();
                if (ttsSection != null && ttsSection.HasSelectedTts())
                {
                    selectedTtsIds = ttsSection.GetSelectedTts().Select(t => t.Id).ToList();
                }

                var broadcast = new WicsPlatform.Server.Models.wics.Broadcast
                {
                    ChannelId = selectedChannel.Id,
                    SpeakerIdList = string.Join(' ', onlineSpeakers.Select(s => s.Id)),
                    MediaIdList = string.Join(' ', selectedMediaIds),
                    TtsIdList = string.Join(' ', selectedTtsIds),
                    LoopbackYn = "N",
                    OngoingYn = "Y",
                    CreatedAt = DateTime.Now,
                    UpdatedAt = DateTime.Now
                };

                await WicsService.CreateBroadcast(broadcast);

                _logger.LogInformation($"Broadcast created: Channel={selectedChannel.Id}, " +
                                      $"SpeakerIdList='{broadcast.SpeakerIdList}', " +
                                      $"MediaIdList='{broadcast.MediaIdList}', " +
                                      $"TtsIdList='{broadcast.TtsIdList}'");

                _currentLoopbackSetting = false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create broadcast record");
                NotifyWarn("기록 생성 실패", "방송 기록 생성 중 오류가 발생했습니다. 방송은 계속됩니다.");
            }
        }

        private async Task UpdateBroadcastRecordsToStopped()
        {
            try
            {
                if (selectedChannel == null)
                {
                    _logger.LogWarning("UpdateBroadcastRecordsToStopped: selectedChannel is null");
                    return;
                }

                var query = new Radzen.Query
                {
                    Filter = $"ChannelId eq {selectedChannel.Id} and OngoingYn eq 'Y'"
                };

                var result = await WicsService.GetBroadcasts(query);

                if (result?.Value == null || !result.Value.Any())
                {
                    _logger.LogInformation("No ongoing broadcasts to update");
                    return;
                }

                _logger.LogInformation($"Updating {result.Value.Count()} ongoing broadcasts to stopped");

                foreach (var broadcast in result.Value)
                {
                    try
                    {
                        var updateData = new
                        {
                            OngoingYn = "N",
                            UpdatedAt = DateTime.Now
                        };

                        var response = await Http.PatchAsJsonAsync(
                            $"odata/wics/Broadcasts(Id={broadcast.Id})",
                            updateData
                        );

                        if (response.IsSuccessStatusCode)
                        {
                            _logger.LogInformation($"Successfully updated broadcast {broadcast.Id} to stopped");
                        }
                        else
                        {
                            var content = await response.Content.ReadAsStringAsync();
                            _logger.LogError($"Failed to update broadcast {broadcast.Id}: {response.StatusCode} - {content}");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Error updating broadcast {broadcast.Id}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update broadcast records");
                throw;
            }
        }

        private void InitializeBroadcastState()
        {
            isBroadcasting = true;
            broadcastStartTime = DateTime.Now;
            totalDataPackets = 0;
            totalDataSize = 0.0;
            audioLevel = 0.0;

            _broadcastTimer = new System.Threading.Timer(
                UpdateBroadcastTime,
                null,
                TimeSpan.Zero,
                TimeSpan.FromSeconds(1));
        }

        private void NotifyBroadcastStarted(
            List<WicsPlatform.Server.Models.wics.Speaker> onlineSpeakers,
            List<WicsPlatform.Server.Models.wics.Speaker> offlineSpeakers)
        {
            if (offlineSpeakers.Any())
            {
                NotifyInfo("방송 시작",
                    $"'{selectedChannel.Name}' 채널 방송을 시작했습니다. " +
                    $"온라인 스피커 {onlineSpeakers.Count}대로 방송 중입니다.");
            }
            else
            {
                NotifySuccess("방송 시작",
                    $"'{selectedChannel.Name}' 채널 방송이 정상적으로 시작되었습니다. " +
                    $"모든 스피커({onlineSpeakers.Count}대)가 온라인 상태입니다.");
            }
        }

        private async Task CleanupFailedBroadcast()
        {
            await StopWebSocketBroadcast();
            currentBroadcastId = null;
            await CleanupMicrophone();
        }

        private async Task HandleBroadcastError(Exception ex)
        {
            isBroadcasting = false;
            currentBroadcastId = null;

            if (ex is JSException)
                NotifyError("방송 시작(JS 오류)", ex);
            else
                NotifyError("방송 시작", ex);

            await InvokeAsync(StateHasChanged);
        }
        #endregion

        #region Loopback Control
        private async Task<bool> GetLoopbackSetting()
        {
            try
            {
                var query = new Radzen.Query
                {
                    Filter = $"ChannelId eq {selectedChannel.Id} and OngoingYn eq 'Y'",
                    Top = 1,
                    OrderBy = "CreatedAt desc"
                };

                var broadcasts = await WicsService.GetBroadcasts(query);
                var currentBroadcast = broadcasts.Value.FirstOrDefault();

                _currentLoopbackSetting = currentBroadcast?.LoopbackYn == "Y";
                return _currentLoopbackSetting;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get loopback setting");
                return false;
            }
        }

        protected async Task ToggleLoopback()
        {
            try
            {
                if (!isBroadcasting)
                {
                    NotifyWarn("방송 필요", "방송이 시작된 상태에서만 루프백 설정을 변경할 수 있습니다.");
                    return;
                }

                var query = new Radzen.Query
                {
                    Filter = $"ChannelId eq {selectedChannel.Id} and OngoingYn eq 'Y'"
                };

                var broadcasts = await WicsService.GetBroadcasts(query);
                var currentLoopback = broadcasts.Value.FirstOrDefault()?.LoopbackYn == "Y";
                var newLoopback = !currentLoopback;

                foreach (var broadcast in broadcasts.Value)
                {
                    broadcast.LoopbackYn = newLoopback ? "Y" : "N";
                    broadcast.UpdatedAt = DateTime.Now;
                    await WicsService.UpdateBroadcast(broadcast.Id, broadcast);
                }

                _currentLoopbackSetting = newLoopback;

                if (newLoopback && _speakerModule == null)
                {
                    await InitializeSpeakerModule();
                }

                NotifySuccess("루프백 설정 변경",
                    $"루프백이 {(newLoopback ? "활성화" : "비활성화")}되었습니다.");

                await InvokeAsync(StateHasChanged);
            }
            catch (Exception ex)
            {
                NotifyError("루프백 설정 변경", ex);
            }
        }
        #endregion

        #region Event Handlers
        protected async Task OnBroadcastStatusChanged(string status)
        {
            switch (status)
            {
                case "started":
                    isBroadcasting = true;
                    break;
                case "stopped":
                case "paused":
                    await HandleBroadcastStopped();
                    break;
            }
            await InvokeAsync(StateHasChanged);
        }

        protected async Task OnBroadcastStopped(BroadcastStoppedEventArgs args)
        {
            await HandleBroadcastStopped();
            LogBroadcastStatistics(args);
            await InvokeAsync(StateHasChanged);
        }

        protected Task TogglePanel(string panelName)
        {
            switch (panelName)
            {
                case "speakerGroup": speakerGroupPanelCollapsed = !speakerGroupPanelCollapsed; break;
                case "tts": ttsPanelCollapsed = !ttsPanelCollapsed; break;
                case "monitoring": monitoringPanelCollapsed = !monitoringPanelCollapsed; break;
            }
            return Task.CompletedTask;
        }
        #endregion

        #region Timer Callbacks
        private async void UpdateBroadcastTime(object state)
        {
            if (!isBroadcasting) return;

            var elapsed = DateTime.Now - broadcastStartTime;
            broadcastDuration = elapsed.ToString(@"hh\:mm\:ss");

            if (totalDataPackets > 0 && elapsed.TotalSeconds > 0)
            {
                averageBitrate = (totalDataSize * 8) / elapsed.TotalSeconds / 1000;
            }

            if (monitoringSection != null)
            {
                await monitoringSection.UpdateBroadcastData(
                    audioLevel, broadcastStartTime, broadcastDuration,
                    totalDataPackets, totalDataSize, averageBitrate, sampleRate);
            }

            try
            {
                await InvokeAsync(StateHasChanged);
            }
            catch (ObjectDisposedException)
            {
                return;
            }
        }
        #endregion

        #region Helper Methods
        private async Task HandleBroadcastStopped()
        {
            try
            {
                _logger.LogInformation("HandleBroadcastStopped 시작");

                isBroadcasting = false;
                monitoringPanelCollapsed = false;

                if (_broadcastTimer != null)
                {
                    _broadcastTimer.Dispose();
                    _broadcastTimer = null;
                    _logger.LogInformation("브로드캐스트 타이머 정리 완료");
                }

                try
                {
                    await UpdateBroadcastRecordsToStopped();
                    _logger.LogInformation("DB 업데이트 완료");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "DB 업데이트 실패");
                }

                await StopWebSocketBroadcast();
                await CleanupMicrophone();

                _currentLoopbackSetting = false;

                await InvokeAsync(StateHasChanged);

                _logger.LogInformation("HandleBroadcastStopped 완료");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "HandleBroadcastStopped 전체 오류");

                isBroadcasting = false;
                currentBroadcastId = null;
                _mixerModule = null;
                _jsModule = null;
                _speakerModule = null;
                _currentLoopbackSetting = false;

                try
                {
                    await InvokeAsync(StateHasChanged);
                }
                catch { }
            }
        }

        private async Task SaveSelectedMediaToChannel()
        {
            try
            {
                if (selectedChannel == null) return;

                if (playlistSection == null)
                {
                    _logger.LogInformation("플레이리스트 섹션이 초기화되지 않았습니다.");
                    LoggingService.AddLog("WARN", "플레이리스트 섹션이 초기화되지 않았습니다.");
                    return;
                }

                var selectedMedia = playlistSection.GetSelectedMedia();
                var selectedMediaIds = selectedMedia?.Select(m => m.Id).ToHashSet() ?? new HashSet<ulong>();

                LoggingService.AddLog("INFO", $"미디어 매핑 동기화 시작 - 선택된 미디어: {selectedMediaIds.Count}개");

                var query = new Radzen.Query
                {
                    Filter = $"ChannelId eq {selectedChannel.Id}"
                };
                var existingMappings = await WicsService.GetMapChannelMedia(query);
                var existingMediaIds = existingMappings.Value
                    .Where(m => m.DeleteYn != "Y")
                    .Select(m => m.MediaId)
                    .ToHashSet();

                var toAdd = selectedMediaIds.Except(existingMediaIds);
                foreach (var mediaId in toAdd)
                {
                    var media = selectedMedia.FirstOrDefault(m => m.Id == mediaId);
                    if (media != null)
                    {
                        var mapping = new WicsPlatform.Server.Models.wics.MapChannelMedium
                        {
                            ChannelId = selectedChannel.Id,
                            MediaId = mediaId,
                            DeleteYn = "N",
                            CreatedAt = DateTime.Now,
                            UpdatedAt = DateTime.Now
                        };

                        await WicsService.CreateMapChannelMedium(mapping);
                        LoggingService.AddLog("SUCCESS", $"신규 미디어 추가: {media.FileName} (ID: {mediaId})");
                    }
                }

                var toDelete = existingMediaIds.Except(selectedMediaIds);
                foreach (var mediaId in toDelete)
                {
                    var mapping = existingMappings.Value.FirstOrDefault(m => m.MediaId == mediaId && m.DeleteYn != "Y");
                    if (mapping != null)
                    {
                        var updateData = new
                        {
                            DeleteYn = "Y",
                            UpdatedAt = DateTime.Now
                        };

                        var response = await Http.PatchAsJsonAsync($"odata/wics/MapChannelMedia(Id={mapping.Id})", updateData);

                        if (response.IsSuccessStatusCode)
                        {
                            LoggingService.AddLog("INFO", $"미디어 제거: MediaID {mediaId} (delete_yn=Y)");
                        }
                        else
                        {
                            LoggingService.AddLog("ERROR", $"미디어 제거 실패: MediaID {mediaId}, Status: {response.StatusCode}");
                        }
                    }
                }

                LoggingService.AddLog("SUCCESS",
                    $"미디어 매핑 동기화 완료 - 추가: {toAdd.Count()}개, 제거: {toDelete.Count()}개");
            }
            catch (Exception ex)
            {
                LoggingService.AddLog("ERROR", $"미디어 매핑 실패: {ex.Message}");
                _logger.LogError(ex, "Failed to save selected media to channel");
            }
        }

        private async Task SaveSelectedTtsToChannel()
        {
            try
            {
                if (selectedChannel == null) return;

                if (ttsSection == null)
                {
                    _logger.LogInformation("TTS 섹션이 초기화되지 않았습니다.");
                    LoggingService.AddLog("WARN", "TTS 섹션이 초기화되지 않았습니다.");
                    return;
                }

                var selectedTts = ttsSection.GetSelectedTts();
                var selectedTtsIds = selectedTts?.Select(t => t.Id).ToHashSet() ?? new HashSet<ulong>();

                LoggingService.AddLog("INFO", $"TTS 매핑 동기화 시작 - 선택된 TTS: {selectedTtsIds.Count}개");

                var query = new Radzen.Query
                {
                    Filter = $"ChannelId eq {selectedChannel.Id}"
                };

                var existingMappings = await WicsService.GetMapChannelTts(query);
                var existingTtsIds = existingMappings.Value
                    .Where(m => m.DeleteYn != "Y")
                    .Select(m => m.TtsId)
                    .ToHashSet();

                var toAdd = selectedTtsIds.Except(existingTtsIds);
                foreach (var ttsId in toAdd)
                {
                    var tts = selectedTts.FirstOrDefault(t => t.Id == ttsId);
                    if (tts != null)
                    {
                        var mapping = new WicsPlatform.Server.Models.wics.MapChannelTt
                        {
                            ChannelId = selectedChannel.Id,
                            TtsId = ttsId,
                            DeleteYn = "N",
                            CreatedAt = DateTime.Now,
                            UpdatedAt = DateTime.Now
                        };

                        await WicsService.CreateMapChannelTt(mapping);
                        LoggingService.AddLog("SUCCESS", $"신규 TTS 추가: {tts.Name} (ID: {ttsId})");
                    }
                }

                var toDelete = existingTtsIds.Except(selectedTtsIds);
                foreach (var ttsId in toDelete)
                {
                    var mapping = existingMappings.Value.FirstOrDefault(m => m.TtsId == ttsId && m.DeleteYn != "Y");
                    if (mapping != null)
                    {
                        var updateData = new
                        {
                            DeleteYn = "Y",
                            UpdatedAt = DateTime.Now
                        };

                        var response = await Http.PatchAsJsonAsync($"odata/wics/MapChannelTts(Id={mapping.Id})", updateData);

                        if (response.IsSuccessStatusCode)
                        {
                            LoggingService.AddLog("INFO", $"TTS 제거: TtsID {ttsId} (delete_yn=Y)");
                        }
                    }
                }

                LoggingService.AddLog("SUCCESS",
                    $"TTS 매핑 동기화 완료 - 추가: {toAdd.Count()}개, 제거: {toDelete.Count()}개");
            }
            catch (Exception ex)
            {
                LoggingService.AddLog("ERROR", $"TTS 매핑 실패: {ex.Message}");
                _logger.LogError(ex, "Failed to save selected TTS to channel");
            }
        }

        private void LogBroadcastStatistics(BroadcastStoppedEventArgs args)
        {
            _logger.LogInformation($"방송 종료 - 시간: {args.Duration}, 패킷: {args.TotalPackets}, 데이터: {args.TotalDataSize}KB");
        }

        private void ResetAllPanels()
        {
            speakerGroupPanelCollapsed =
                playlistPanelCollapsed = ttsPanelCollapsed = monitoringPanelCollapsed = false;
        }

        private void DisposeResources()
        {
            _dotNetRef?.Dispose();
            _broadcastTimer?.Dispose();
            RecordingService?.Dispose();
        }

        // UI Helpers
        protected string GetChannelIcon(byte type) => type switch
        {
            0 => "settings_input_antenna",
            1 => "podcasts",
            2 => "campaign",
            _ => "radio"
        };

        protected BadgeStyle GetChannelBadgeStyle(sbyte state) => state switch
        {
            1 => BadgeStyle.Success,
            0 => BadgeStyle.Danger,
            -1 => BadgeStyle.Warning,
            _ => BadgeStyle.Light
        };

        protected string GetChannelStateText(sbyte state) => state switch
        {
            1 => "활성",
            0 => "비활성",
            -1 => "일시정지",
            _ => "알 수 없음"
        };

        // Notification Helpers
        private void Notify(NotificationSeverity severity, string summary, string detail, int duration = 4000) =>
            NotificationService.Notify(new NotificationMessage { Severity = severity, Summary = summary, Detail = detail, Duration = duration });

        private void NotifySuccess(string summary, string detail) =>
            Notify(NotificationSeverity.Success, summary, detail);

        private void NotifyError(string summary, Exception ex) =>
            Notify(NotificationSeverity.Error, "오류", $"{summary} 중 오류: {ex.Message}");

        private void NotifyWarn(string summary, string detail) =>
            Notify(NotificationSeverity.Warning, summary, detail);

        private void NotifyInfo(string summary, string detail) =>
            Notify(NotificationSeverity.Info, summary, detail);
        #endregion
    }
}