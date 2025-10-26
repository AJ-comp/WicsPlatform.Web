using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Radzen;
using System.Diagnostics;
using System.Net.Http.Json;
using WicsPlatform.Audio;
using WicsPlatform.Client.Dialogs;
using WicsPlatform.Client.Pages.SubPages;
using WicsPlatform.Client.Services;
using WicsPlatform.Client.Services.Interfaces;

namespace WicsPlatform.Client.Pages;

public partial class BroadcastLiveTab : IDisposable, IAsyncDisposable
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
    [Inject] protected ILogger<BroadcastLiveTab> _logger { get; set; }
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
    private DotNetObjectReference<BroadcastLiveTab> _dotNetRef;

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
                Width = "750px",
                Height = "auto",
                Resizable = true,
                Draggable = true
            });

        if (result is bool success && success)
        {
            // 채널 목록 다시 로드
            await LoadInitialData();
            NotifySuccess("채널 생성", "새 채널이 성공적으로 생성되었습니다.");
        }
    }

    protected async Task OpenEditChannelDialog(WicsPlatform.Server.Models.wics.Channel channel)
    {
        var parameters = new Dictionary<string, object>
        {
            { "ChannelId", channel.Id }
        };

        var result = await DialogService.OpenAsync<EditChannelDialog>(
            $"'{channel.Name}' 채널 편집",
            parameters,
            new DialogOptions
            {
                Width = "750px",
                Height = "auto",
                Resizable = true,
                Draggable = true
            });

        if (result is bool success && success)
        {
            // 채널 목록 다시 로드
            await LoadInitialData();
            NotifySuccess("채널 편집", "채널이 성공적으로 수정되었습니다.");
        }
    }

    protected async Task ConfirmDeleteChannel(WicsPlatform.Server.Models.wics.Channel channel)
    {
        var result = await DialogService.Confirm(
            $"'{channel.Name}' 채널을 삭제하시겠습니까?\n연결된 콘텐츠 및 스피커 매핑도 함께 삭제됩니다.",
            "채널 삭제 확인",
            new ConfirmOptions
            {
                OkButtonText = "삭제",
                CancelButtonText = "취소"
            });

        if (result == true)
        {
            await DeleteChannel(channel);
        }
    }

    protected async Task DeleteChannel(WicsPlatform.Server.Models.wics.Channel channel)
    {
        try
        {
            _logger.LogInformation($"Deleting channel: {channel.Name} (ID: {channel.Id})");

            // 1. 채널 관련 미디어 매핑 소프트 삭제
            var mapMediaQuery = new Radzen.Query
            {
                Filter = $"ChannelId eq {channel.Id} and (DeleteYn eq 'N' or DeleteYn eq null)"
            };
            var mapMediaResult = await WicsService.GetMapChannelMedia(mapMediaQuery);
            foreach (var m in mapMediaResult.Value.AsODataEnumerable())
            {
                m.DeleteYn = "Y";
                m.UpdatedAt = DateTime.UtcNow;
                await WicsService.UpdateMapChannelMedium(m.Id, m);
            }

            // 2. 채널 관련 TTS 매핑 소프트 삭제
            var mapTtsQuery = new Radzen.Query
            {
                Filter = $"ChannelId eq {channel.Id} and (DeleteYn eq 'N' or DeleteYn eq null)"
            };
            var mapTtsResult = await WicsService.GetMapChannelTts(mapTtsQuery);
            foreach (var t in mapTtsResult.Value.AsODataEnumerable())
            {
                t.DeleteYn = "Y";
                t.UpdatedAt = DateTime.UtcNow;
                await WicsService.UpdateMapChannelTt(t.Id, t);
            }

            // 3. 채널 관련 그룹 매핑 소프트 삭제
            var mapGroupsQuery = new Radzen.Query
            {
                Filter = $"ChannelId eq {channel.Id} and (DeleteYn eq 'N' or DeleteYn eq null)"
            };
            var mapGroupsResult = await WicsService.GetMapChannelGroups(mapGroupsQuery);
            foreach (var g in mapGroupsResult.Value.AsODataEnumerable())
            {
                g.DeleteYn = "Y";
                g.UpdatedAt = DateTime.UtcNow;
                await WicsService.UpdateMapChannelGroup(g.Id, g);
            }

            // 4. 채널 관련 스피커 매핑 소프트 삭제
            var mapSpeakersQuery = new Radzen.Query
            {
                Filter = $"ChannelId eq {channel.Id} and (DeleteYn eq 'N' or DeleteYn eq null)"
            };
            var mapSpeakersResult = await WicsService.GetMapChannelSpeakers(mapSpeakersQuery);
            foreach (var s in mapSpeakersResult.Value.AsODataEnumerable())
            {
                s.DeleteYn = "Y";
                s.UpdatedAt = DateTime.UtcNow;
                await WicsService.UpdateMapChannelSpeaker(s.Id, s);
            }

            // 5. 채널 자체를 소프트 삭제
            channel.DeleteYn = "Y";
            channel.UpdatedAt = DateTime.UtcNow;
            await WicsService.UpdateChannel(channel.Id, channel);

            _logger.LogInformation($"Successfully deleted channel and related data: {channel.Name}");

            // 선택된 채널이 삭제된 채널이면 선택 해제
            if (selectedChannel?.Id == channel.Id)
            {
                selectedChannel = null;
            }

            // 채널 목록 다시 로드
            await LoadInitialData();

            NotifySuccess("채널 삭제", $"'{channel.Name}' 채널이 삭제되었습니다.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Failed to delete channel: {channel.Name}");
            NotifyError("채널 삭제", ex);
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

        // ========== 1단계: 채널 선택 및 기본 설정 ==========
        selectedChannel = channel;
        micVolume = (int)(channel.MicVolume * 100);
        mediaVolume = (int)(channel.MediaVolume * 100);
        ttsVolume = (int)(channel.TtsVolume * 100);
        _preferredSampleRate = channel.SamplingRate > 0 ? (int)channel.SamplingRate : 48000;
        _preferredChannels = channel.ChannelCount;

        _logger.LogInformation($"[1단계] 채널 선택: {channel.Name} (ID: {channel.Id})");

        // ========== 2단계: UI 패널 초기화 ==========
        ResetAllPanels();
        InitializeSubPages();
        await InvokeAsync(StateHasChanged);

        // ========== 3단계: 채널 데이터 로드 (스피커/그룹/플레이리스트/미디어) ==========
        await LoadChannelData();

        // ========== 4단계: 방송 복구 (state=1인 경우만) ==========
        if (channel.State == 1)
        {
            _logger.LogInformation($"[4단계] state=1 감지 - 방송 복구 시작");
            await RecoverBroadcast();
        }
        else
        {
            _logger.LogInformation($"[4단계] state=0 - 완료");
        }
    }

    /// <summary>
    /// 채널 데이터 로드: 스피커/그룹/플레이리스트/미디어
    /// </summary>
    private async Task LoadChannelData()
    {
        _logger.LogInformation("[3단계] 채널 데이터 로드 시작");

        // 컴포넌트 렌더링 강제 및 다음 tick 대기 (1-10ms)
        StateHasChanged();
        await Task.Yield();

        // 스피커/그룹 로드
        if (speakerSection != null)
        {
            await speakerSection.LoadChannelMappings();
        }

        // 플레이리스트/미디어 로드
        if (playlistSection != null)
        {
            await playlistSection.LoadChannelMappings();
        }

        _logger.LogInformation("[3단계] 채널 데이터 로드 완료 ✅");
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

        // 스피커 섹션: 선택 초기화만 (채널 매핑은 OnParametersSetAsync에서 자동 로드됨)
        // 참고: BroadcastSpeakerSection의 OnParametersSetAsync가 Channel 파라미터 변경 시 자동으로 LoadChannelMappings 호출
        // 예약방송과 동일한 방식으로 작동함

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
    protected async Task StartBroadcast(bool isRecovery = false)
    {
        Debug.WriteLine($"[방송시작] ========== StartBroadcast() 메서드 호출 (복구모드: {isRecovery}) ==========");
        _logger.LogInformation($"StartBroadcast 메서드 호출됨 (복구모드: {isRecovery})");

        Debug.WriteLine($"[방송시작] 사전 조건 검증 중...");
        if (!ValidateBroadcastPrerequisites(isRecovery))
        {
            Debug.WriteLine($"[방송시작] ❌ 사전 조건 검증 실패 - 중단");
            return;
        }
        Debug.WriteLine($"[방송시작] ✓ 사전 조건 검증 완료");

        try
        {
            Debug.WriteLine($"[방송시작] 스피커 온라인/오프라인 상태 확인 중...");
            var onlineSpeakers = speakerSection.GetOnlineSpeakers();
            var offlineSpeakers = speakerSection.GetOfflineSpeakers();
            Debug.WriteLine($"[방송시작] 온라인 스피커: {onlineSpeakers.Count}대, 오프라인: {offlineSpeakers.Count}대");

            if (!await ValidateAndNotifyOnlineStatus(onlineSpeakers, offlineSpeakers))
            {
                Debug.WriteLine($"[방송시작] ❌ 온라인 스피커 검증 실패 - 중단");
                return;
            }
            Debug.WriteLine($"[방송시작] ✓ 온라인 스피커 검증 완료");

            var onlineGroups = speakerSection.GetSelectedGroups();
            Debug.WriteLine($"[방송시작] 선택된 그룹: {onlineGroups.Count}개");

            // DB 저장 작업 (복구 모드에서는 건너뜀)
            if (!isRecovery)
            {
                Debug.WriteLine($"[방송시작] ===== 1단계: DB 저장 작업 =====");
                _logger.LogInformation("1단계: DB 저장 작업");
                
                // 모든 채널 설정을 한 번에 저장
                await SaveAllChannelSettings();
                
                Debug.WriteLine($"[방송시작] ✓ 1단계: 모든 설정 DB 저장 완료");
            }
            else
            {
                Debug.WriteLine($"[방송시작] ===== 1단계: DB 저장 건너뜀 (복구 모드) =====");
                _logger.LogInformation("1단계: DB 저장 건너뜀 (복구 모드)");
            }

            // 마이크 초기화
            Debug.WriteLine($"[방송시작] ===== 2단계: 오디오 믹서 초기화 =====");
            _logger.LogInformation("2단계: 오디오 믹서 초기화 (필요시)");
            if (!await InitializeAudioMixer())
            {
                Debug.WriteLine($"[방송시작] ❌ 오디오 믹서 초기화 실패 - 중단");
                return;
            }
            Debug.WriteLine($"[방송시작] ✓ 오디오 믹서 초기화 완료");

            Debug.WriteLine($"[방송시작] ===== 3단계: WebSocket 연결 시작 =====");
            _logger.LogInformation("3단계: WebSocket 연결 시작");
            LoggingService.AddLog("INFO", "WebSocket 연결 중...");
            Debug.WriteLine($"[방송시작] WebSocket 연결 중... (채널: {selectedChannel.Id}, 그룹: {onlineGroups.Count}개)");

            if (!await InitializeWebSocketBroadcast(onlineGroups))
            {
                Debug.WriteLine($"[방송시작] ❌ WebSocket 연결 실패 - 중단");
                return;
            }
            Debug.WriteLine($"[방송시작] ✓ WebSocket 연결 완료 (BroadcastId: {currentBroadcastId})");

            // 마이크 활성화
            Debug.WriteLine($"[방송시작] ===== 4단계: 마이크 활성화 =====");
            _logger.LogInformation("4단계: 마이크 활성화 (필요시)");
            if (!await EnableMicrophone())
            {
                Debug.WriteLine($"[방송시작] ❌ 마이크 활성화 실패 - 정리 작업 시작");
                await CleanupFailedBroadcast();
                return;
            }
            Debug.WriteLine($"[방송시작] ✓ 마이크 활성화 완료");

            Debug.WriteLine($"[방송시작] ===== 5단계: 방송 상태 초기화 =====");
            InitializeBroadcastState();
            Debug.WriteLine($"[방송시작] ✓ 방송 상태 초기화 완료");
            
            Debug.WriteLine($"[방송시작] ===== 6단계: DB Broadcast 레코드 생성 =====");
            await CreateBroadcastRecords(onlineSpeakers);
            Debug.WriteLine($"[방송시작] ✓ Broadcast 레코드 생성 완료");
            
            // 채널 상태를 방송 중(1)으로 업데이트
            Debug.WriteLine($"[방송시작] ===== 7단계: 채널 상태 업데이트 (State=1) =====");
            await UpdateChannelState(1);
            Debug.WriteLine($"[방송시작] ✓ 채널 상태 업데이트 완료");

            Debug.WriteLine($"[방송시작] ========== 방송 시작 완료! ==========");
            NotifyBroadcastStarted(onlineSpeakers, offlineSpeakers);
            await InvokeAsync(StateHasChanged);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[방송시작] ❌❌❌ 예외 발생 ❌❌❌");
            Debug.WriteLine($"[방송시작] 예외 타입: {ex.GetType().Name}");
            Debug.WriteLine($"[방송시작] 에러 메시지: {ex.Message}");
            Debug.WriteLine($"[방송시작] 스택 트레이스:");
            Debug.WriteLine(ex.StackTrace);
            if (ex.InnerException != null)
            {
                Debug.WriteLine($"[방송시작] InnerException: {ex.InnerException.Message}");
                Debug.WriteLine(ex.InnerException.StackTrace);
            }
            await HandleBroadcastError(ex);
        }
    }

    private bool ValidateBroadcastPrerequisites(bool isRecovery = false)
    {
        if (selectedChannel == null)
        {
            Debug.WriteLine($"[검증] ❌ selectedChannel == null");
            NotifyWarn("채널 선택", "먼저 방송할 채널을 선택하세요.");
            return false;
        }

        // 복구 모드가 아닐 때만 그룹 검증 (복구 시에는 DB에서 비동기 로드 중)
        if (!isRecovery)
        {
            if (speakerSection == null || !speakerSection.GetSelectedGroups().Any())
            {
                Debug.WriteLine($"[검증] ❌ speakerSection == null: {speakerSection == null}");
                Debug.WriteLine($"[검증] ❌ 선택된 그룹 수: {speakerSection?.GetSelectedGroups()?.Count() ?? 0}");
                NotifyWarn("스피커 그룹 선택", "방송할 스피커 그룹을 선택하세요.");
                return false;
            }
        }
        else
        {
            Debug.WriteLine($"[검증] 복구 모드 - 그룹 검증 건너뜀 (DB에서 로드 중)");
        }

        Debug.WriteLine($"[검증] ✓ 사전 조건 검증 통과");
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

    /// <summary>
    /// 채널의 State 값을 업데이트합니다.
    /// </summary>
    /// <param name="state">0: 대기, 1: 방송 중</param>
    private async Task UpdateChannelState(sbyte state)
    {
        Debug.WriteLine($"========================================");
        Debug.WriteLine($"[State업데이트] UpdateChannelState 호출: state={state}");
        
        try
        {
            if (selectedChannel == null)
            {
                Debug.WriteLine($"[State업데이트] ❌ selectedChannel이 null");
                _logger.LogWarning("UpdateChannelState: selectedChannel is null");
                return;
            }

            Debug.WriteLine($"[State업데이트] 채널 ID: {selectedChannel.Id}");
            Debug.WriteLine($"[State업데이트] 현재 State: {selectedChannel.State}");
            Debug.WriteLine($"[State업데이트] 변경할 State: {state}");

            var updateData = new
            {
                State = state,
                UpdatedAt = DateTime.Now
            };

            var url = $"odata/wics/Channels({selectedChannel.Id})";
            Debug.WriteLine($"[State업데이트] PATCH URL: {url}");
            Debug.WriteLine($"[State업데이트] PATCH Data: State={state}, UpdatedAt={DateTime.Now}");

            var response = await Http.PatchAsJsonAsync(url, updateData);

            Debug.WriteLine($"[State업데이트] 응답 StatusCode: {response.StatusCode}");
            Debug.WriteLine($"[State업데이트] 응답 IsSuccessStatusCode: {response.IsSuccessStatusCode}");

            if (response.IsSuccessStatusCode)
            {
                Debug.WriteLine($"[State업데이트] ✓ DB 업데이트 성공!");
                Debug.WriteLine($"[State업데이트] selectedChannel.State를 {selectedChannel.State} → {state}로 변경");
                selectedChannel.State = state;
                Debug.WriteLine($"[State업데이트] 변경 후 selectedChannel.State: {selectedChannel.State}");
                _logger.LogInformation($"Channel {selectedChannel.Id} state updated to {state}");
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                Debug.WriteLine($"[State업데이트] ❌ DB 업데이트 실패: {response.StatusCode}");
                Debug.WriteLine($"[State업데이트] 에러 내용: {errorContent}");
                _logger.LogWarning($"Failed to update channel state: {response.StatusCode}");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[State업데이트] ❌❌❌ 예외 발생 ❌❌❌");
            Debug.WriteLine($"[State업데이트] 예외 타입: {ex.GetType().Name}");
            Debug.WriteLine($"[State업데이트] 에러 메시지: {ex.Message}");
            Debug.WriteLine($"[State업데이트] 스택 트레이스:");
            Debug.WriteLine(ex.StackTrace);
            _logger.LogError(ex, "Error updating channel state");
        }
        
        Debug.WriteLine($"========================================");
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

            // 채널 상태를 대기(0)로 업데이트
            await UpdateChannelState(0);

            await StopWebSocketBroadcast();
            await CleanupMicrophone();

            _currentLoopbackSetting = false;

            // 스피커 그룹 관리 섹션으로 돌아갈 때 채널 매핑 복원
            await InvokeAsync(StateHasChanged);
            await Task.Yield(); // UI 렌더링 완료 대기
            
            if (speakerSection != null && selectedChannel != null)
            {
                _logger.LogInformation("방송 종료 후 스피커/그룹 매핑 복원 시작");
                await speakerSection.LoadChannelMappings();
                _logger.LogInformation("스피커/그룹 매핑 복원 완료");
            }

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
        0 => BadgeStyle.Light,
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
