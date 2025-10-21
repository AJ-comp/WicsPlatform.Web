using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using Radzen;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using WicsPlatform.Client.Dialogs;
using WicsPlatform.Shared;

namespace WicsPlatform.Client.Pages.SubPages
{
    public partial class BroadcastTtsSection
    {
        /* ────────────────────── [Parameters] ────────────────────── */
        [Parameter] public WicsPlatform.Server.Models.wics.Channel Channel { get; set; }
        [Parameter] public bool IsCollapsed { get; set; }
        [Parameter] public EventCallback<bool> IsCollapsedChanged { get; set; }

        // 방송 관련 파라미터 추가
        [Parameter] public bool IsBroadcasting { get; set; }
        [Parameter] public string BroadcastId { get; set; }

        /* ────────────────────── [DI] ─────────────────────────────── */
        [Inject] protected NotificationService NotificationService { get; set; }
        [Inject] protected wicsService WicsService { get; set; }
        [Inject] protected IJSRuntime JSRuntime { get; set; }
        [Inject] protected DialogService DialogService { get; set; }
        [Inject] protected HttpClient Http { get; set; }
        [Inject] protected ILogger<BroadcastTtsSection> logger { get; set; }

        /* ────────────────────── [State] ──────────────────────────── */
        private IEnumerable<WicsPlatform.Server.Models.wics.Tt> ttsList = new List<WicsPlatform.Server.Models.wics.Tt>();
        private WicsPlatform.Server.Models.wics.Tt selectedTts = null;
        private ulong? selectedTtsId = null;
        private bool isLoadingTts = false;
        private ulong? playingTtsId = null;   // 현재 재생 중인 TTS (로컬)

        // ★ 변경: 미디어처럼 선택된 TTS를 메모리에 저장
        private List<WicsPlatform.Server.Models.wics.Tt> _selectedTtsList = new List<WicsPlatform.Server.Models.wics.Tt>();

        // ★ 중복 로딩 방지를 위한 변수들
        private ulong? _lastLoadedChannelId = null;
        private bool _hasLoadedTts = false;

        // ★ 방송 중 TTS 재생 관련 추가
        private bool isTtsPlaying = false;
        private bool isTtsActionInProgress = false;
        private string currentTtsSessionId = null;

        /* ────────────────────── [Life‑Cycle] ─────────────────────── */
        protected override async Task OnInitializedAsync()
        {
            await LoadTts();
            _hasLoadedTts = true;
        }

        protected override async Task OnParametersSetAsync()
        {
            // 채널이 실제로 변경되었을 때만 채널 TTS를 다시 로드
            if (Channel != null && !IsCollapsed && _lastLoadedChannelId != Channel.Id)
            {
                _lastLoadedChannelId = Channel.Id;
                await LoadChannelTts(Channel.Id);
            }

            // 방송이 종료되면 TTS 재생 상태 초기화
            if (!IsBroadcasting && isTtsPlaying)
            {
                isTtsPlaying = false;
                currentTtsSessionId = null;
                StateHasChanged();
            }
        }


        // TTS 선택 복구 메서드 추가
        public async Task RecoverSelectedTts(List<ulong> ttsIds)
        {
            try
            {
                _selectedTtsList.Clear();
                selectedTtsId = null;
                selectedTts = null;

                // TTS 목록이 로드되지 않았다면 로드
                if (!ttsList.Any())
                {
                    await LoadTts();
                }

                // 첫 번째 TTS만 선택 (하나만 선택 가능)
                if (ttsIds.Any())
                {
                    var ttsId = ttsIds.First();
                    var tts = ttsList.FirstOrDefault(t => t.Id == ttsId);

                    if (tts != null)
                    {
                        selectedTtsId = ttsId;
                        selectedTts = tts;
                        _selectedTtsList.Add(tts);
                    }
                }

                logger.LogInformation($"Recovered TTS selection: {selectedTtsId}");
                await InvokeAsync(StateHasChanged);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to recover TTS selection");
            }
        }

        /* ────────────────────── [Panel Toggle] ───────────────────── */
        private async Task TogglePanel()
        {
            var newCollapsedState = !IsCollapsed;
            await IsCollapsedChanged.InvokeAsync(newCollapsedState);

            // 패널이 열릴 때만 그리고 아직 로드되지 않은 경우에만 로드
            if (!newCollapsedState && Channel != null && _lastLoadedChannelId != Channel.Id)
            {
                _lastLoadedChannelId = Channel.Id;
                await LoadChannelTts(Channel.Id);
            }
        }

        /* ────────────────────── [TTS 재생/중지] ────────────────────── */
        private async Task PlayTts()
        {
            if (!IsBroadcasting || string.IsNullOrEmpty(BroadcastId) || !HasSelectedTts())
            {
                NotifyWarning("TTS를 재생하려면 방송 중이어야 하고 TTS가 선택되어 있어야 합니다.");
                return;
            }

            try
            {
                isTtsActionInProgress = true;
                StateHasChanged();

                // 선택된 TTS ID 가져오기
                var ttsIds = GetSelectedTts().Select(t => t.Id).ToList();
                if (!ttsIds.Any())
                {
                    NotifyWarning("선택된 TTS가 없습니다.");
                    return;
                }

                // TtsPlayerController의 play 엔드포인트 호출
                var request = new TtsPlayRequest
                {
                    BroadcastId = string.IsNullOrWhiteSpace(BroadcastId) ? 0 : ulong.Parse(BroadcastId),
                    TtsIds = ttsIds
                };

                var response = await Http.PostAsJsonAsync("api/ttsplayer/play", request);

                if (response.IsSuccessStatusCode)
                {
                    var result = await response.Content.ReadFromJsonAsync<TtsPlayResponse>();
                    if (result.Success)
                    {
                        isTtsPlaying = true;
                        currentTtsSessionId = result.SessionId;
                        NotifySuccess($"TTS 재생을 시작했습니다. ({ttsIds.Count}개 항목)");
                    }
                    else
                    {
                        NotifyError("TTS 재생 실패", new Exception(result.Message));
                    }
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    NotifyError("TTS 재생 요청 실패", new Exception($"Status: {response.StatusCode}, Error: {errorContent}"));
                }
            }
            catch (Exception ex)
            {
                NotifyError("TTS 재생 중 오류가 발생했습니다", ex);
            }
            finally
            {
                isTtsActionInProgress = false;
                StateHasChanged();
            }
        }

        private async Task StopTts()
        {
            if (!IsBroadcasting || string.IsNullOrEmpty(BroadcastId))
            {
                NotifyWarning("방송 중이 아닙니다.");
                return;
            }

            try
            {
                isTtsActionInProgress = true;
                StateHasChanged();

                // TtsPlayerController의 stop 엔드포인트 호출
                var request = new TtsStopRequest
                {
                    BroadcastId = string.IsNullOrWhiteSpace(BroadcastId) ? 0 : ulong.Parse(BroadcastId)
                };

                var response = await Http.PostAsJsonAsync("api/ttsplayer/stop", request);

                if (response.IsSuccessStatusCode)
                {
                    var result = await response.Content.ReadFromJsonAsync<TtsStopResponse>();
                    if (result.Success)
                    {
                        isTtsPlaying = false;
                        currentTtsSessionId = null;
                        NotifyInfo("TTS 재생을 중지했습니다.");
                    }
                    else
                    {
                        NotifyError("TTS 중지 실패", new Exception(result.Message));
                    }
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    NotifyError("TTS 중지 요청 실패", new Exception($"Status: {response.StatusCode}, Error: {errorContent}"));
                }
            }
            catch (Exception ex)
            {
                NotifyError("TTS 중지 중 오류가 발생했습니다", ex);
            }
            finally
            {
                isTtsActionInProgress = false;
                StateHasChanged();
            }
        }

        /* ────────────────────── [TTS 목록] ───────────────────────── */
        private async Task LoadTts()
        {
            // 이미 로드된 경우 스킵
            if (_hasLoadedTts && ttsList.Any())
                return;

            try
            {
                isLoadingTts = true;

                var query = new Radzen.Query
                {
                    Filter = "DeleteYn eq 'N' or DeleteYn eq null",
                    OrderBy = "CreatedAt desc",
                    Top = 50,
                    Select = "Id,Name,Content,CreatedAt"
                };

                var result = await WicsService.GetTts(query);
                ttsList = result.Value.AsODataEnumerable();
                _hasLoadedTts = true;
            }
            catch (Exception ex)
            {
                NotifyError("TTS 목록을 불러오는 중 오류가 발생했습니다", ex);
            }
            finally
            {
                isLoadingTts = false;
            }
        }

        // TTS 목록 강제 새로고침 메서드 추가
        private async Task RefreshTtsList()
        {
            _hasLoadedTts = false;
            await LoadTts();
        }

        /* ────────────────────── [채널‑TTS 매핑] ───────────────────── */
        private async Task LoadChannelTts(ulong channelId)
        {
            try
            {
                isLoadingTts = true;

                var query = new Radzen.Query
                {
                    Filter = $"ChannelId eq {channelId} and (DeleteYn eq 'N' or DeleteYn eq null)"
                };

                var result = await WicsService.GetMapChannelTts(query);
                var mapping = result.Value.AsODataEnumerable().FirstOrDefault();

                if (mapping != null)
                {
                    // 매핑된 TTS를 찾아서 선택
                    selectedTts = ttsList.FirstOrDefault(t => t.Id == mapping.TtsId);
                    if (selectedTts != null)
                    {
                        selectedTtsId = selectedTts.Id;
                        _selectedTtsList.Clear();
                        _selectedTtsList.Add(selectedTts);
                    }
                }
                else
                {
                    selectedTts = null;
                    selectedTtsId = null;
                    _selectedTtsList.Clear();
                }
            }
            catch (Exception ex)
            {
                NotifyError("채널 TTS 정보를 불러오는 중 오류가 발생했습니다", ex);
            }
            finally
            {
                isLoadingTts = false;
            }
        }

        /* ────────────────────── [선택 변경] ──────────────────────── */
        private async Task OnRadioButtonListChanged()
        {
            if (selectedTtsId.HasValue)
            {
                selectedTts = ttsList.FirstOrDefault(t => t.Id == selectedTtsId.Value);
                if (selectedTts != null)
                {
                    _selectedTtsList.Clear();
                    _selectedTtsList.Add(selectedTts);
                }
            }
            else
            {
                selectedTts = null;
                _selectedTtsList.Clear();
            }

            await InvokeAsync(StateHasChanged);
        }

        private async Task ClearTtsSelection()
        {
            selectedTts = null;
            selectedTtsId = null;
            _selectedTtsList.Clear();

            NotifyInfo("TTS 선택이 해제되었습니다.");
            await InvokeAsync(StateHasChanged);
        }

        /* ────────────────────── [Public Methods] ────────────────────── */

        /// <summary>
        /// 선택된 TTS 목록 가져오기
        /// </summary>
        public IEnumerable<WicsPlatform.Server.Models.wics.Tt> GetSelectedTts()
        {
            return _selectedTtsList.AsEnumerable();
        }

        /// <summary>
        /// 선택된 TTS가 있는지 확인
        /// </summary>
        public bool HasSelectedTts()
        {
            return _selectedTtsList.Any();
        }

        /// <summary>
        /// TTS 재생 상태 초기화 (외부에서 호출 가능)
        /// </summary>
        public void ResetTtsPlaybackState()
        {
            isTtsPlaying = false;
            currentTtsSessionId = null;
            StateHasChanged();
        }

        /// <summary>
        /// TTS 재생 상태 복원 (복구 시나리오)
        /// </summary>
        public void RestorePlaybackState()
        {
            // TTS 재생 상태 복원
            isTtsPlaying = true;
            StateHasChanged();
        }

        /* ────────────────────── [로컬 재생 / 중지] ────────────────────── */
        /// <summary>
        /// Web Speech API를 이용해 브라우저에서 직접 재생/중지
        /// </summary>
        private async Task ToggleTtsPlayback(WicsPlatform.Server.Models.wics.Tt tts)
        {
            try
            {
                if (playingTtsId == tts.Id) // ■ 중지
                {
                    await JSRuntime.InvokeVoidAsync("stopTTS");
                    playingTtsId = null;

                    NotifyInfo("TTS 재생이 중지되었습니다.");
                }
                else                       // ▶ 재생
                {
                    await JSRuntime.InvokeVoidAsync("playTTS", tts.Content);
                    playingTtsId = tts.Id;

                    NotifySuccess($"'{tts.Name}' TTS를 재생합니다.");

                    // 예상 재생 시간 후 자동 상태 리셋
                    var estimatedDuration = EstimateTtsDuration(tts.Content);
                    await Task.Delay(estimatedDuration);

                    if (playingTtsId == tts.Id)
                    {
                        playingTtsId = null;
                        StateHasChanged();
                    }
                }
            }
            catch (JSException jsEx)
            {
                NotifyError("브라우저에서 TTS를 재생할 수 없습니다. (Web Speech API 미지원)", jsEx);
            }
            catch (Exception ex)
            {
                NotifyError("TTS 재생 처리 중 오류가 발생했습니다", ex);
            }
            finally
            {
                StateHasChanged();
            }
        }

        /* Web Speech API 속도(한국어 약 280자/분)로 대략적인 재생 시간을 계산 */
        private int EstimateTtsDuration(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
                return 3000;

            var charCount = content.Length;
            var estimatedSeconds = (int)Math.Ceiling(charCount / 4.67); // 280자/60초 ≒ 4.67자/초
            estimatedSeconds = Math.Clamp(estimatedSeconds, 3, 60); // 3‑60초 제한

            return estimatedSeconds * 1000;
        }

        private async Task ShowAddTtsDialog()
        {
            var result = await DialogService.OpenAsync<AddTtsDialog>(
                "새 TTS 추가",
                null,
                new DialogOptions { Width = "600px", Resizable = true, Draggable = true });

            if (true.Equals(result))
            {
                await RefreshTtsList();
            }
        }

        /* ────────────────────── [TTS 편집] ────────────────────── */
        protected async Task OpenEditTtsDialog(WicsPlatform.Server.Models.wics.Tt tts)
        {
            var parameters = new Dictionary<string, object>
            {
                { "TtsId", tts.Id }
            };
            var result = await DialogService.OpenAsync<Dialogs.EditTtsDialog>("TTS 수정",
                parameters,
                new DialogOptions()
                {
                    Width = "600px",
                    Height = "auto",
                    Resizable = true,
                    Draggable = true
                });
            if (result == true)
            {
                await RefreshTtsList();
            }
        }

        /* ────────────────────── [TTS 삭제] ────────────────────── */
        protected async Task OpenDeleteTtsDialog(WicsPlatform.Server.Models.wics.Tt tts)
        {
            var result = await DialogService.Confirm($"'{tts.Name}' TTS를 삭제하시겠습니까?", "TTS 삭제",
                new ConfirmOptions() { OkButtonText = "삭제", CancelButtonText = "취소" });
            if (result == true)
            {
                await DeleteTts(tts);
            }
        }

        protected async Task DeleteTts(WicsPlatform.Server.Models.wics.Tt tts)
        {
            try
            {
                // 삭제할 TTS가 현재 선택된 TTS인 경우 처리
                if (selectedTts?.Id == tts.Id || selectedTtsId == tts.Id)
                {
                    await ClearTtsSelection();
                }

                // 소프트 삭제를 위한 업데이트 요청
                var softDeleteRequest = new
                {
                    DeleteYn = "Y",
                    UpdatedAt = DateTime.UtcNow
                };

                // PATCH 메서드를 사용하여 DeleteYn 필드만 업데이트
                var response = await Http.PatchAsJsonAsync($"odata/wics/Tts({tts.Id})", softDeleteRequest);

                if (response.IsSuccessStatusCode)
                {
                    NotificationService.Notify(new NotificationMessage
                    {
                        Severity = NotificationSeverity.Success,
                        Summary = "삭제 완료",
                        Detail = $"'{tts.Name}' TTS가 삭제되었습니다.",
                        Duration = 4000
                    });
                    await RefreshTtsList();
                }
                else
                {
                    NotificationService.Notify(new NotificationMessage
                    {
                        Severity = NotificationSeverity.Error,
                        Summary = "삭제 실패",
                        Detail = "TTS 삭제 중 오류가 발생했습니다.",
                        Duration = 4000
                    });
                }
            }
            catch (Exception ex)
            {
                NotificationService.Notify(new NotificationMessage
                {
                    Severity = NotificationSeverity.Error,
                    Summary = "오류",
                    Detail = $"TTS 삭제 중 오류가 발생했습니다: {ex.Message}",
                    Duration = 4000
                });
            }
        }

        /* ────────────────────── [Helpers] ───────────────────────── */
        private void NotifySuccess(string message) => Notify(NotificationSeverity.Success, "완료", message);
        private void NotifyInfo(string message) => Notify(NotificationSeverity.Info, "안내", message);
        private void NotifyWarning(string message) => Notify(NotificationSeverity.Warning, "경고", message);
        private void NotifyError(string summary, Exception ex) =>
            Notify(NotificationSeverity.Error, summary, ex.Message);

        private void Notify(NotificationSeverity severity, string summary, string detail) =>
            NotificationService.Notify(new NotificationMessage
            {
                Severity = severity,
                Summary = summary,
                Detail = detail,
                Duration = 4000
            });
    }
}