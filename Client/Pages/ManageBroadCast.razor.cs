using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.JSInterop;
using Microsoft.AspNetCore.Components;
using Radzen;
using Radzen.Blazor;
using System.Net.Http;
using System.Net.Http.Json;
using WicsPlatform.Client.Dialogs;
using WicsPlatform.Client.Pages.SubPages;
using WicsPlatform.Client.Services;
using Microsoft.Extensions.Logging;
using System.Threading;
using WicsPlatform.Client.Services.Interfaces;

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
        [Inject] protected MediaStreamingService MediaStreamingService { get; set; }
        [Inject] protected BroadcastRecordingService RecordingService { get; set; }
        [Inject] protected BroadcastLoggingService LoggingService { get; set; }
        [Inject] protected TtsStreamingService TtsStreamingService { get; set; }
        #endregion

        #region Fields & Properties
        // 채널 관련
        protected string newChannelName = "";
        protected IEnumerable<WicsPlatform.Server.Models.wics.Channel> channels = new List<WicsPlatform.Server.Models.wics.Channel>();
        protected WicsPlatform.Server.Models.wics.Channel selectedChannel = null;
        protected bool isLoading = true;
        protected Dictionary<ulong, bool> isChannelBroadcasting = new Dictionary<ulong, bool>();

        // 그룹 및 스피커 관련
        protected IEnumerable<WicsPlatform.Server.Models.wics.Group> speakerGroups = new List<WicsPlatform.Server.Models.wics.Group>();
        protected List<ulong> selectedGroups = new List<ulong>();
        protected bool isLoadingGroups = false;
        protected IEnumerable<WicsPlatform.Server.Models.wics.Speaker> allSpeakers = new List<WicsPlatform.Server.Models.wics.Speaker>();
        protected bool isLoadingSpeakers = false;
        protected IEnumerable<WicsPlatform.Server.Models.wics.MapSpeakerGroup> speakerGroupMappings = new List<WicsPlatform.Server.Models.wics.MapSpeakerGroup>();

        // UI 상태
        protected bool speakerGroupPanelCollapsed = false;
        protected bool volumePanelCollapsed = false;
        protected bool playlistPanelCollapsed = false;
        protected bool ttsPanelCollapsed = false;
        protected bool monitoringPanelCollapsed = false;

        // 방송 상태
        protected bool isBroadcasting = false;
        protected string currentBroadcastId = null;
        protected DateTime broadcastStartTime = DateTime.Now;
        protected string broadcastDuration = "00:00:00";
        protected int totalDataPackets = 0;
        protected double totalDataSize = 0.0;
        protected double audioLevel = 0.0;
        protected double averageBitrate = 0.0;
        protected int sampleRate = 44100;
        private System.Threading.Timer _broadcastTimer;

        // 방송 소스 선택
        protected bool isMicEnabled = true;
        protected bool isMediaEnabled = false;
        protected bool isTtsEnabled = false;

        // 테스트 방송 상태
        protected bool isTestBroadcasting = false;
        protected string testBroadcastId = null;
        private System.Threading.Timer _testDataTimer;
        private Random _testRandom = new Random();

        // 녹음 관련 (RecordingService로 위임)
        protected bool isRecording => RecordingService.IsRecording;
        protected string recordingDuration => RecordingService.RecordingDuration;
        protected double recordingDataSize => RecordingService.RecordingDataSize;

        // JS Interop
        private IJSObjectReference _jsModule;
        private IJSObjectReference _speakerModule;
        private DotNetObjectReference<ManageBroadCast> _dotNetRef;
        protected BroadcastMonitoringSection monitoringSection;
        protected BroadcastPlaylistSection playlistSection;
        protected BroadcastTtsSection ttsSection;

        // Broadcast 관련
        private List<WicsPlatform.Server.Models.wics.Speaker> _currentOnlineSpeakers;
        private bool _currentLoopbackSetting = false;
        #endregion

        #region Audio Configuration
        public class AudioConfiguration
        {
            public int SampleRate { get; set; } = 44100;
            public int ChannelCount { get; set; } = 2;
            public bool EchoCancellation { get; set; } = true;
            public bool NoiseSuppression { get; set; } = true;
            public bool AutoGainControl { get; set; } = true;
            public string DeviceId { get; set; }
            public string GroupId { get; set; }
        }

        private AudioConfiguration _currentAudioConfig = new AudioConfiguration();
        private int _preferredSampleRate = 48000;
        private int _preferredChannels = 2;
        private int _preferredBitrate = 64000;

        private List<SampleRateOption> sampleRateOptions = new List<SampleRateOption>
        {
            new SampleRateOption { Value = 8000, Text = "8000 Hz (전화품질)" },
            new SampleRateOption { Value = 16000, Text = "16000 Hz (광대역)" },
            new SampleRateOption { Value = 24000, Text = "24000 Hz (고품질)" },
            new SampleRateOption { Value = 48000, Text = "48000 Hz (프로페셔널)" }
        };

        private List<ChannelOption> channelOptions = new List<ChannelOption>
        {
            new ChannelOption { Value = 1, Text = "모노 (1채널)" },
            new ChannelOption { Value = 2, Text = "스테레오 (2채널)" }
        };

        public class SampleRateOption
        {
            public int Value { get; set; }
            public string Text { get; set; }
        }

        public class ChannelOption
        {
            public int Value { get; set; }
            public string Text { get; set; }
        }
        #endregion

        #region Lifecycle Methods
        protected override async Task OnInitializedAsync()
        {
            SubscribeToWebSocketEvents();
            SubscribeToRecordingEvents();
            await LoadInitialData();
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
                speakerGroups = initialData.SpeakerGroups;
                allSpeakers = initialData.AllSpeakers;
                speakerGroupMappings = initialData.SpeakerGroupMappings;
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
            selectedGroups.Clear();
            ResetAllPanels();

            if (channel != null)
            {
                var channelSampleRate = (int)(channel.SamplingRate > 0 ? channel.SamplingRate : 48000);
                var supportedSampleRates = sampleRateOptions.Select(o => o.Value).ToArray();
                _preferredSampleRate = FindClosestSampleRate(channelSampleRate, supportedSampleRates);

                if (_preferredSampleRate != channelSampleRate && channel.SamplingRate > 0)
                {
                    await BroadcastDataService.UpdateChannelAudioSettingsAsync(channel, _preferredSampleRate, null);
                }

                _preferredChannels = channel.Channel1 == "mono" ? 1 : 2;
                _logger.LogInformation($"Channel selected: {channel.Name}, SampleRate: {_preferredSampleRate}Hz, Channels: {_preferredChannels}");
            }

            await InvokeAsync(StateHasChanged);
        }

        private int FindClosestSampleRate(int targetSampleRate, int[] supportedSampleRates)
        {
            if (supportedSampleRates.Contains(targetSampleRate))
                return targetSampleRate;

            return supportedSampleRates
                .OrderBy(rate => Math.Abs(rate - targetSampleRate))
                .First();
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

        #region Broadcast Control
        protected async Task StartBroadcast()
        {
            _logger.LogInformation("StartBroadcast 메서드 호출됨");

            if (!ValidateBroadcastPrerequisites()) return;

            try
            {
                var (onlineSpeakers, offlineSpeakers) = GetSpeakersByStatus();

                if (!await ValidateAndNotifyOnlineStatus(onlineSpeakers, offlineSpeakers))
                    return;

                _currentOnlineSpeakers = onlineSpeakers;

                var onlineGroups = GetOnlineGroups(onlineSpeakers);

                if (!await InitializeWebSocketBroadcast(onlineGroups))
                    return;

                if (!await InitializeAudioModules())
                    return;

                // 선택된 미디어를 채널에 매핑 (방송 시작 전에 저장)
                if (isMediaEnabled)
                {
                    await SaveSelectedMediaToChannel();
                }

                if (isTtsEnabled)
                {
                    await SaveSelectedTtsToChannel();
                }

                // 활성화된 소스별 스트리밍 시작
                if (!await StartEnabledSources())
                {
                    await CleanupFailedBroadcast();
                    return;
                }

                InitializeBroadcastState();
                await CreateBroadcastRecords(onlineSpeakers);
                NotifyBroadcastStarted(onlineSpeakers, offlineSpeakers);
                await InvokeAsync(StateHasChanged);
            }
            catch (Exception ex)
            {
                await HandleBroadcastError(ex);
            }
        }

        // 통합된 소스 시작 메서드
        private async Task<bool> StartEnabledSources()
        {
            // 마이크 스트리밍
            if (isMicEnabled)
            {
                if (!await StartMicrophoneRecording())
                    return false;
            }

            // 미디어 스트리밍
            if (isMediaEnabled)
            {
                if (!await MediaStreamingService.StartMediaStreaming(
                    isMediaEnabled, _dotNetRef, selectedChannel,
                    _preferredSampleRate, _preferredChannels))
                    return false;
            }

            // TTS 스트리밍 - ★ ttsSection 전달
            if (isTtsEnabled)
            {
                if (!await TtsStreamingService.StartTtsStreaming(
                    isTtsEnabled, _dotNetRef, selectedChannel,
                    _preferredSampleRate, _preferredChannels, ttsSection))
                    return false;
            }

            return true;
        }

        private async Task CreateBroadcastRecords(List<WicsPlatform.Server.Models.wics.Speaker> onlineSpeakers)
        {
            try
            {
                var selectedMediaIds = await MediaStreamingService.GetSelectedMediaIds(selectedChannel);

                // ★ TTS 섹션에서 직접 가져오기
                var selectedTtsIds = new List<ulong>();
                if (ttsSection != null && ttsSection.HasSelectedTts())
                {
                    selectedTtsIds = ttsSection.GetSelectedTts().Select(t => t.Id).ToList();
                }

                var broadcast = new WicsPlatform.Server.Models.wics.Broadcast
                {
                    ChannelId = selectedChannel.Id,
                    SpeakerIds = onlineSpeakers.Select(s => s.Id).ToList(),
                    MediaIds = selectedMediaIds,
                    TtsIds = selectedTtsIds,
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
                if (selectedChannel == null) return;

                var query = new Radzen.Query
                {
                    Filter = $"ChannelId eq {selectedChannel.Id} and OngoingYn eq 'Y'"
                };

                var result = await WicsService.GetBroadcasts(query);

                foreach (var broadcast in result.Value)
                {
                    var updateData = new
                    {
                        OngoingYn = "N",
                        UpdatedAt = DateTime.Now
                    };

                    // OData URL 형식 수정: Id={broadcast.Id} 형식 사용
                    var response = await Http.PatchAsJsonAsync($"odata/wics/Broadcasts(Id={broadcast.Id})", updateData);

                    if (response.IsSuccessStatusCode)
                    {
                        _logger.LogInformation($"Successfully updated broadcast {broadcast.Id} to stopped");
                    }
                    else
                    {
                        _logger.LogError($"Failed to update broadcast {broadcast.Id}: {response.StatusCode}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update broadcast records");
            }
        }

        private bool ValidateBroadcastPrerequisites()
        {
            if (selectedChannel == null)
            {
                NotifyWarn("채널 선택", "먼저 방송할 채널을 선택하세요.");
                return false;
            }

            if (selectedGroups == null || !selectedGroups.Any())
            {
                NotifyWarn("스피커 그룹 선택", "방송할 스피커 그룹을 선택하세요.");
                return false;
            }

            if (!isMicEnabled && !isMediaEnabled && !isTtsEnabled)
            {
                NotifyWarn("방송 소스 선택", "최소 하나의 방송 소스(마이크, 미디어, TTS)를 활성화해주세요.");
                return false;
            }

            return true;
        }

        private (List<WicsPlatform.Server.Models.wics.Speaker> online, List<WicsPlatform.Server.Models.wics.Speaker> offline) GetSpeakersByStatus()
        {
            var selectedSpeakerIds = speakerGroupMappings
                .Where(m => selectedGroups.Contains(m.GroupId))
                .Select(m => m.SpeakerId)
                .Distinct()
                .ToList();

            var online = allSpeakers
                .Where(s => selectedSpeakerIds.Contains(s.Id) && s.State == 1)
                .ToList();

            var offline = allSpeakers
                .Where(s => selectedSpeakerIds.Contains(s.Id) && s.State != 1)
                .ToList();

            return (online, offline);
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

        private List<ulong> GetOnlineGroups(List<WicsPlatform.Server.Models.wics.Speaker> onlineSpeakers)
        {
            var onlineSpeakerIds = onlineSpeakers.Select(s => s.Id).ToList();
            return speakerGroupMappings
                .Where(m => onlineSpeakerIds.Contains(m.SpeakerId) && selectedGroups.Contains(m.GroupId))
                .Select(m => m.GroupId)
                .Distinct()
                .ToList();
        }

        private async Task<bool> InitializeWebSocketBroadcast(List<ulong> onlineGroups)
        {
            var response = await WebSocketService.StartBroadcastAsync(selectedChannel.Id, onlineGroups);

            if (!response.Success)
            {
                NotifyError("방송 시작 실패", new Exception(response.Error));
                return false;
            }

            currentBroadcastId = response.BroadcastId;
            _logger.LogInformation($"WebSocket broadcast started with ID: {currentBroadcastId}");
            return true;
        }

        private async Task<bool> InitializeAudioModules()
        {
            if (_jsModule == null)
            {
                _jsModule = await JSRuntime.InvokeAsync<IJSObjectReference>("import", "./js/mic.js");
                _dotNetRef = DotNetObjectReference.Create(this);
            }

            if (_currentLoopbackSetting && _speakerModule == null)
            {
                _speakerModule = await JSRuntime.InvokeAsync<IJSObjectReference>("import", "./js/speaker.js");
                await _speakerModule.InvokeVoidAsync("init");
            }

            return true;
        }

        private async Task<bool> StartMicrophoneRecording()
        {
            const int MicTimesliceMs = 50;

            var channelSampleRate = selectedChannel?.SamplingRate > 0 ? (int)selectedChannel.SamplingRate : _preferredSampleRate;
            var channelChannels = selectedChannel?.Channel1 == "mono" ? 1 : _preferredChannels;

            var micConfig = new
            {
                timeslice = MicTimesliceMs,
                sampleRate = channelSampleRate,
                channels = channelChannels,
                bitrate = _preferredBitrate,
                echoCancellation = true,
                noiseSuppression = true,
                autoGainControl = true
            };

            var ok = await _jsModule.InvokeAsync<bool>("start", _dotNetRef, micConfig);

            if (!ok)
            {
                NotifyWarn("권한 필요", "마이크 권한을 허용하셔야 방송을 시작할 수 있습니다.");
                return false;
            }

            _logger.LogInformation($"Microphone started with SampleRate: {channelSampleRate}Hz, Channels: {channelChannels}");
            return true;
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
            var enabledSources = new List<string>();
            if (isMicEnabled) enabledSources.Add("마이크");
            if (isMediaEnabled) enabledSources.Add("미디어");
            if (isTtsEnabled) enabledSources.Add("TTS");

            var sourcesText = string.Join(", ", enabledSources);

            if (offlineSpeakers.Any())
            {
                NotifyInfo("방송 시작",
                    $"'{selectedChannel.Name}' 채널 방송을 시작했습니다. " +
                    $"온라인 스피커 {onlineSpeakers.Count}대로 방송 중입니다. " +
                    $"활성 소스: {sourcesText}");
            }
            else
            {
                NotifySuccess("방송 시작",
                    $"'{selectedChannel.Name}' 채널 방송이 정상적으로 시작되었습니다. " +
                    $"모든 스피커({onlineSpeakers.Count}대)가 온라인 상태입니다. " +
                    $"활성 소스: {sourcesText}");
            }
        }

        private async Task CleanupFailedBroadcast()
        {
            await WebSocketService.StopBroadcastAsync(currentBroadcastId);
            currentBroadcastId = null;
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
                    _speakerModule = await JSRuntime.InvokeAsync<IJSObjectReference>("import", "./js/speaker.js");
                    await _speakerModule.InvokeVoidAsync("init");
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

        #region Test Broadcast Control
        protected async Task ToggleTestBroadcast()
        {
            if (!isTestBroadcasting)
            {
                await StartTestBroadcast();
            }
            else
            {
                await StopTestBroadcast();
            }
        }

        protected async Task StartTestBroadcast()
        {
            if (!ValidateBroadcastPrerequisites()) return;

            try
            {
                var (onlineSpeakers, offlineSpeakers) = GetSpeakersByStatus();

                if (!onlineSpeakers.Any())
                {
                    NotifyError("방송 불가", new Exception("온라인 상태인 스피커가 없습니다."));
                    return;
                }

                var onlineGroups = GetOnlineGroups(onlineSpeakers);

                var response = await WebSocketService.StartBroadcastAsync(selectedChannel.Id, onlineGroups);

                if (!response.Success)
                {
                    NotifyError("테스트 방송 시작 실패", new Exception(response.Error));
                    return;
                }

                testBroadcastId = response.BroadcastId;
                isTestBroadcasting = true;

                _testDataTimer = new System.Threading.Timer(
                    SendTestData,
                    null,
                    TimeSpan.Zero,
                    TimeSpan.FromMilliseconds(50));

                NotifySuccess("테스트 방송 시작",
                    $"'{selectedChannel.Name}' 채널에서 테스트 방송을 시작했습니다. " +
                    $"랜덤 데이터를 50ms마다 전송합니다.");

                await InvokeAsync(StateHasChanged);
            }
            catch (Exception ex)
            {
                isTestBroadcasting = false;
                testBroadcastId = null;
                NotifyError("테스트 방송 시작", ex);
                await InvokeAsync(StateHasChanged);
            }
        }

        protected async Task StopTestBroadcast()
        {
            try
            {
                _testDataTimer?.Dispose();
                _testDataTimer = null;

                if (!string.IsNullOrEmpty(testBroadcastId))
                {
                    await WebSocketService.StopBroadcastAsync(testBroadcastId);
                    testBroadcastId = null;
                }

                isTestBroadcasting = false;

                NotifyInfo("테스트 방송 종료", "테스트 방송이 종료되었습니다.");
                await InvokeAsync(StateHasChanged);
            }
            catch (Exception ex)
            {
                NotifyError("테스트 방송 종료", ex);
            }
        }

        private async void SendTestData(object state)
        {
            if (!isTestBroadcasting || string.IsNullOrEmpty(testBroadcastId))
                return;

            try
            {
                var dataSize = 1024;
                var testData = new byte[dataSize];

                await WebSocketService.SendAudioDataAsync(testBroadcastId, testData);

                Interlocked.Increment(ref totalDataPackets);
                var sizeDelta = dataSize / 1024.0;
                var currentTotal = totalDataSize;
                while (currentTotal != Interlocked.CompareExchange(
                    ref totalDataSize,
                    currentTotal + sizeDelta,
                    currentTotal))
                {
                    currentTotal = totalDataSize;
                }

                if (totalDataPackets % 20 == 0)
                {
                    await InvokeAsync(StateHasChanged);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "테스트 데이터 전송 중 오류");
            }
        }
        #endregion

        #region Recording Control
        protected async Task StartRecording()
        {
            await RecordingService.StartRecording(isBroadcasting);
        }

        protected async Task StopRecording()
        {
            await RecordingService.StopRecording();
        }

        private void SubscribeToRecordingEvents()
        {
            RecordingService.OnRecordingStateChanged += async () => await InvokeAsync(StateHasChanged);
        }

        private void UnsubscribeFromRecordingEvents()
        {
            if (RecordingService != null)
            {
                RecordingService.OnRecordingStateChanged -= async () => await InvokeAsync(StateHasChanged);
            }
        }
        #endregion

        #region Audio Processing - 통합된 오디오 캡처 핸들러
        [JSInvokable]
        public async Task OnAudioCaptured(string base64Data)
        {
            await ProcessAudioData(base64Data, isMicEnabled);
        }

        [JSInvokable]
        public async Task OnMediaAudioCaptured(string base64Data)
        {
            await ProcessAudioData(base64Data, isMediaEnabled);
        }

        // TTS 오디오 캡처 핸들러 추가
        [JSInvokable]
        public async Task OnTtsAudioCaptured(string base64Data)
        {
            await ProcessAudioData(base64Data, isTtsEnabled);
        }

        // 통합된 오디오 처리 메서드
        private async Task ProcessAudioData(string base64Data, bool isSourceEnabled)
        {
            if (!isSourceEnabled || string.IsNullOrWhiteSpace(base64Data))
            {
                _logger.LogWarning($"ProcessAudioData skipped - Enabled: {isSourceEnabled}, Data: {!string.IsNullOrWhiteSpace(base64Data)}");
                return;
            }

            try
            {
                byte[] data = Convert.FromBase64String(base64Data);

                // 어떤 소스에서 온 데이터인지 로그
                string source = "Unknown";
                if (isSourceEnabled == isMicEnabled && isMicEnabled) source = "Mic";
                else if (isSourceEnabled == isMediaEnabled && isMediaEnabled) source = "Media";
                else if (isSourceEnabled == isTtsEnabled && isTtsEnabled) source = "TTS";

                _logger.LogInformation($"ProcessAudioData from {source} - Size: {data.Length} bytes");
                LoggingService.AddLog("DEBUG", $"{source} 데이터 수신: {data.Length} bytes");

                UpdateAudioStatistics(data);
                RecordingService.AddAudioData(data);

                if (monitoringSection != null)
                    await monitoringSection.OnAudioCaptured(data);

                if (!string.IsNullOrEmpty(currentBroadcastId))
                {
                    await WebSocketService.SendAudioDataAsync(currentBroadcastId, data);
                    _logger.LogDebug($"Sent {source} audio data to WebSocket: {data.Length} bytes");
                }
                else
                {
                    _logger.LogWarning("currentBroadcastId is null or empty - cannot send to WebSocket");
                }

                if (_currentLoopbackSetting && _speakerModule != null)
                    await _speakerModule.InvokeVoidAsync("feed", base64Data);

                if (totalDataPackets % 10 == 0)
                {
                    await InvokeAsync(StateHasChanged);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"ProcessAudioData 오류: {ex.Message}");
            }
        }

        private void UpdateAudioStatistics(byte[] data)
        {
            totalDataPackets++;
            totalDataSize += data.Length / 1024.0;
            audioLevel = CalculateAudioLevel(data);
        }

        private double CalculateAudioLevel(byte[] audioData)
        {
            if (audioData.Length < 2) return 0;

            double sum = 0;
            int sampleCount = audioData.Length / 2;

            for (int i = 0; i < audioData.Length - 1; i += 2)
            {
                short sample = (short)(audioData[i] | (audioData[i + 1] << 8));
                double normalized = sample / 32768.0;
                sum += normalized * normalized;
            }

            double rms = Math.Sqrt(sum / sampleCount);
            return Math.Min(100, rms * 100);
        }

        // TTS 플레이리스트 종료 핸들러 추가
        [JSInvokable]
        public async Task OnTtsPlaylistEnded()
        {
            _logger.LogInformation("TTS playlist ended - attempting to restart");

            LoggingService.AddLog("INFO", "TTS 재생 완료");

            // 루프 재생 설정 확인
            var shouldLoop = true;  // 나중에 UI에서 설정 가능하도록

            if (shouldLoop && isTtsEnabled && isBroadcasting)
            {
                LoggingService.AddLog("INFO", "TTS 루프 재생 시작");
                var success = await TtsStreamingService.RestartTts(ttsSection, isTtsEnabled, isBroadcasting);

                if (!success)
                {
                    LoggingService.AddLog("WARN", "TTS 재시작 실패");
                }
            }
            else
            {
                LoggingService.AddLog("INFO", "TTS 재생 종료");
            }

            await InvokeAsync(StateHasChanged);
        }


        [JSInvokable]
        public Task ShowMicHelp()
        {
            DialogService.Open<MicHelpDialog>("마이크 권한 해제 방법",
                new Dictionary<string, object>(),
                new DialogOptions { Width = "600px", Resizable = true });
            return Task.CompletedTask;
        }

        [JSInvokable]
        public async Task OnAudioConfigurationDetected(AudioConfiguration config)
        {
            _currentAudioConfig = config;
            sampleRate = config.SampleRate;

            _logger.LogInformation($"오디오 설정 감지됨 - 샘플레이트: {config.SampleRate}Hz, " +
                                  $"채널: {config.ChannelCount}, 에코제거: {config.EchoCancellation}");

            if (monitoringSection != null)
            {
                await monitoringSection.UpdateAudioConfiguration(config);
            }

            await InvokeAsync(StateHasChanged);
        }

        // 오디오 설정 변경 메서드 통합
        protected async Task ChangeAudioSetting(int? newSampleRate, int? newChannels)
        {
            if (isBroadcasting)
            {
                NotifyWarn("방송 중", "방송 중에는 오디오 설정을 변경할 수 없습니다.");
                return;
            }

            if (newSampleRate.HasValue)
            {
                var supportedSampleRates = sampleRateOptions.Select(o => o.Value).ToArray();
                if (!supportedSampleRates.Contains(newSampleRate.Value))
                {
                    NotifyWarn("지원되지 않는 샘플레이트", $"{newSampleRate.Value}Hz는 지원되지 않는 샘플레이트입니다.");
                    return;
                }
                _preferredSampleRate = newSampleRate.Value;
            }

            if (newChannels.HasValue)
            {
                _preferredChannels = newChannels.Value;
            }

            if (selectedChannel != null)
            {
                await BroadcastDataService.UpdateChannelAudioSettingsAsync(selectedChannel, newSampleRate, newChannels);
            }
            else
            {
                var settingType = newSampleRate.HasValue ? "샘플레이트" : "채널 수";
                var settingValue = newSampleRate ?? newChannels;
                var settingUnit = newSampleRate.HasValue ? "Hz" : "채널";
                NotifyInfo("설정 변경", $"{settingType}가 {settingValue}{settingUnit}로 설정되었습니다. 다음 방송부터 적용됩니다.");
            }
        }

        protected Task ChangeSampleRate(int newSampleRate) => ChangeAudioSetting(newSampleRate, null);
        protected Task ChangeChannels(int newChannels) => ChangeAudioSetting(null, newChannels);

        [JSInvokable]
        public async Task OnMediaPlaylistEnded()
        {
            _logger.LogInformation("Media playlist ended - attempting to restart");

            // 로그에 표시
            LoggingService.AddLog("INFO", "미디어 플레이리스트 재생 완료");

            // 루프 재생 설정 확인 (향후 구현 가능)
            var shouldLoop = true; // 나중에 UI에서 설정 가능하도록

            if (shouldLoop && isMediaEnabled && isBroadcasting)
            {
                LoggingService.AddLog("INFO", "미디어 플레이리스트 루프 재생 시작");
                var success = await MediaStreamingService.RestartPlaylist(selectedChannel, isMediaEnabled, isBroadcasting);

                if (!success)
                {
                    LoggingService.AddLog("WARN", "미디어 플레이리스트 재시작 실패");
                }
            }
            else
            {
                LoggingService.AddLog("INFO", "미디어 플레이리스트 종료");
            }

            await InvokeAsync(StateHasChanged);
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

        protected async Task OnVolumeSaved()
        {
            if (!isBroadcasting && !isTestBroadcasting)
            {
                await LoadInitialData();
                if (selectedChannel != null)
                {
                    selectedChannel = channels.FirstOrDefault(c => c.Id == selectedChannel.Id);

                    if (selectedChannel != null)
                    {
                        var channelSampleRate = (int)(selectedChannel.SamplingRate > 0 ? selectedChannel.SamplingRate : 48000);
                        var supportedSampleRates = sampleRateOptions.Select(o => o.Value).ToArray();
                        _preferredSampleRate = FindClosestSampleRate(channelSampleRate, supportedSampleRates);
                        _preferredChannels = selectedChannel.Channel1 == "mono" ? 1 : 2;
                    }
                }
            }
        }

        protected Task TogglePanel(string panelName)
        {
            switch (panelName)
            {
                case "speakerGroup": speakerGroupPanelCollapsed = !speakerGroupPanelCollapsed; break;
                case "volume": volumePanelCollapsed = !volumePanelCollapsed; break;
                case "tts": ttsPanelCollapsed = !ttsPanelCollapsed; break;
                case "monitoring": monitoringPanelCollapsed = !monitoringPanelCollapsed; break;
            }
            return Task.CompletedTask;
        }
        #endregion

        #region WebSocket Event Handlers
        private void SubscribeToWebSocketEvents()
        {
            WebSocketService.OnBroadcastStatusReceived += OnBroadcastStatusReceived;
            WebSocketService.OnConnectionStatusChanged += OnWebSocketConnectionStatusChanged;
        }

        private void UnsubscribeFromWebSocketEvents()
        {
            if (WebSocketService != null)
            {
                WebSocketService.OnBroadcastStatusReceived -= OnBroadcastStatusReceived;
                WebSocketService.OnConnectionStatusChanged -= OnWebSocketConnectionStatusChanged;
            }
        }

        private void OnBroadcastStatusReceived(string broadcastId, BroadcastStatus status)
        {
            if (broadcastId == currentBroadcastId || broadcastId == testBroadcastId)
            {
                _logger.LogDebug($"Broadcast status update - Packets: {status.PacketCount}, Bytes: {status.TotalBytes}");
            }
        }

        private void OnWebSocketConnectionStatusChanged(string broadcastId, string status)
        {
            if (broadcastId == currentBroadcastId || broadcastId == testBroadcastId)
            {
                switch (status)
                {
                    case "Connected":
                        NotifySuccess("연결 성공", $"채널 {selectedChannel?.Name}의 실시간 방송이 시작되었습니다.");
                        break;
                    case "Disconnected":
                        HandleWebSocketDisconnection(broadcastId);
                        break;
                }
            }
        }

        private void HandleWebSocketDisconnection(string broadcastId)
        {
            NotifyError("연결 끊김", new Exception($"채널 {selectedChannel?.Name}의 방송 연결이 끊어졌습니다."));

            if (isBroadcasting && broadcastId == currentBroadcastId)
            {
                isBroadcasting = false;
                currentBroadcastId = null;
                InvokeAsync(StateHasChanged);
            }

            if (isTestBroadcasting && broadcastId == testBroadcastId)
            {
                isTestBroadcasting = false;
                testBroadcastId = null;
                InvokeAsync(StateHasChanged);
            }
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
            isBroadcasting = false;
            monitoringPanelCollapsed = false;
            _broadcastTimer?.Dispose();
            _broadcastTimer = null;

            // 방송 레코드 업데이트 (ongoing_yn = 'N')
            await UpdateBroadcastRecordsToStopped();

            if (!string.IsNullOrEmpty(currentBroadcastId))
            {
                await WebSocketService.StopBroadcastAsync(currentBroadcastId);
                currentBroadcastId = null;
            }

            await StopEnabledSources();

            _currentOnlineSpeakers = null;
            _currentLoopbackSetting = false;
        }

        // 통합된 소스 정지 메서드
        private async Task StopEnabledSources()
        {
            if (isMicEnabled && _jsModule != null)
            {
                try
                {
                    await _jsModule.InvokeVoidAsync("stop");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to stop microphone module");
                }
            }

            if (isMediaEnabled)
            {
                await MediaStreamingService.StopMediaStreaming();
            }

            if (isTtsEnabled)
            {
                await TtsStreamingService.StopTtsStreaming();
            }
        }

        private async Task SaveSelectedMediaToChannel()
        {
            try
            {
                if (selectedChannel == null) return;

                // playlistSection이 없으면 미디어 매핑을 건너뜀
                if (playlistSection == null)
                {
                    _logger.LogInformation("플레이리스트 섹션이 초기화되지 않았습니다.");
                    LoggingService.AddLog("WARN", "플레이리스트 섹션이 초기화되지 않았습니다.");
                    return;
                }

                // 선택된 미디어 가져오기
                var selectedMedia = playlistSection.GetSelectedMedia();
                var selectedMediaIds = selectedMedia?.Select(m => m.Id).ToHashSet() ?? new HashSet<ulong>();

                LoggingService.AddLog("INFO", $"미디어 매핑 동기화 시작 - 선택된 미디어: {selectedMediaIds.Count}개");

                // 기존 map_channel_media 테이블 데이터 조회
                var query = new Radzen.Query
                {
                    Filter = $"ChannelId eq {selectedChannel.Id}"
                };
                var existingMappings = await WicsService.GetMapChannelMedia(query);
                var existingMediaIds = existingMappings.Value
                    .Where(m => m.DeleteYn != "Y")
                    .Select(m => m.MediaId)
                    .ToHashSet();

                // 1. 신규 추가: 선택됨 BUT DB에 없음
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

                // 2. 소프트 삭제: DB에 있음 BUT 선택 안됨
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

                        // HTTP PATCH 직접 호출
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

                // 3. 유지: 양쪽 모두 있음 (처리 없음)
                var unchanged = selectedMediaIds.Intersect(existingMediaIds);
                if (unchanged.Any())
                {
                    LoggingService.AddLog("INFO", $"변경 없음: {unchanged.Count()}개 미디어 유지");
                }

                // 4. 이미 delete_yn='Y'인 항목 중 다시 선택된 경우 복구
                foreach (var mediaId in selectedMediaIds)
                {
                    var deletedMapping = existingMappings.Value.FirstOrDefault(m => m.MediaId == mediaId && m.DeleteYn == "Y");
                    if (deletedMapping != null)
                    {
                        var updateData = new
                        {
                            DeleteYn = "N",
                            UpdatedAt = DateTime.Now
                        };

                        // HTTP PATCH 직접 호출
                        var response = await Http.PatchAsJsonAsync($"odata/wics/MapChannelMedia(Id={deletedMapping.Id})", updateData);

                        if (response.IsSuccessStatusCode)
                        {
                            var media = selectedMedia.FirstOrDefault(m => m.Id == mediaId);
                            LoggingService.AddLog("SUCCESS", $"미디어 복구: {media?.FileName ?? $"ID {mediaId}"} (delete_yn=N)");
                        }
                        else
                        {
                            LoggingService.AddLog("ERROR", $"미디어 복구 실패: MediaID {mediaId}, Status: {response.StatusCode}");
                        }
                    }
                }

                LoggingService.AddLog("SUCCESS",
                    $"미디어 매핑 동기화 완료 - 추가: {toAdd.Count()}개, 제거: {toDelete.Count()}개, 유지: {unchanged.Count()}개");
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

                // 선택된 TTS 가져오기
                var selectedTts = ttsSection.GetSelectedTts();
                var selectedTtsIds = selectedTts?.Select(t => t.Id).ToHashSet() ?? new HashSet<ulong>();

                LoggingService.AddLog("INFO", $"TTS 매핑 동기화 시작 - 선택된 TTS: {selectedTtsIds.Count}개");

                // 기존 map_channel_tts 테이블 데이터 조회
                var query = new Radzen.Query
                {
                    Filter = $"ChannelId eq {selectedChannel.Id}"
                };
                var existingMappings = await WicsService.GetMapChannelTts(query);
                var existingTtsIds = existingMappings.Value
                    .Where(m => m.DeleteYn != "Y")
                    .Select(m => m.TtsId)
                    .ToHashSet();

                // 1. 신규 추가: 선택됨 BUT DB에 없음
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

                // 2. 소프트 삭제: DB에 있음 BUT 선택 안됨
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
            speakerGroupPanelCollapsed = volumePanelCollapsed =
                playlistPanelCollapsed = ttsPanelCollapsed = monitoringPanelCollapsed = false;
        }

        private void DisposeResources()
        {
            _dotNetRef?.Dispose();
            _broadcastTimer?.Dispose();
            _testDataTimer?.Dispose();
            RecordingService?.Dispose();
        }

        // Group & Speaker Helpers
        protected void ToggleGroupSelection(ulong groupId)
        {
            if (selectedGroups.Contains(groupId))
                selectedGroups.Remove(groupId);
            else
                selectedGroups.Add(groupId);
        }

        protected int GetSpeakerCountInGroup(ulong groupId) =>
            speakerGroupMappings.Where(m => m.GroupId == groupId)
                .Select(m => m.SpeakerId).Distinct().Count();

        protected IEnumerable<string> GetSpeakerGroups(ulong speakerId) =>
            speakerGroupMappings.Where(m => m.SpeakerId == speakerId && m.Group != null)
                .Select(m => m.Group.Name).Distinct();

        protected bool IsSpeakerInSelectedGroups(ulong speakerId) =>
            speakerGroupMappings.Any(m => m.SpeakerId == speakerId && selectedGroups.Contains(m.GroupId));

        protected int GetSelectedSpeakersCount() =>
            speakerGroupMappings.Where(m => selectedGroups.Contains(m.GroupId))
                .Select(m => m.SpeakerId).Distinct().Count();

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

        protected BadgeStyle GetSpeakerStatusBadgeStyle(byte state) =>
            state == 1 ? BadgeStyle.Success :
            state == 0 ? BadgeStyle.Danger : BadgeStyle.Light;

        protected string GetSpeakerStatusText(byte state) =>
            state == 1 ? "온라인" :
            state == 0 ? "오프라인" : "알 수 없음";

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

        #region Broadcast Source Control
        private async Task ToggleBroadcastSource(Func<bool> getSource, Action<bool> setSource, string sourceName)
        {
            if (isBroadcasting)
            {
                NotifyWarn("방송 중", $"방송 중에는 {sourceName} 설정을 변경할 수 없습니다.");
                return;
            }

            var newValue = !getSource();
            setSource(newValue);

            var status = newValue ? "활성화" : "비활성화";
            NotifyInfo($"{sourceName} 설정", $"{sourceName}가 {status}되었습니다.");

            await InvokeAsync(StateHasChanged);
        }

        protected Task ToggleMicEnabled() => ToggleBroadcastSource(() => isMicEnabled, v => isMicEnabled = v, "마이크");
        protected Task ToggleMediaEnabled() => ToggleBroadcastSource(() => isMediaEnabled, v => isMediaEnabled = v, "미디어");
        protected Task ToggleTtsEnabled() => ToggleBroadcastSource(() => isTtsEnabled, v => isTtsEnabled = v, "TTS");
        #endregion
    }
}