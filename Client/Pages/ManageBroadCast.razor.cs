using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Radzen;
using System.Net.Http.Json;
using WicsPlatform.Audio;
using WicsPlatform.Client.Dialogs;
using WicsPlatform.Client.Pages.SubPages;
using WicsPlatform.Client.Services;
using WicsPlatform.Client.Services.Interfaces;

namespace WicsPlatform.Client.Pages;

public partial class ManageBroadCast : IDisposable, IAsyncDisposable
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

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            try
            {
                _dotNetRef = DotNetObjectReference.Create(this);
                
                // 스피커 모듈은 한 번만 초기화하고 계속 사용
                _speakerModule = await JSRuntime.InvokeAsync<IJSObjectReference>("import", "./js/speaker.js");
                await _speakerModule.InvokeVoidAsync("init");
                
                _logger.LogInformation("스피커 모듈 초기화 완료");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "스피커 모듈 초기화 실패");
            }
        }
    }

    protected override async Task OnParametersSetAsync()
    {
        if (selectedChannel == null) return;

        micVolume = (int)(selectedChannel.MicVolume * 100);
        mediaVolume = (int)(selectedChannel.MediaVolume * 100);
        ttsVolume = (int)(selectedChannel.TtsVolume * 100);
    }

    // 기존 IDisposable은 최소 정리만 수행하고, 실제 JS/Interop 정리는 DisposeAsync에서 처리
    public void Dispose()
    {
        UnsubscribeFromWebSocketEvents();
        UnsubscribeFromRecordingEvents();
        // JS 및 DotNetObjectReference 정리는 DisposeAsync에서 안전하게 수행됨
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            UnsubscribeFromWebSocketEvents();
            UnsubscribeFromRecordingEvents();

            // 1) JS 쪽 믹서를 먼저 안전하게 종료하여 JS → .NET 콜백을 중단
            await CleanupMicrophone();

            // 2) 타이머 및 서비스 정리
            _broadcastTimer?.Dispose();
            RecordingService?.Dispose();

            // 3) JS 모듈 정리 (스피커는 재사용 대상이 아니므로 안전 종료)
            try
            {
                if (_speakerModule != null)
                {
                    await _speakerModule.DisposeAsync();
                    _speakerModule = null;
                }

                if (_mixerModule != null)
                {
                    await _mixerModule.DisposeAsync();
                    _mixerModule = null;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "JS 모듈 정리 실패");
            }
        }
        finally
        {
            // 4) 마지막에 DotNetObjectReference 해제 (JS에서 더 이상 참조하지 않도록 위에서 dispose 호출 선행)
            _dotNetRef?.Dispose();
            _dotNetRef = null;
        }
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

    protected async Task OpenCreateChannelDialog()
    {
        var result = await DialogService.OpenAsync<AddChannelDialog>(
            "새 채널 만들기",
            null,
            new DialogOptions
            {
                Width = "500px",
                Height = "auto",
                Resizable = false,
                Draggable = true
            });

        if (result is bool success && success)
        {
            // 채널 목록 다시 로드
            await LoadInitialData();
            NotifySuccess("채널 생성", "새 채널이 성공적으로 생성되었습니다.");
        }
    }

    protected async Task SelectChannel(WicsPlatform.Server.Models.wics.Channel channel)
    {
        _logger.LogInformation($"SelectChannel called - Old: {selectedChannel?.Id}, New: {channel?.Id}");

        // 이전 채널과 같은 채널을 선택한 경우 무시
        if (selectedChannel?.Id == channel?.Id)
        {
            _logger.LogInformation("Same channel selected, ignoring");
            return;
        }

        // 현재 방송 중이면 먼저 정리
        if (isBroadcasting)
        {
            _logger.LogInformation("Cleaning up current broadcast before channel switch");
            await CleanupCurrentBroadcast();
        }

        // 새 채널 선택
        selectedChannel = channel;
        ResetAllPanels();

        // SubPage들 초기화
        InitializeSubPages();

        if (channel != null)
        {
            micVolume = (int)(channel.MicVolume * 100);
            mediaVolume = (int)(channel.MediaVolume * 100);
            ttsVolume = (int)(channel.TtsVolume * 100);

            _preferredSampleRate = channel.SamplingRate > 0 ? (int)channel.SamplingRate : 48000;
            _preferredChannels = channel.ChannelCount;

            _logger.LogInformation($"Channel selected: {channel.Name}, Settings: {_preferredSampleRate}Hz, {_preferredChannels}ch");

            // 복구 루틴 실행
            await CheckAndRecoverIfNeeded(channel.Id);
        }

        await InvokeAsync(StateHasChanged);
    }

    private async Task CleanupCurrentBroadcast()
    {
        try
        {
            _logger.LogInformation("Starting cleanup of current broadcast");

            // 1. 방송 상태 초기화
            isBroadcasting = false;
            var broadcastIdToStop = currentBroadcastId;
            currentBroadcastId = null;

            // 2. 타이머 정리
            if (_broadcastTimer != null)
            {
                _broadcastTimer.Dispose();
                _broadcastTimer = null;
            }

            // 3. WebSocket 정리
            if (broadcastIdToStop.HasValue)
            {
                await StopWebSocketBroadcast();
            }

            // 4. 마이크 및 오디오 믹서 정리
            await CleanupMicrophone();

            // 5. DB 업데이트 (이전 채널의 방송 상태를 종료로 변경)
            if (selectedChannel != null)
            {
                await UpdateBroadcastRecordsToStopped();
            }

            // 6. 루프백 설정 초기화
            _currentLoopbackSetting = false;

            _logger.LogInformation("Cleanup completed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during cleanup");
        }
    }

    private void InitializeSubPages()
    {
        // 모니터링 섹션 초기화
        if (monitoringSection != null)
        {
            monitoringSection.ResetBroadcastState();
        }

        // 스피커 섹션 초기화
        if (speakerSection != null)
        {
            speakerSection.ClearSelection();
        }

        // 플레이리스트 섹션 초기화
        if (playlistSection != null)
        {
            playlistSection.ResetMediaPlaybackState();
        }

        // TTS 섹션 초기화
        if (ttsSection != null)
        {
            ttsSection.ResetTtsPlaybackState();
        }

        _logger.LogInformation("All SubPages initialized");
    }

    #region Broadcast Control
    protected async Task StartBroadcast()
    {
        _logger.LogInformation("StartBroadcast 메서드 호출됨");

        if (!ValidateBroadcastPrerequisites()) return;

        try
        {
            var onlineSpeakers = speakerSection.GetOnlineSpeakers();
            var offlineSpeakers = speakerSection.GetOfflineSpeakers();

            if (!await ValidateAndNotifyOnlineStatus(onlineSpeakers, offlineSpeakers)) return;

            var onlineGroups = speakerSection.GetSelectedGroups();

            // DB 저장 작업
            _logger.LogInformation("1단계: DB 저장 작업");
            LoggingService.AddLog("INFO", "미디어 선택사항 DB 저장");
            await SaveSelectedMediaToChannel();

            LoggingService.AddLog("INFO", "TTS 선택사항 DB 저장");
            await SaveSelectedTtsToChannel();

            // 마이크 초기화
            _logger.LogInformation("2단계: 오디오 믹서 초기화 (필요시)");
            if (!await InitializeAudioMixer()) return;

            _logger.LogInformation("3단계: WebSocket 연결 시작");
            LoggingService.AddLog("INFO", "WebSocket 연결 중...");

            if (!await InitializeWebSocketBroadcast(onlineGroups)) return;

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
            _currentLoopbackSetting = false;

            try
            {
                await InvokeAsync(StateHasChanged);
            }
            catch { }
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