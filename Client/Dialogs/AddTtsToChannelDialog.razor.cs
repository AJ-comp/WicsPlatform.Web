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

namespace WicsPlatform.Client.Dialogs
{
    public partial class AddTtsToChannelDialog
    {
        [Inject]
        protected IJSRuntime JSRuntime { get; set; }

        [Inject]
        protected DialogService DialogService { get; set; }

        [Inject]
        protected NotificationService NotificationService { get; set; }

        [Inject]
        protected HttpClient Http { get; set; }

        [Inject]
        protected wicsService WicsService { get; set; }

        [Parameter]
        public IEnumerable<WicsPlatform.Server.Models.wics.Tt> SelectedTts { get; set; }

        protected IEnumerable<WicsPlatform.Server.Models.wics.Tt> selectedTts => SelectedTts ?? new List<WicsPlatform.Server.Models.wics.Tt>();
        protected IEnumerable<ChannelViewModel> channels;
        protected ulong selectedChannelId;
        protected string error;
        protected bool errorVisible;
        protected bool isLoading = true;
        protected bool isProcessing = false;

        protected override async Task OnInitializedAsync()
        {
            await LoadChannels();
        }

        protected async Task LoadChannels()
        {
            try
            {
                isLoading = true;

                var query = new Query
                {
                    Filter = "DeleteYn eq 'N' or DeleteYn eq null"
                };

                var result = await WicsService.GetChannels(query);

                if (result != null && result.Value != null)
                {
                    channels = result.Value.Select(c => new ChannelViewModel
                    {
                        Id = c.Id,
                        Name = c.Name,
                        Description = c.Description
                    }).ToList();

                    if (channels.Any())
                    {
                        selectedChannelId = channels.First().Id;
                    }
                }
            }
            catch (Exception ex)
            {
                errorVisible = true;
                error = $"채널 목록을 불러오는 중 오류가 발생했습니다: {ex.Message}";
            }
            finally
            {
                isLoading = false;
            }
        }

        protected async Task AddToChannel()
        {
            if (selectedChannelId == 0 || !selectedTts.Any())
            {
                errorVisible = true;
                error = "채널을 선택하고 TTS가 선택되어 있어야 합니다.";
                return;
            }

            try
            {
                isProcessing = true;
                errorVisible = false;

                int successCount = 0;
                int failCount = 0;

                // 선택한 각 TTS를 채널에 매핑
                foreach (var tts in selectedTts)
                {
                    try
                    {
                        var map = new WicsPlatform.Server.Models.wics.MapChannelTt
                        {
                            TtsId = tts.Id,
                            ChannelId = selectedChannelId,
                            CreatedAt = DateTime.Now,
                            UpdatedAt = DateTime.Now
                        };

                        var response = await Http.PostAsJsonAsync("odata/wics/MapChannelTts", map);

                        if (response.IsSuccessStatusCode)
                        {
                            successCount++;
                        }
                        else
                        {
                            failCount++;
                        }
                    }
                    catch
                    {
                        failCount++;
                    }
                }

                // 결과 알림 표시
                if (successCount > 0)
                {
                    var channelName = channels.FirstOrDefault(c => c.Id == selectedChannelId)?.Name;
                    NotificationService.Notify(new NotificationMessage
                    {
                        Severity = NotificationSeverity.Success,
                        Summary = "추가 완료",
                        Detail = $"{successCount}개의 TTS가 '{channelName}' 채널에 추가되었습니다." +
                                (failCount > 0 ? $" ({failCount}개 실패)" : ""),
                        Duration = 4000
                    });

                    // 다이얼로그 닫기 (true를 반환하여 변경이 있었음을 알림)
                    DialogService.Close(true);
                }
                else if (failCount > 0)
                {
                    errorVisible = true;
                    error = "모든 TTS를 채널에 추가하는데 실패했습니다.";
                }
            }
            catch (Exception ex)
            {
                errorVisible = true;
                error = $"채널에 추가하는 중 오류가 발생했습니다: {ex.Message}";
            }
            finally
            {
                isProcessing = false;
            }
        }

        protected async Task CancelClick()
        {
            DialogService.Close(null);
        }
    }
}
