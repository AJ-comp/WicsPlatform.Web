using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.JSInterop;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Radzen;
using Radzen.Blazor;
using System.Net.Http;
using System.Net.Http.Json;
using WicsPlatform.Client.Models;
using WicsPlatform.Client.Dialogs;

namespace WicsPlatform.Client.Pages
{
    public partial class ManageTts
    {
        [Inject]
        protected IJSRuntime JSRuntime { get; set; }

        [Inject]
        protected NavigationManager NavigationManager { get; set; }

        [Inject]
        protected DialogService DialogService { get; set; }

        [Inject]
        protected TooltipService TooltipService { get; set; }

        [Inject]
        protected ContextMenuService ContextMenuService { get; set; }

        [Inject]
        protected NotificationService NotificationService { get; set; }

        [Inject]
        protected SecurityService Security { get; set; }

        [Inject]
        protected HttpClient Http { get; set; }

        [Inject]
        protected wicsService WicsService { get; set; }

        protected RadzenDataGrid<WicsPlatform.Server.Models.wics.Tt> ttsGrid;
        protected IEnumerable<WicsPlatform.Server.Models.wics.Tt> ttsList;
        protected IList<WicsPlatform.Server.Models.wics.Tt> selectedTts = new List<WicsPlatform.Server.Models.wics.Tt>();
        protected bool selectAllChecked = false;
        protected IEnumerable<WicsPlatform.Server.Models.wics.Tt> filteredTtsList;
        protected bool isLoading = true;

        // 검색 필터
        protected string searchFilter = "";
        protected DateTime? dateFilter = null;

        protected override async Task OnInitializedAsync()
        {
            await LoadTts();
        }

        protected async Task LoadTts()
        {
            try
            {
                isLoading = true;

                var result = await WicsService.GetTts(new Query
                {
                    Filter = "DeleteYn eq 'N'",
                    OrderBy = "CreatedAt desc"
                });

                if (result != null)
                {
                    ttsList = result.Value;
                    filteredTtsList = ttsList;
                    ApplyFilters();
                }
            }
            catch (Exception ex)
            {
                NotificationService.Notify(new NotificationMessage
                {
                    Severity = NotificationSeverity.Error,
                    Summary = "오류",
                    Detail = $"TTS 목록을 가져오는 중 오류가 발생했습니다: {ex.Message}",
                    Duration = 4000
                });
            }
            finally
            {
                isLoading = false;
            }
        }

        protected void ApplyFilters()
        {
            if (ttsList == null) return;

            filteredTtsList = ttsList;

            // 검색 필터
            if (!string.IsNullOrWhiteSpace(searchFilter))
            {
                filteredTtsList = filteredTtsList.Where(t =>
                    t.Name.Contains(searchFilter, StringComparison.OrdinalIgnoreCase) ||
                    t.Content.Contains(searchFilter, StringComparison.OrdinalIgnoreCase));
            }

            // 날짜 필터
            if (dateFilter.HasValue)
            {
                var startDate = dateFilter.Value.Date;
                var endDate = startDate.AddDays(1);
                filteredTtsList = filteredTtsList.Where(t => t.CreatedAt >= startDate && t.CreatedAt < endDate);
            }

            // 그리드 업데이트
            ttsGrid?.Reload();
        }

        protected void ResetFilters()
        {
            searchFilter = "";
            dateFilter = null;
            ApplyFilters();
        }

        // 선택 여부 확인
        protected bool IsTtsSelected(WicsPlatform.Server.Models.wics.Tt tts)
        {
            return selectedTts != null && selectedTts.Any(t => t.Id == tts.Id);
        }

        protected void TtsSelectionChanged(bool selected, WicsPlatform.Server.Models.wics.Tt tts)
        {
            if (selected)
            {
                if (!IsTtsSelected(tts))
                {
                    ((List<WicsPlatform.Server.Models.wics.Tt>)selectedTts).Add(tts);
                }
            }
            else
            {
                if (IsTtsSelected(tts))
                {
                    ((List<WicsPlatform.Server.Models.wics.Tt>)selectedTts).RemoveAll(t => t.Id == tts.Id);
                }
            }

            UpdateSelectAllCheckbox();
        }

        protected void SelectAllTtsChanged(bool selected)
        {
            if (selected)
            {
                selectedTts = ttsList.ToList();
            }
            else
            {
                selectedTts.Clear();
            }

            StateHasChanged();
        }

        protected void UpdateSelectAllCheckbox()
        {
            if (ttsList != null && selectedTts != null)
            {
                selectAllChecked = ttsList.Count() > 0 && selectedTts.Count() == ttsList.Count();
            }
            else
            {
                selectAllChecked = false;
            }
        }

        protected async Task OpenAddTtsDialog()
        {
            var result = await DialogService.OpenAsync<Dialogs.AddTtsDialog>("새 TTS 추가",
                null,
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

        protected async Task OpenEditTtsDialog(WicsPlatform.Server.Models.wics.Tt tts)
        {
            var parameters = new Dictionary<string, object>
            {
                { "TtsId", tts.Id }
            };

            var result = await DialogService.OpenAsync<Dialogs.EditTtsDialog>("TTS 편집",
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
                // 소프트 삭제를 위한 업데이트 요청
                var softDeleteRequest = new
                {
                    DeleteYn = "Y",
                    UpdatedAt = DateTime.UtcNow
                };

                // PATCH 메서드를 사용해 DeleteYn 필드만 업데이트
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
                        Detail = "TTS 삭제 중 문제가 발생했습니다.",
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

        protected void ViewTtsDetails(WicsPlatform.Server.Models.wics.Tt tts)
        {
            // TTS의 상세 정보 (필요 시 구현)
        }

        protected async Task PlayTts(WicsPlatform.Server.Models.wics.Tt tts)
        {
            try
            {
                // TTS 재생 테스트
                // 현재 브라우저에서 TTS 플레이백을 실행합니다
                NotificationService.Notify(new NotificationMessage
                {
                    Severity = NotificationSeverity.Info,
                    Summary = "TTS 재생",
                    Detail = $"'{tts.Name}' TTS를 재생합니다.",
                    Duration = 3000
                });

                // JavaScript 함수를 호출하여 Web Speech API 활용 (필요 시 구현)
                await JSRuntime.InvokeVoidAsync("playTTS", tts.Content);
            }
            catch (Exception ex)
            {
                NotificationService.Notify(new NotificationMessage
                {
                    Severity = NotificationSeverity.Error,
                    Summary = "재생 오류",
                    Detail = $"TTS 재생 중 오류가 발생했습니다: {ex.Message}",
                    Duration = 4000
                });
            }
        }


        protected async Task OpenAddTtsToChannelDialog()
        {
            if (selectedTts == null || !selectedTts.Any())
            {
                NotificationService.Notify(new NotificationMessage
                {
                    Severity = NotificationSeverity.Warning,
                    Summary = "선택 필요",
                    Detail = "채널에 추가할 TTS를 선택해주세요.",
                    Duration = 4000
                });
                return;
            }

            var result = await DialogService.OpenAsync<AddTtsToChannelDialog>("채널에 TTS 추가",
                new Dictionary<string, object>
                {
                    { "SelectedTts", selectedTts }
                },
                new DialogOptions()
                {
                    Width = "600px",
                    Height = "auto",
                    Resizable = false,
                    Draggable = false
                });

            if (result == true)
            {
                selectedTts.Clear();
                selectAllChecked = false;
                await LoadTts();
            }
        }
    }
}
