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

        // ✅ 방송 소스 선택 체크박스 (새로 추가)
        protected bool isMicEnabled = true;     // 마이크 - 기본값 true (기존 동작 유지)
        protected bool isMediaEnabled = false;  // 미디어
        protected bool isTtsEnabled = false;    // TTS

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

        // Broadcast 관련 추가
        private List<WicsPlatform.Server.Models.wics.Speaker> _currentOnlineSpeakers;
        
        // 루프백 설정 - 이제 Broadcast 테이블 기반으로 관리
        private bool _currentLoopbackSetting = false;
        #endregion

        #region Audio Configuration
// 오디오 설정 클래스 추가
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

// 오디오 설정 필드 추가
private AudioConfiguration _currentAudioConfig = new AudioConfiguration();
private int _preferredSampleRate = 48000; // 기본값을 48000Hz로 변경
private int _preferredChannels = 2;
private int _preferredBitrate = 64000;

// 샘플레이트 옵션
private List<SampleRateOption> sampleRateOptions = new List<SampleRateOption>
{
    new SampleRateOption { Value = 8000, Text = "8000 Hz (전화품질)" },
    new SampleRateOption { Value = 16000, Text = "16000 Hz (광대역)" },
    new SampleRateOption { Value = 24000, Text = "24000 Hz (고품질)" },
    new SampleRateOption { Value = 48000, Text = "48000 Hz (프로페셔널)" }
};

// 채널 옵션
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
            
            // 선택된 채널의 오디오 설정으로 UI 업데이트
            if (channel != null)
            {
                var channelSampleRate = (int)(channel.SamplingRate > 0 ? channel.SamplingRate : 48000);
                
                // 지원되는 샘플레이트 목록 중에서 가장 가까운 값 찾기
                var supportedSampleRates = sampleRateOptions.Select(o => o.Value).ToArray();
                _preferredSampleRate = FindClosestSampleRate(channelSampleRate, supportedSampleRates);
                
                // 채널에 저장된 값과 다르면 업데이트
                if (_preferredSampleRate != channelSampleRate && channel.SamplingRate > 0)
                {
                    try
                    {
                        channel.SamplingRate = (uint)_preferredSampleRate;
                        channel.UpdatedAt = DateTime.Now;
                        await WicsService.UpdateChannel(channel.Id, channel);
                        
                        _logger.LogInformation($"Channel {channel.Id} sampling rate auto-adjusted from {channelSampleRate}Hz to {_preferredSampleRate}Hz");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Failed to update channel {channel.Id} sampling rate");
                    }
                }
                
                _preferredChannels = channel.Channel1 == "mono" ? 1 : 2;
                
                _logger.LogInformation($"Channel selected: {channel.Name}, SamplingRate: {_preferredSampleRate}Hz, Channels: {_preferredChannels}");
            }
            
            await InvokeAsync(StateHasChanged);
        }

        // 지원되는 샘플레이트 중에서 가장 가까운 값을 찾는 메서드
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

                // ✅ 마이크가 활성화된 경우에만 마이크 녹음 시작
                if (isMicEnabled)
                {
                    if (!await StartMicrophoneRecording())
                    {
                        await CleanupFailedBroadcast();
                        return;
                    }
                }

                // ✅ 미디어가 활성화된 경우 미디어 스트리밍 시작
                if (isMediaEnabled)
                {
                    if (!await StartMediaStreaming())
                    {
                        await CleanupFailedBroadcast();
                        return;
                    }
                }

                // ✅ TTS가 활성화된 경우 TTS 스트리밍 시작 (향후 구현)
                if (isTtsEnabled)
                {
                    // TODO: StartTtsStreaming() 구현
                    _logger.LogInformation("TTS streaming will be implemented in future updates");
                }

                InitializeBroadcastState();

                // Broadcast 테이블에 데이터 삽입 (LoopbackYn 포함)
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
                // 선택된 미디어와 TTS ID 가져오기
                var selectedMediaIds = await GetSelectedMediaIds();
                var selectedTtsIds = await GetSelectedTtsIds();
                
                // ✅ 띄어쓰기 구분 문자열로 단일 레코드 생성
                var broadcast = new WicsPlatform.Server.Models.wics.Broadcast
                {
                    ChannelId = selectedChannel.Id,
                    
                    // 편의 프로퍼티 사용 (내부적으로 띄어쓰기 구분 문자열로 변환됨)
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

        // ✅ 현재 채널에 설정된 미디어 ID 가져오기
        private async Task<List<ulong>> GetSelectedMediaIds()
        {
            try
            {
                if (selectedChannel == null) return new List<ulong>();
                
                // MapChannelMedium에서 현재 채널에 연결된 미디어 가져오기
                var query = new Radzen.Query
                {
                    Filter = $"ChannelId eq {selectedChannel.Id} and (DeleteYn eq 'N' or DeleteYn eq null)",
                    Expand = "Medium"
                };
                
                var channelMedia = await WicsService.GetMapChannelMedia(query);
                return channelMedia.Value.Select(m => m.MediaId).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get selected media IDs");
                return new List<ulong>();
            }
        }

        // ✅ 현재 채널에 설정된 TTS ID 가져오기
        private async Task<List<ulong>> GetSelectedTtsIds()
        {
            try
            {
                if (selectedChannel == null) return new List<ulong>();
                
                // MapChannelTt에서 현재 채널에 연결된 TTS 가져오기
                var query = new Radzen.Query
                {
                    Filter = $"ChannelId eq {selectedChannel.Id} and (DeleteYn eq 'N' or DeleteYn eq null)",
                    Expand = "Tt"
                };
                
                var channelTts = await WicsService.GetMapChannelTts(query);
                return channelTts.Value.Select(t => t.TtsId).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get selected TTS IDs");
                return new List<ulong>();
            }
        }
        private async Task UpdateBroadcastRecordsToStopped()
        {
            try
            {
                if (_currentOnlineSpeakers == null || !_currentOnlineSpeakers.Any())
                    return;

                // ✅ 현재 진행 중인 방송 레코드 조회 (띄어쓰기 구분 문자열 방식)
                var query = new Radzen.Query
                {
                    Filter = $"ChannelId eq {selectedChannel.Id} and OngoingYn eq 'Y'",
                    OrderBy = "CreatedAt desc"
                };

                var broadcasts = await WicsService.GetBroadcasts(query);

                foreach (var broadcast in broadcasts.Value)
                {
                    // ✅ 띄어쓰기 구분 문자열에서 스피커 ID 확인
                    var broadcastSpeakerIds = broadcast.SpeakerIds; // 자동으로 파싱됨
                    var currentSpeakerIds = _currentOnlineSpeakers.Select(s => s.Id).ToList();
                    
                    // 현재 방송 중인 스피커가 포함된 경우만 업데이트
                    if (broadcastSpeakerIds.Any(id => currentSpeakerIds.Contains(id)))
                    {
                        broadcast.OngoingYn = "N";
                        broadcast.UpdatedAt = DateTime.Now;
                        await WicsService.UpdateBroadcast(broadcast.Id, broadcast);
                        
                        _logger.LogInformation($"Broadcast record updated to stopped for channel {broadcast.ChannelId}, " +
                                              $"speaker_id_list: '{broadcast.SpeakerIdList}'");
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

            // ✅ 최소 하나의 방송 소스가 활성화되어야 함
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

            // 루프백이 활성화된 경우에만 스피커 모듈 초기화
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
            
            // 선택된 채널의 샘플링 레이트를 우선 사용, 없으면 사용자 설정값 사용
            var channelSampleRate = selectedChannel?.SamplingRate > 0 ? (int)selectedChannel.SamplingRate : _preferredSampleRate;
            var channelChannels = selectedChannel?.Channel1 == "mono" ? 1 : _preferredChannels;
            
            // ✅ 채널 설정 또는 사용자 정의 오디오 설정으로 마이크 시작
            var micConfig = new
            {
                timeslice = MicTimesliceMs,
                sampleRate = channelSampleRate,      // 채널 또는 사용자가 원하는 샘플레이트
                channels = channelChannels,          // 채널 또는 사용자가 원하는 채널 수
                bitrate = _preferredBitrate,         // 사용자가 원하는 비트레이트
                echoCancellation = true,             // 에코 제거
                noiseSuppression = true,             // 노이즈 제거
                autoGainControl = true               // 자동 게인 조절
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
            // ✅ 활성화된 방송 소스를 알림 메시지에 포함
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
        // 루프백 설정을 위한 메서드 추가
        private async Task<bool> GetLoopbackSetting()
        {
            try
            {
                // ✅ 현재 진행 중인 방송에서 루프백 설정 확인 (띄어쓰기 구분 문자열 방식)
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
                return false; // 기본값
            }
        }

        // 루프백 설정 토글 메서드 추가 (UI에서 호출 가능)
        protected async Task ToggleLoopback()
        {
            try
            {
                if (!isBroadcasting)
                {
                    NotifyWarn("방송 필요", "방송 중일 때만 루프백 설정을 변경할 수 있습니다.");
                    return;
                }

                // ✅ 현재 진행 중인 방송 레코드들 조회 (띄어쓰기 구분 문자열 방식)
                var query = new Radzen.Query
                {
                    Filter = $"ChannelId eq {selectedChannel.Id} and OngoingYn eq 'Y'"
                };

                var broadcasts = await WicsService.GetBroadcasts(query);
                var currentLoopback = broadcasts.Value.FirstOrDefault()?.LoopbackYn == "Y";
                var newLoopback = !currentLoopback;

                // ✅ 모든 진행 중인 방송 레코드의 루프백 설정 업데이트
                foreach (var broadcast in broadcasts.Value)
                {
                    broadcast.LoopbackYn = newLoopback ? "Y" : "N";
                    broadcast.UpdatedAt = DateTime.Now;
                    await WicsService.UpdateBroadcast(broadcast.Id, broadcast);
                }

                _currentLoopbackSetting = newLoopback;

                // 루프백 모듈 초기화/해제
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
            // ✅ 마이크가 비활성화된 경우 오디오 처리 중단
            if (!isMicEnabled || string.IsNullOrWhiteSpace(base64Data)) return;

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

                // 루프백 설정을 동적으로 확인하여 스피커로 피드
                if (_currentLoopbackSetting && _speakerModule != null)
                    await _speakerModule.InvokeVoidAsync("feed", base64Data);

                // StateHasChanged 호출 최적화 - 10번의 오디오 패킷마다 한 번씩만 UI 업데이트
                if (totalDataPackets % 10 == 0)
                {
                    await InvokeAsync(StateHasChanged);
                }
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

        [JSInvokable]
        public async Task OnAudioConfigurationDetected(AudioConfiguration config)
        {
            _currentAudioConfig = config;
            
            // ✅ 실제 감지된 샘플레이트로 업데이트
            sampleRate = config.SampleRate;
            
            _logger.LogInformation($"오디오 설정 감지됨 - 샘플레이트: {config.SampleRate}Hz, " +
                                  $"채널: {config.ChannelCount}, 에코제거: {config.EchoCancellation}");
            
            // MicDataSection에도 실제 설정 전달
            if (micDataSection != null)
            {
                await micDataSection.UpdateAudioConfiguration(config);
            }
            
            await InvokeAsync(StateHasChanged);
        }

        // 샘플레이트 변경 메서드 (UI에서 호출 가능)
        protected async Task ChangeSampleRate(int newSampleRate)
        {
            if (isBroadcasting)
            {
                NotifyWarn("방송 중", "방송 중에는 오디오 설정을 변경할 수 없습니다.");
                return;
            }
            
            // 지원되는 샘플레이트인지 확인
            var supportedSampleRates = sampleRateOptions.Select(o => o.Value).ToArray();
            if (!supportedSampleRates.Contains(newSampleRate))
            {
                NotifyWarn("지원되지 않는 샘플레이트", $"{newSampleRate}Hz는 지원되지 않는 샘플레이트입니다.");
                return;
            }
            
            _preferredSampleRate = newSampleRate;
            
            // 선택된 채널이 있으면 채널의 SamplingRate도 업데이트
            if (selectedChannel != null)
            {
                try
                {
                    selectedChannel.SamplingRate = (uint)newSampleRate;
                    selectedChannel.UpdatedAt = DateTime.Now;
                    await WicsService.UpdateChannel(selectedChannel.Id, selectedChannel);
                    
                    NotifySuccess("설정 저장", 
                        $"채널 '{selectedChannel.Name}'의 샘플레이트가 {newSampleRate}Hz로 저장되었습니다.");
                    
                    _logger.LogInformation($"Channel {selectedChannel.Id} sampling rate updated to {newSampleRate}Hz");
                }
                catch (Exception ex)
                {
                    NotifyError("설정 저장", ex);
                    _logger.LogError(ex, $"Failed to update channel {selectedChannel.Id} sampling rate");
                }
            }
            else
            {
                NotifyInfo("설정 변경", $"샘플레이트가 {newSampleRate}Hz로 설정되었습니다. 다음 방송부터 적용됩니다.");
            }
        }

        // 채널 수 변경 메서드
        protected async Task ChangeChannels(int newChannels)
        {
            if (isBroadcasting)
            {
                NotifyWarn("방송 중", "방송 중에는 오디오 설정을 변경할 수 없습니다.");
                return;
            }
            
            _preferredChannels = newChannels;
            
            // 선택된 채널이 있으면 채널의 Channel1 필드도 업데이트
            if (selectedChannel != null)
            {
                try
                {
                    selectedChannel.Channel1 = newChannels == 1 ? "mono" : "stereo";
                    selectedChannel.UpdatedAt = DateTime.Now;
                    await WicsService.UpdateChannel(selectedChannel.Id, selectedChannel);
                    
                    NotifySuccess("설정 저장", 
                        $"채널 '{selectedChannel.Name}'의 채널 수가 {newChannels}채널로 저장되었습니다.");
                    
                    _logger.LogInformation($"Channel {selectedChannel.Id} channels updated to {newChannels}");
                }
                catch (Exception ex)
                {
                    NotifyError("설정 저장", ex);
                    _logger.LogError(ex, $"Failed to update channel {selectedChannel.Id} channels");
                }
            }
            else
            {
                NotifyInfo("설정 변경", $"채널 수가 {newChannels}채널로 설정되었습니다. 다음 방송부터 적용됩니다.");
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

        protected async Task OnVolumeSaved()
        {
            // ★ 방송 중이 아닐 때만 채널 목록을 다시 로드
            // 방송 중에는 현재 선택된 채널 객체를 유지하여 UI 상태 보존
            if (!isBroadcasting && !isTestBroadcasting)
            {
                await LoadChannels();
                if (selectedChannel != null)
                {
                    selectedChannel = channels.FirstOrDefault(c => c.Id == selectedChannel.Id);
                    
                    // 채널 설정이 업데이트된 경우 UI에 반영
                    if (selectedChannel != null)
                    {
                        var channelSampleRate = (int)(selectedChannel.SamplingRate > 0 ? selectedChannel.SamplingRate : 48000);
                        var supportedSampleRates = sampleRateOptions.Select(o => o.Value).ToArray();
                        _preferredSampleRate = FindClosestSampleRate(channelSampleRate, supportedSampleRates);
                        _preferredChannels = selectedChannel.Channel1 == "mono" ? 1 : 2;
                    }
                }
            }
            // 방송 중일 때는 로컬 채널 객체가 이미 BroadcastVolumeSection에서 업데이트되었으므로
            // 추가적인 로드가 필요하지 않음
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

            // StateHasChanged 호출 빈도를 줄여서 화면 깜빡임 방지
            // 방송 시간이 초 단위로 변경될 때만 UI 업데이트
            try
            {
                await InvokeAsync(StateHasChanged);
            }
            catch (ObjectDisposedException)
            {
                // 컴포넌트가 이미 disposed된 경우 무시
                return;
            }
        }

        private async void UpdateRecordingTime(object state)
        {
            if (!isRecording) return;

            var elapsed = DateTime.Now - recordingStartTime;
            recordingDuration = elapsed.ToString(@"hh\:mm\:ss");
            recordingDataSize = recordedChunks.Sum(chunk => chunk.Length) / 1024.0 / 1024.0;

            try
            {
                await InvokeAsync(StateHasChanged);
            }
            catch (ObjectDisposedException)
            {
                // 컴포넌트가 이미 disposed된 경우 무시
                return;
            }
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
                SamplingRate = 48000, // 기본 샘플링 레이트를 48000Hz로 설정
                AudioMethod = 0,
                Codec = "webm",
                Channel1 = "stereo",
                Bit = 16,
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

            // ✅ 마이크가 활성화된 경우에만 마이크 모듈 정지
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

            // ✅ 미디어가 활성화된 경우 미디어 스트리밍 정지
            if (isMediaEnabled)
            {
                await StopMediaStreaming();
            }

            // ✅ TTS가 활성화된 경우 TTS 스트리밍 정지 (향후 구현)
            if (isTtsEnabled)
            {
                // TODO: StopTtsStreaming() 구현
                _logger.LogInformation("TTS streaming stop will be implemented in future updates");
            }

            // онлайн спикеры 목록 및 루프백 설정 초기화
            _currentOnlineSpeakers = null;
            _currentLoopbackSetting = false;
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
            
            // ✅ 미디어 스트리머 모듈 정리
            _mediaStreamerModule?.DisposeAsync();
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

        #region Broadcast Source Control (새로 추가)
        /// <summary>
        /// 마이크 활성화/비활성화 토글
        /// </summary>
        protected async Task ToggleMicEnabled()
        {
            if (isBroadcasting)
            {
                NotifyWarn("방송 중", "방송 중에는 마이크 설정을 변경할 수 없습니다.");
                return;
            }

            isMicEnabled = !isMicEnabled;
            
            var status = isMicEnabled ? "활성화" : "비활성화";
            NotifyInfo("마이크 설정", $"마이크가 {status}되었습니다.");
            
            await InvokeAsync(StateHasChanged);
        }

        /// <summary>
        /// 미디어 활성화/비활성화 토글
        /// </summary>
        protected async Task ToggleMediaEnabled()
        {
            if (isBroadcasting)
            {
                NotifyWarn("방송 중", "방송 중에는 미디어 설정을 변경할 수 없습니다.");
                return;
            }

            isMediaEnabled = !isMediaEnabled;
            
            var status = isMediaEnabled ? "활성화" : "비활성화";
            NotifyInfo("미디어 설정", $"미디어가 {status}되었습니다.");
            
            await InvokeAsync(StateHasChanged);
        }

        /// <summary>
        /// TTS 활성화/비활성화 토글
        /// </summary>
        protected async Task ToggleTtsEnabled()
        {
            if (isBroadcasting)
            {
                NotifyWarn("방송 중", "방송 중에는 TTS 설정을 변경할 수 없습니다.");
                return;
            }

            isTtsEnabled = !isTtsEnabled;
            
            var status = isTtsEnabled ? "활성화" : "비활성화";
            NotifyInfo("TTS 설정", $"TTS가 {status}되었습니다.");
            
            await InvokeAsync(StateHasChanged);
        }
        #endregion

        #region Media Streaming Control (새로 추가)
        
        // ✅ 미디어 스트리밍 관련 필드
        private IJSObjectReference _mediaStreamerModule;
        private bool _isMediaStreaming = false;
        private List<string> _currentMediaPlaylist = new List<string>();

        /// <summary>
        /// 미디어 스트리밍 시작
        /// </summary>
        private async Task<bool> StartMediaStreaming()
        {
            if (!isMediaEnabled)
            {
                _logger.LogInformation("Media is disabled, skipping media streaming");
                return true; // 미디어가 비활성화된 경우 성공으로 처리
            }

            try
            {
                // 미디어 스트리머 모듈 로드
                if (_mediaStreamerModule == null)
                {
                    _mediaStreamerModule = await JSRuntime.InvokeAsync<IJSObjectReference>("import", "./js/mediastreamer.js");
                }

                // 오디오 설정 구성
                var channelSampleRate = selectedChannel?.SamplingRate > 0 ? (int)selectedChannel.SamplingRate : _preferredSampleRate;
                var channelChannels = selectedChannel?.Channel1 == "mono" ? 1 : _preferredChannels;

                var mediaConfig = new
                {
                    sampleRate = channelSampleRate,
                    channels = channelChannels,
                    timeslice = 50 // 50ms 간격
                };

                // 미디어 스트리머 초기화
                var initialized = await _mediaStreamerModule.InvokeAsync<bool>("initializeMediaStreamer", _dotNetRef, mediaConfig);
                if (!initialized)
                {
                    _logger.LogError("Failed to initialize media streamer");
                    return false;
                }

                // 선택된 미디어 파일 URL 목록 가져오기
                var mediaUrls = await GetMediaPlaylistUrls();
                if (!mediaUrls.Any())
                {
                    _logger.LogInformation("No media files selected for streaming");
                    return true; // 미디어가 없는 경우 성공으로 처리
                }

                // 플레이리스트 로드 및 스트리밍 시작
                var started = await _mediaStreamerModule.InvokeAsync<bool>("loadAndStreamMediaPlaylist", mediaUrls.ToArray());
                if (started)
                {
                    _isMediaStreaming = true;
                    _currentMediaPlaylist = mediaUrls.ToList();
                    _logger.LogInformation($"Media streaming started with {mediaUrls.Count} files");
                    return true;
                }
                else
                {
                    _logger.LogError("Failed to start media streaming");
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error starting media streaming");
                return false;
            }
        }

        /// <summary>
        /// 선택된 미디어 파일들의 URL 목록 가져오기
        /// </summary>
        private async Task<List<string>> GetMediaPlaylistUrls()
        {
            try
            {
                var selectedMediaIds = await GetSelectedMediaIds();
                if (!selectedMediaIds.Any())
                {
                    return new List<string>();
                }

                var query = new Radzen.Query
                {
                    Filter = $"Id in ({string.Join(",", selectedMediaIds)}) and (DeleteYn eq 'N' or DeleteYn eq null)",
                    OrderBy = "CreatedAt asc"
                };

                var result = await WicsService.GetMedia(query);
                var mediaFiles = result.Value.ToList();

                return mediaFiles.Select(m => GetMediaFileUrl(m)).Where(url => !string.IsNullOrEmpty(url)).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get media playlist URLs");
                return new List<string>();
            }
        }

        /// <summary>
        /// 미디어 파일의 실제 URL 생성
        /// </summary>
        private string GetMediaFileUrl(WicsPlatform.Server.Models.wics.Medium mediaFile)
        {
            try
            {
                if (string.IsNullOrEmpty(mediaFile.FullPath))
                {
                    return null;
                }

                // 상대 경로를 절대 URL로 변환
                var baseUri = NavigationManager.BaseUri.TrimEnd('/');
                
                if (mediaFile.FullPath.StartsWith("/"))
                {
                    return $"{baseUri}{mediaFile.FullPath}";
                }
                else if (!mediaFile.FullPath.StartsWith("http"))
                {
                    return $"{baseUri}/Uploads/{mediaFile.FullPath}";
                }
                else
                {
                    return mediaFile.FullPath;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error generating URL for media file {mediaFile.Id}");
                return null;
            }
        }

        /// <summary>
        /// 미디어 스트리밍 중지
        /// </summary>
        private async Task StopMediaStreaming()
        {
            if (_mediaStreamerModule != null && _isMediaStreaming)
            {
                try
                {
                    await _mediaStreamerModule.InvokeVoidAsync("stopMediaStreaming");
                    _isMediaStreaming = false;
                    _currentMediaPlaylist.Clear();
                    _logger.LogInformation("Media streaming stopped");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error stopping media streaming");
                }
            }
        }

        /// <summary>
        /// JavaScript에서 호출되는 미디어 오디오 캡처 핸들러
        /// </summary>
        [JSInvokable]
        public async Task OnMediaAudioCaptured(string base64Data)
        {
            // ✅ 미디어가 비활성화된 경우 처리 중단
            if (!isMediaEnabled || string.IsNullOrWhiteSpace(base64Data)) return;

            try
            {
                byte[] data = Convert.FromBase64String(base64Data);

                // 마이크와 동일한 통계 업데이트 (별도 추적 가능)
                UpdateAudioStatistics(data);

                // 녹음에 추가 (마이크와 미디어 모두 녹음됨)
                if (isRecording)
                    recordedChunks.Add(data);

                // 모니터링 섹션에 데이터 전달
                if (micDataSection != null)
                    await micDataSection.OnAudioCaptured(data);

                // ✅ 개별 전송: 미디어 데이터를 별도 UDP 패킷으로 전송
                if (!string.IsNullOrEmpty(currentBroadcastId))
                    await WebSocketService.SendAudioDataAsync(currentBroadcastId, data);

                // 루프백 설정시 스피커로 피드
                if (_currentLoopbackSetting && _speakerModule != null)
                    await _speakerModule.InvokeVoidAsync("feed", base64Data);

                // UI 업데이트 최적화
                if (totalDataPackets % 10 == 0)
                {
                    await InvokeAsync(StateHasChanged);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"OnMediaAudioCaptured – 오류: {ex.Message}");
            }
        }

        /// <summary>
        /// JavaScript에서 호출되는 미디어 플레이리스트 종료 핸들러
        /// </summary>
        [JSInvokable]
        public async Task OnMediaPlaylistEnded()
        {
            _logger.LogInformation("Media playlist ended");
            _isMediaStreaming = false;
            
            // 플레이리스트가 끝났을 때의 처리 (필요시 반복 재생 등)
            if (isMediaEnabled && isBroadcasting)
            {
                // 루프 재생 (선택사항)
                var mediaUrls = await GetMediaPlaylistUrls();
                if (mediaUrls.Any())
                {
                    await Task.Delay(1000); // 1초 대기 후 재시작
                    var restarted = await _mediaStreamerModule.InvokeAsync<bool>("loadAndStreamMediaPlaylist", mediaUrls.ToArray());
                    if (restarted)
                    {
                        _isMediaStreaming = true;
                        _logger.LogInformation("Media playlist restarted for loop playback");
                    }
                }
            }
            
            await InvokeAsync(StateHasChanged);
        }

        #endregion
    }
}