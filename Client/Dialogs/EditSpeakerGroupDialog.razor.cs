using System;
using System.Threading.Tasks;
using Microsoft.JSInterop;
using Microsoft.AspNetCore.Components;
using Radzen;
using System.ComponentModel.DataAnnotations;
using System.Net.Http;
using System.Net.Http.Json;
using System.Linq;

namespace WicsPlatform.Client.Dialogs
{
    public partial class EditSpeakerGroupDialog
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
        public ulong GroupId { get; set; }

        protected SpeakerGroupFormModel model = new SpeakerGroupFormModel();
        protected string error;
        protected bool errorVisible;
        protected bool isProcessing = false;

        protected override async Task OnInitializedAsync()
        {
            await LoadGroupData();
        }

        protected async Task LoadGroupData()
        {
            try
            {
                isProcessing = true;

                // wicsService를 사용하여 그룹 데이터 가져오기
                var query = new Radzen.Query
                {
                    Filter = $"Id eq {GroupId}"
                };

                var result = await WicsService.GetGroups(query);
                var group = result.Value.AsODataEnumerable().FirstOrDefault();

                if (group != null)
                {
                    // 모델에 데이터 설정
                    model.GroupName = group.Name;
                    model.Description = group.Description;
                }
                else
                {
                    errorVisible = true;
                    error = "그룹 정보를 불러올 수 없습니다.";
                }
            }
            catch (Exception ex)
            {
                errorVisible = true;
                error = $"그룹 정보를 불러오는 중 오류가 발생했습니다: {ex.Message}";
            }
            finally
            {
                isProcessing = false;
            }
        }

        protected async Task FormSubmit()
        {
            try
            {
                isProcessing = true;
                errorVisible = false;

                if (string.IsNullOrWhiteSpace(model.GroupName))
                {
                    errorVisible = true;
                    error = "그룹명은 필수 항목입니다.";
                    isProcessing = false;
                    return;
                }

                // 서버로 전송할 그룹 데이터 생성
                var group = new UpdateGroupRequest
                {
                    Name = model.GroupName,
                    Description = model.Description ?? "",
                    UpdatedAt = DateTime.Now
                };

                // API 호출하여 그룹 업데이트 (PATCH 메서드 사용)
                var response = await Http.PatchAsJsonAsync($"odata/wics/Groups({GroupId})", group);

                // 응답 확인
                if (response.IsSuccessStatusCode)
                {
                    // 성공 알림 표시
                    NotificationService.Notify(new NotificationMessage
                    {
                        Severity = NotificationSeverity.Success,
                        Summary = "그룹 수정 성공",
                        Detail = $"'{model.GroupName}' 그룹이 성공적으로 수정되었습니다.",
                        Duration = 4000
                    });

                    // 다이얼로그 닫기 및 데이터 반환
                    DialogService.Close(true);
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    errorVisible = true;
                    error = $"그룹 수정 중 오류가 발생했습니다: {errorContent}";
                }
            }
            catch (Exception ex)
            {
                errorVisible = true;
                error = $"그룹 수정 중 오류가 발생했습니다: {ex.Message}";
            }
            finally
            {
                isProcessing = false;
            }
        }

        protected async Task CancelClick()
        {
            await Task.Delay(100);
            DialogService.Close(null);
        }

        // 그룹 수정 요청 모델
        public class UpdateGroupRequest
        {
            public string Name { get; set; }
            public string Description { get; set; }
            public DateTime UpdatedAt { get; set; }
        }

        // 폼 모델 정의
        public class SpeakerGroupFormModel
        {
            [Required(ErrorMessage = "그룹명은 필수입니다.")]
            [StringLength(100, ErrorMessage = "그룹명은 100자 이내로 입력해주세요.")]
            public string GroupName { get; set; }

            [StringLength(500, ErrorMessage = "설명은 500자 이내로 입력해주세요.")]
            public string Description { get; set; }
        }
    }
}
