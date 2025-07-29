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
        protected bool micDataPanelCollapsed = false;

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

        // 테스트 방송 상태
        protected bool isTestBroadcasting = false;
        protected string testBroadcastId = null;
        private System.Threading.Timer _testDataTimer;
        private Random _testRandom = new Random();

        // 녹음 관련
        protected bool isRecording = false;
        protected List<byte[]> recordedChunks = new List<byte[]>();
        protected DateTime recordingStartTime;
        protected string recordingDuration = "00:00:00";
        protected double recordingDataSize = 0.0;
        private System.Threading.Timer _recordingTimer;

        // JS Interop
        private IJSObjectReference _jsModule;
        private IJSObjectReference _speakerModule;
        private DotNetObjectReference<ManageBroadCast> _dotNetRef;
        protected BroadcastMicDataSection micDataSection;
        private bool _loopbackEnabled = false;

        // Broadcast 관련 추가
        private List<WicsPlatform.Server.Models.wics.Speaker> _currentOnlineSpeakers;
        #endregion

        #region Lifecycle Methods
        protected override async Task OnInitializedAsync()
        {
            SubscribeToWebSocketEvents();
            await LoadAllDataAsync();
        }

        public void Dispose()
        {
            UnsubscribeFromWebSocketEvents();
            DisposeResources();
        }
        #endregion

        #region Data Loading Methods
        private async Task LoadAllDataAsync()
        {
            var tasks = new List<Task>
            {
                LoadChannels(),
                LoadSpeakerGroups(),
                LoadSpeakers(),
                LoadSpeakerGroupMappings()
            };
            await Task.WhenAll(tasks);
        }

        protected async Task LoadChannels()
        {
            await ExecuteWithLoading(
                async () =>
                {
                    var query = new Radzen.Query
                    {
                        Filter = "DeleteYn eq 'N' or DeleteYn eq null",
                        OrderBy = "CreatedAt desc"
                    };
                    var result = await WicsService.GetChannels(query);
                    channels = result.Value.AsODataEnumerable();
                },
                loading => isLoading = loading,
                "채널 목록"
            );
        }

        protected async Task LoadSpeakerGroups()
        {
            await ExecuteWithLoading(
                async () =>
                {
                    var result = await WicsService.GetGroups(new Radzen.Query
                    {
                        Filter = "DeleteYn eq 'N' or DeleteYn eq null"
                    });
                    speakerGroups = result.Value.AsODataEnumerable();
                },
                loading => isLoadingGroups = loading,
                "스피커 그룹 목록"
            );
        }

        protected async Task LoadSpeakers()
        {
            await ExecuteWithLoading(
                async () =>
                {
                    var q = new Radzen.Query
                    {
                        Filter = "DeleteYn eq 'N' or DeleteYn eq null",
                        Expand = "Channel"
                    };
                    var result = await WicsService.GetSpeakers(q);
                    allSpeakers = result.Value.AsODataEnumerable();
                },
                loading => isLoadingSpeakers = loading,
                "스피커 목록"
            );
        }

        protected async Task LoadSpeakerGroupMappings()
        {
            await ExecuteWithLoading(
                async () =>
                {
                    var result = await WicsService.GetMapSpeakerGroups(new Radzen.Query
                    {
                        Expand = "Group,Speaker",
                        Filter = "LastYn eq 'Y'"
                    });
                    speakerGroupMappings = result.Value.AsODataEnumerable();
                },
                null,
                "매핑 정보"
            );
        }
        #endregion

        #region Channel Management
        protected async Task CreateChannel()
        {
            if (string.IsNullOrWhiteSpace(newChannelName))
            {
                NotifyWarn("입력 필요", "채널명을 입력해주세요.");
                return;
            }

            try
            {
                var newChannel = CreateNewChannelObject(newChannelName.Trim());
                await WicsService.CreateChannel(newChannel);
                NotifySuccess("생성 완료", $"'{newChannelName}' 채널이 생성되었습니다.");
                newChannelName = "";
                await LoadChannels();
            }
            catch (Exception ex)
            {
                NotifyError("채널 생성", ex);
            }
        }

        protected async Task SelectChannel(WicsPlatform.Server.Models.wics.Channel channel)
        {
            selectedChannel = channel;
            selectedGroups.Clear();
            ResetAllPanels();
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

        #region Broadcast Control
        protected async Task StartBroadcast()
        {
            Console.WriteLine("StartBroadcast 메서드 호출됨");

            if (!ValidateBroadcastPrerequisites()) return;

            try
            {
                var (onlineSpeakers, offlineSpeakers) = GetSpeakersByStatus();

                if (!await ValidateAndNotifyOnlineStatus(onlineSpeakers, offlineSpeakers))
                    return;

                // 온라인 스피커 목록 저장
                _currentOnlineSpeakers = onlineSpeakers;

                var onlineGroups = GetOnlineGroups(onlineSpeakers);

                if (!await InitializeWebSocketBroadcast(onlineGroups))
                    return;

                if (!await InitializeAudioModules())
                    return;

                if (!await StartMicrophoneRecording())
                {
                    await CleanupFailedBroadcast();
                    return;
                }

                InitializeBroadcastState();

                // Broadcast 테이블에 데이터 삽입
                await CreateBroadcastRecords(onlineSpeakers);

                NotifyBroadcastStarted(onlineSpeakers, offlineSpeakers);
                await InvokeAsync(StateHasChanged);
            }
            catch (Exception ex)
            {
                await HandleBroadcastError(ex);
            }
        }

        private async Task CreateBroadcastRecords(List<WicsPlatform.Server.Models.wics.Speaker> onlineSpeakers)
        {
            try
            {
                foreach (var speaker in onlineSpeakers)
                {
                    var broadcast = new WicsPlatform.Server.Models.wics.Broadcast
                    {
                        ChannelId = selectedChannel.Id,
                        SpeakerId = speaker.Id,
                        MediaId = 1, // 우선 고정값
                        OngoingYn = "Y",
                        CreatedAt = DateTime.Now,
                        UpdatedAt = DateTime.Now
                    };

                    await WicsService.CreateBroadcast(broadcast);
                    _logger.LogInformation($"Broadcast record created for channel {selectedChannel.Id}, speaker {speaker.Id}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create broadcast records");
                NotifyWarn("기록 생성 실패", "방송 기록 생성 중 오류가 발생했습니다. 방송은 계속됩니다.");
            }
        }

        private async Task UpdateBroadcastRecordsToStopped()
        {
            try
            {
                if (_currentOnlineSpeakers == null || !_currentOnlineSpeakers.Any())
                    return;

                // 현재 진행 중인 방송 레코드 조회
                var query = new Radzen.Query
                {
                    Filter = $"ChannelId eq {selectedChannel.Id} and OngoingYn eq 'Y'",
                    OrderBy = "CreatedAt desc"
                };

                var broadcasts = await WicsService.GetBroadcasts(query);

                foreach (var broadcast in broadcasts.Value)
                {
                    // 현재 방송 중인 스피커인 경우만 업데이트
                    if (_currentOnlineSpeakers.Any(s => s.Id == broadcast.SpeakerId))
                    {
                        broadcast.OngoingYn = "N";
                        broadcast.UpdatedAt = DateTime.Now;
                        await WicsService.UpdateBroadcast(broadcast.Id, broadcast);
                        _logger.LogInformation($"Broadcast record updated to stopped for channel {broadcast.ChannelId}, speaker {broadcast.SpeakerId}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update broadcast records to stopped");
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

            if (_loopbackEnabled && _speakerModule == null)
            {
                _speakerModule = await JSRuntime.InvokeAsync<IJSObjectReference>("import", "./js/speaker.js");
                await _speakerModule.InvokeVoidAsync("init");
            }

            return true;
        }

        private async Task<bool> StartMicrophoneRecording()
        {
            const int MicTimesliceMs = 50;
            var ok = await _jsModule.InvokeAsync<bool>("start", _dotNetRef, new { timeslice = MicTimesliceMs });

            if (!ok)
            {
                NotifyWarn("권한 필요", "마이크 권한을 허용하셔야 방송을 시작할 수 있습니다.");
                return false;
            }

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
            if (selectedChannel == null)
            {
                NotifyWarn("채널 선택", "먼저 방송할 채널을 선택하세요.");
                return;
            }

            if (selectedGroups == null || !selectedGroups.Any())
            {
                NotifyWarn("스피커 그룹 선택", "방송할 스피커 그룹을 선택하세요.");
                return;
            }

            try
            {
                var selectedSpeakers = speakerGroupMappings
                    .Where(m => selectedGroups.Contains(m.GroupId))
                    .Select(m => m.SpeakerId)
                    .Distinct()
                    .ToList();

                var onlineSpeakers = allSpeakers
                    .Where(s => selectedSpeakers.Contains(s.Id) && s.State == 1)
                    .ToList();

                if (!onlineSpeakers.Any())
                {
                    NotifyError("방송 불가", new Exception("온라인 상태인 스피커가 없습니다."));
                    return;
                }

                var onlineSpeakerIds = onlineSpeakers.Select(s => s.Id).ToList();
                var onlineGroups = speakerGroupMappings
                    .Where(m => onlineSpeakerIds.Contains(m.SpeakerId) && selectedGroups.Contains(m.GroupId))
                    .Select(m => m.GroupId)
                    .Distinct()
                    .ToList();

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
                var dataSize = _testRandom.Next(1024, 4096);
                var randomData = new byte[dataSize];
                _testRandom.NextBytes(randomData);

                await WebSocketService.SendAudioDataAsync(testBroadcastId, randomData);

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
            if (!isBroadcasting)
            {
                NotifyWarn("방송 필요", "방송이 시작된 상태에서만 녹음할 수 있습니다.");
                return;
            }

            try
            {
                InitializeRecordingState();
                NotifySuccess("녹음 시작", "방송 내용 녹음을 시작합니다.");
                await InvokeAsync(StateHasChanged);
            }
            catch (Exception ex)
            {
                isRecording = false;
                NotifyError("녹음 시작", ex);
            }
        }

        protected async Task StopRecording()
        {
            if (!isRecording) return;

            try
            {
                StopRecordingTimer();

                if (recordedChunks.Any())
                {
                    var combinedData = CombineRecordedChunks();
                    await SaveRecordingToFile(combinedData);
                }
                else
                {
                    NotifyWarn("녹음 없음", "저장할 녹음 데이터가 없습니다.");
                }

                recordedChunks.Clear();
                await InvokeAsync(StateHasChanged);
            }
            catch (Exception ex)
            {
                NotifyError("녹음 저장", ex);
            }
        }

        private void InitializeRecordingState()
        {
            isRecording = true;
            recordedChunks.Clear();
            recordingStartTime = DateTime.Now;
            recordingDataSize = 0.0;

            _recordingTimer = new System.Threading.Timer(
                UpdateRecordingTime,
                null,
                TimeSpan.Zero,
                TimeSpan.FromSeconds(1));
        }

        private void StopRecordingTimer()
        {
            isRecording = false;
            _recordingTimer?.Dispose();
            _recordingTimer = null;
        }

        private byte[] CombineRecordedChunks()
        {
            var totalSize = recordedChunks.Sum(chunk => chunk.Length);
            var combinedData = new byte[totalSize];
            var offset = 0;

            foreach (var chunk in recordedChunks)
            {
                Buffer.BlockCopy(chunk, 0, combinedData, offset, chunk.Length);
                offset += chunk.Length;
            }

            return combinedData;
        }

        private async Task SaveRecordingToFile(byte[] data)
        {
            var base64Data = Convert.ToBase64String(data);
            var fileName = $"recording_{DateTime.Now:yyyyMMdd_HHmmss}.webm";
            await JSRuntime.InvokeVoidAsync("downloadBase64File", base64Data, fileName, "audio/webm");
            NotifySuccess("녹음 완료", $"녹음이 완료되어 '{fileName}' 파일로 저장되었습니다.");
        }
        #endregion

        #region Audio Processing
        [JSInvokable]
        public async Task OnAudioCaptured(string base64Data)
        {
            if (string.IsNullOrWhiteSpace(base64Data)) return;

            try
            {
                byte[] data = Convert.FromBase64String(base64Data);

                UpdateAudioStatistics(data);

                if (isRecording)
                    recordedChunks.Add(data);

                if (micDataSection != null)
                    await micDataSection.OnAudioCaptured(data);

                if (!string.IsNullOrEmpty(currentBroadcastId))
                    await WebSocketService.SendAudioDataAsync(currentBroadcastId, data);

                if (_loopbackEnabled && _speakerModule != null)
                    await _speakerModule.InvokeVoidAsync("feed", base64Data);

                await InvokeAsync(StateHasChanged);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"OnAudioCaptured – 오류: {ex.Message}");
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

        [JSInvokable]
        public Task ShowMicHelp()
        {
            DialogService.Open<MicHelpDialog>("마이크 권한 해제 방법",
                new Dictionary<string, object>(),
                new DialogOptions { Width = "600px", Resizable = true });
            return Task.CompletedTask;
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
            await LoadChannels();
            if (selectedChannel != null)
                selectedChannel = channels.FirstOrDefault(c => c.Id == selectedChannel.Id);
        }

        protected Task TogglePanel(string panelName)
        {
            switch (panelName)
            {
                case "speakerGroup": speakerGroupPanelCollapsed = !speakerGroupPanelCollapsed; break;
                case "volume": volumePanelCollapsed = !volumePanelCollapsed; break;
                case "tts": ttsPanelCollapsed = !ttsPanelCollapsed; break;
                case "micData": micDataPanelCollapsed = !micDataPanelCollapsed; break;
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

            if (micDataSection != null)
            {
                await micDataSection.UpdateBroadcastData(
                    audioLevel, broadcastStartTime, broadcastDuration,
                    totalDataPackets, totalDataSize, averageBitrate, sampleRate);
            }

            await InvokeAsync(StateHasChanged);
        }

        private async void UpdateRecordingTime(object state)
        {
            if (!isRecording) return;

            var elapsed = DateTime.Now - recordingStartTime;
            recordingDuration = elapsed.ToString(@"hh\:mm\:ss");
            recordingDataSize = recordedChunks.Sum(chunk => chunk.Length) / 1024.0 / 1024.0;

            await InvokeAsync(StateHasChanged);
        }
        #endregion

        #region Helper Methods
        private async Task ExecuteWithLoading(Func<Task> action, Action<bool> setLoading, string errorContext)
        {
            try
            {
                setLoading?.Invoke(true);
                await action();
            }
            catch (Exception ex)
            {
                NotifyError(errorContext, ex);
            }
            finally
            {
                setLoading?.Invoke(false);
            }
        }

        private WicsPlatform.Server.Models.wics.Channel CreateNewChannelObject(string name)
        {
            return new WicsPlatform.Server.Models.wics.Channel
            {
                Name = name,
                Type = 0,
                State = 0,
                MicVolume = 0.5f,
                TtsVolume = 0.5f,
                MediaVolume = 0.5f,
                Volume = 0.5f,
                Description = "",
                DeleteYn = "N",
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now
            };
        }

        private async Task HandleBroadcastStopped()
        {
            isBroadcasting = false;
            micDataPanelCollapsed = false;
            _broadcastTimer?.Dispose();
            _broadcastTimer = null;

            // Broadcast 레코드 업데이트
            await UpdateBroadcastRecordsToStopped();

            if (!string.IsNullOrEmpty(currentBroadcastId))
            {
                await WebSocketService.StopBroadcastAsync(currentBroadcastId);
                currentBroadcastId = null;
            }

            if (isRecording)
            {
                await StopRecording();
            }

            // 온라인 스피커 목록 초기화
            _currentOnlineSpeakers = null;
        }

        private void LogBroadcastStatistics(BroadcastStoppedEventArgs args)
        {
            Console.WriteLine($"방송 종료 - 시간: {args.Duration}, 패킷: {args.TotalPackets}, 데이터: {args.TotalDataSize}KB");
        }

        private void ResetAllPanels()
        {
            speakerGroupPanelCollapsed = volumePanelCollapsed =
                playlistPanelCollapsed = ttsPanelCollapsed = micDataPanelCollapsed = false;
        }

        private void DisposeResources()
        {
            _dotNetRef?.Dispose();
            _broadcastTimer?.Dispose();
            _recordingTimer?.Dispose();
            _testDataTimer?.Dispose();
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
        private void NotifySuccess(string summary, string detail) =>
            NotificationService.Notify(NotificationSeverity.Success, summary, detail, 4000);

        private void NotifyError(string summary, Exception ex) =>
            NotificationService.Notify(NotificationSeverity.Error, "오류", $"{summary} 중 오류: {ex.Message}", 4000);

        private void NotifyWarn(string summary, string detail) =>
            NotificationService.Notify(NotificationSeverity.Warning, summary, detail, 4000);

        private void NotifyInfo(string summary, string detail) =>
            NotificationService.Notify(NotificationSeverity.Info, summary, detail, 4000);
        #endregion
    }
}