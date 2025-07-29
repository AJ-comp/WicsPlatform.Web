using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Radzen;
using System.Net.Http;
using System.Net.Http.Json;
using WicsPlatform.Client.Dialogs;

namespace WicsPlatform.Client.Pages.SubPages
{
    public partial class BroadcastTtsSection
    {
        /* ────────────────────── [Parameters] ────────────────────── */
        [Parameter] public WicsPlatform.Server.Models.wics.Channel Channel { get; set; }
        [Parameter] public bool IsCollapsed { get; set; }
        [Parameter] public EventCallback<bool> IsCollapsedChanged { get; set; }

        /* ────────────────────── [DI] ─────────────────────────────── */
        [Inject] protected NotificationService NotificationService { get; set; }
        [Inject] protected wicsService WicsService { get; set; }
        [Inject] protected IJSRuntime JSRuntime { get; set; }   // ★ JSInterop 주입
        [Inject] protected DialogService DialogService { get; set; }
        [Inject] protected HttpClient Http { get; set; }        // ★ HttpClient 주입 추가

        /* ────────────────────── [State] ──────────────────────────── */
        private IEnumerable<WicsPlatform.Server.Models.wics.Tt> ttsList = new List<WicsPlatform.Server.Models.wics.Tt>();
        private WicsPlatform.Server.Models.wics.MapChannelTt currentChannelTts = null;
        private ulong? selectedTtsId = null;
        private bool isLoadingTts = false;
        private bool isProcessingSelection = false;
        private ulong? playingTtsId = null;   // 현재 재생 중인 TTS

        // 채널‑TTS 매핑 캐시
        private static readonly Dictionary<ulong, WicsPlatform.Server.Models.wics.MapChannelTt> channelTtsCache
            = new();

        /* ────────────────────── [Life‑Cycle] ─────────────────────── */
        protected override async Task OnInitializedAsync()
        {
            await LoadTts();
        }

        protected override async Task OnParametersSetAsync()
        {
            if (Channel != null && !IsCollapsed)
            {
                await LoadChannelTts(Channel.Id);
            }
        }

        /* ────────────────────── [Panel Toggle] ───────────────────── */
        private async Task TogglePanel()
        {
            var newCollapsedState = !IsCollapsed;
            await IsCollapsedChanged.InvokeAsync(newCollapsedState);

            if (!newCollapsedState && Channel != null && !selectedTtsId.HasValue)
            {
                await LoadChannelTts(Channel.Id);
            }
        }

        /* ────────────────────── [TTS 목록] ───────────────────────── */
        private async Task LoadTts()
        {
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

        /* ────────────────────── [채널‑TTS 매핑] ───────────────────── */
        private async Task LoadChannelTts(ulong channelId)
        {
            try
            {
                isProcessingSelection = true;

                if (channelTtsCache.TryGetValue(channelId, out var cached))
                {
                    currentChannelTts = cached;
                    selectedTtsId = cached?.TtsId;
                    return;
                }

                isLoadingTts = true;

                var query = new Radzen.Query
                {
                    Filter = $"ChannelId eq {channelId} and (DeleteYn eq 'N' or DeleteYn eq null)",
                    Expand = "Tt",
                    Top = 1
                };

                var result = await WicsService.GetMapChannelTts(query);
                currentChannelTts = result.Value.AsODataEnumerable().FirstOrDefault();

                if (currentChannelTts != null)
                {
                    channelTtsCache[channelId] = currentChannelTts;
                    selectedTtsId = currentChannelTts.TtsId;
                }
                else
                {
                    selectedTtsId = null;
                }
            }
            catch (Exception ex)
            {
                NotifyError("채널 TTS 정보를 불러오는 중 오류가 발생했습니다", ex);
            }
            finally
            {
                isLoadingTts = false;
                isProcessingSelection = false;
            }
        }

        /* ────────────────────── [선택 변경] ──────────────────────── */
        private async Task OnRadioButtonListChanged()
        {
            if (selectedTtsId.HasValue)
            {
                await OnTtsSelectionChanged(selectedTtsId.Value);
            }
        }

        private async Task OnTtsSelectionChanged(ulong ttsId)
        {
            if (Channel == null || isProcessingSelection)
                return;

            try
            {
                isProcessingSelection = true;

                // 기존 매핑 삭제
                if (currentChannelTts != null)
                {
                    await WicsService.DeleteMapChannelTt(currentChannelTts.Id);
                    channelTtsCache.Remove(Channel.Id);
                }

                // 새 매핑 추가
                var newMap = new WicsPlatform.Server.Models.wics.MapChannelTt
                {
                    ChannelId = Channel.Id,
                    TtsId = ttsId,
                    DeleteYn = "N",
                    CreatedAt = DateTime.Now,
                    UpdatedAt = DateTime.Now
                };

                currentChannelTts = await WicsService.CreateMapChannelTt(newMap);
                channelTtsCache[Channel.Id] = currentChannelTts;

                NotifySuccess("TTS가 선택되었습니다.");
                StateHasChanged();
            }
            catch (Exception ex)
            {
                NotifyError("TTS 선택 처리 중 오류가 발생했습니다", ex);
            }
            finally
            {
                isProcessingSelection = false;
            }
        }

        private async Task ClearTtsSelection()
        {
            if (Channel == null || currentChannelTts == null)
                return;

            try
            {
                isProcessingSelection = true;

                await WicsService.DeleteMapChannelTt(currentChannelTts.Id);
                channelTtsCache.Remove(Channel.Id);

                currentChannelTts = null;
                selectedTtsId = null;

                NotifyInfo("TTS 선택이 해제되었습니다.");
                StateHasChanged();
            }
            catch (Exception ex)
            {
                NotifyError("TTS 선택 해제 중 오류가 발생했습니다", ex);
            }
            finally
            {
                isProcessingSelection = false;
            }
        }

        /* ────────────────────── [재생 / 중지] ────────────────────── */
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
                await LoadTts();          // 리스트 즉시 갱신
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
                await LoadTts();
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
                if (selectedTtsId == tts.Id)
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
                    await LoadTts();
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
