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
    public partial class AddSpeakerToChannelDialog
    {
        [Inject]
        protected IJSRuntime JSRuntime { get; set; }

        [Inject]
        protected DialogService DialogService { get; set; }

        [Inject]
        protected NotificationService NotificationService { get; set; }

        [Inject]
        protected HttpClient Http { get; set; }

        [Parameter]
        public ulong ChannelId { get; set; }

        [Parameter]
        public string ChannelName { get; set; }

        protected RadzenDataGrid<SpeakerModel> speakersGrid;
        protected IEnumerable<SpeakerModel> unassignedSpeakers;
        protected IList<SpeakerModel> selectedSpeakers = new List<SpeakerModel>();
        protected bool selectAllChecked = false;
        protected bool isLoading = true;
        protected bool isProcessing = false;
        protected bool errorVisible = false;
        protected string error;

        protected override async Task OnInitializedAsync()
        {
            await LoadUnassignedSpeakers();
        }

        protected async Task LoadUnassignedSpeakers()
        {
            try
            {
                isLoading = true;
                // 아직 채널에 할당되지 않은 스피커 목록을 로드
                var response = await Http.GetFromJsonAsync<ODataResponse<SpeakerModel>>("/odata/wics/Speakers?$filter=ChannelId eq null");
                if (response != null && response.Value != null)
                {
                    unassignedSpeakers = response.Value;
                }
            }
            catch (Exception ex)
            {
                errorVisible = true;
                error = $"스피커 목록을 불러오는 중 오류가 발생했습니다: {ex.Message}";
            }
            finally
            {
                isLoading = false;
            }
        }

        // OData 응답용 헬퍼 클래스
        public class ODataResponse<T>
        {
            public List<T> Value { get; set; }
        }

        // 스피커 모델 클래스
        public class SpeakerModel
        {
            public ulong Id { get; set; }
            public string Name { get; set; }
            public string Ip { get; set; }
            public string Location { get; set; }
            public byte State { get; set; }
            public ulong? ChannelId { get; set; }

            public string Status => State == 0 ? "오프라인" : "온라인";
        }

        // 기기 상태에 따른 뱃지 스타일 결정
        protected BadgeStyle GetDeviceStatusBadgeStyle(string status)
        {
            return status switch
            {
                "온라인" => BadgeStyle.Success,
                "오프라인" => BadgeStyle.Danger,
                _ => BadgeStyle.Light
            };
        }

        // 선택한 스피커 확인
        protected bool IsSpeakerSelected(SpeakerModel speaker)
        {
            return selectedSpeakers != null && selectedSpeakers.Any(s => s.Id == speaker.Id);
        }

        // 특정 스피커의 선택 상태 변경
        protected void SpeakerSelectionChanged(bool selected, SpeakerModel speaker)
        {
            if (selected)
            {
                if (!IsSpeakerSelected(speaker))
                {
                    ((List<SpeakerModel>)selectedSpeakers).Add(speaker);
                }
            }
            else
            {
                if (IsSpeakerSelected(speaker))
                {
                    ((List<SpeakerModel>)selectedSpeakers).RemoveAll(s => s.Id == speaker.Id);
                }
            }

            // 전체 선택 체크박스 상태 업데이트
            UpdateSelectAllCheckbox();
        }

        // 전체 선택 체크박스 상태 변경
        protected void SelectAllSpeakersChanged(bool selected)
        {
            if (selected)
            {
                selectedSpeakers = unassignedSpeakers.ToList();
            }
            else
            {
                selectedSpeakers.Clear();
            }

            // 데이터그리드 상태 업데이트
            speakersGrid.Reload();
        }

        // 전체 선택 체크박스 상태 업데이트
        protected void UpdateSelectAllCheckbox()
        {
            if (unassignedSpeakers != null && selectedSpeakers != null)
            {
                selectAllChecked = unassignedSpeakers.Count() > 0 && selectedSpeakers.Count() == unassignedSpeakers.Count();
            }
            else
            {
                selectAllChecked = false;
            }
        }

        // 선택한 스피커를 채널에 할당
        protected async Task AssignSpeakers()
        {
            if (selectedSpeakers == null || !selectedSpeakers.Any())
            {
                NotificationService.Notify(new NotificationMessage
                {
                    Severity = NotificationSeverity.Warning,
                    Summary = "선택 필요",
                    Detail = "채널에 연결할 스피커를 하나 이상 선택해주세요.",
                    Duration = 4000
                });
                return;
            }

            try
            {
                isProcessing = true;
                errorVisible = false;

                // 처리 중 알림 표시
                NotificationService.Notify(new NotificationMessage
                {
                    Severity = NotificationSeverity.Info,
                    Summary = "처리 중",
                    Detail = "스피커를 채널에 연결하는 중입니다...",
                    Duration = 2000
                });

                bool anyFailed = false;

                // 선택한 모든 스피커의 ChannelId 업데이트
                foreach (var speaker in selectedSpeakers)
                {
                    try
                    {
                        // OData Delta 형식으로 PATCH 요청 
                        var patchData = new
                        {
                            ChannelId = ChannelId
                        };

                        // PATCH 요청 전송
                        var patchResponse = await Http.PatchAsJsonAsync($"/odata/wics/Speakers(Id={speaker.Id})", patchData);

                        if (!patchResponse.IsSuccessStatusCode)
                        {
                            anyFailed = true;
                            var responseContent = await patchResponse.Content.ReadAsStringAsync();
                            NotificationService.Notify(new NotificationMessage
                            {
                                Severity = NotificationSeverity.Warning,
                                Summary = "일부 스피커 연결 실패",
                                Detail = $"'{speaker.Name}' 스피커 연결 실패: {responseContent}",
                                Duration = 4000
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        anyFailed = true;
                        NotificationService.Notify(new NotificationMessage
                        {
                            Severity = NotificationSeverity.Warning,
                            Summary = "일부 스피커 연결 실패",
                            Detail = $"'{speaker.Name}' 스피커 연결 중 오류: {ex.Message}",
                            Duration = 4000
                        });
                    }
                }

                // 결과 알림 표시
                if (!anyFailed)
                {
                    NotificationService.Notify(new NotificationMessage
                    {
                        Severity = NotificationSeverity.Success,
                        Summary = "연결 완료",
                        Detail = $"{selectedSpeakers.Count()}개의 스피커가 '{ChannelName}' 채널에 성공적으로 연결되었습니다.",
                        Duration = 4000
                    });

                    // 다이얼로그 닫기 (true를 반환하여 변경이 있었음을 알림)
                    DialogService.Close(true);
                }
                else
                {
                    NotificationService.Notify(new NotificationMessage
                    {
                        Severity = NotificationSeverity.Info,
                        Summary = "일부 연결 완료",
                        Detail = "일부 스피커가 성공적으로 연결되었으나, 일부는 실패했습니다.",
                        Duration = 4000
                    });
                }
            }
            catch (Exception ex)
            {
                errorVisible = true;
                error = $"스피커 연결 중 오류가 발생했습니다: {ex.Message}";

                NotificationService.Notify(new NotificationMessage
                {
                    Severity = NotificationSeverity.Error,
                    Summary = "오류",
                    Detail = $"스피커 연결 중 예외가 발생했습니다: {ex.Message}",
                    Duration = 6000
                });
            }
            finally
            {
                isProcessing = false;
            }
        }

        // 취소 버튼 클릭
        protected async Task CancelClick()
        {
            DialogService.Close(null);
        }
    }
}
