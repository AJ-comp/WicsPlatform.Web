using System;
using System.Threading.Tasks;
using Microsoft.JSInterop;
using Microsoft.AspNetCore.Components;
using Radzen;
using System.ComponentModel.DataAnnotations;
using System.Net.Http;
using System.Net.Http.Json;

namespace WicsPlatform.Client.Dialogs
{
    public partial class AddSpeakerGroupDialog
    {
        [Inject]
        protected IJSRuntime JSRuntime { get; set; }

        [Inject]
        protected DialogService DialogService { get; set; }

        [Inject]
        protected NotificationService NotificationService { get; set; }

        [Inject]
        protected HttpClient Http { get; set; }

        protected SpeakerGroupFormModel model = new SpeakerGroupFormModel();
        protected string error;
        protected bool errorVisible;
        protected bool isProcessing = false;

        protected override void OnInitialized()
        {
            // 필요시 초기화
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

                // 서버 Group 모델에 맞게 데이터 생성
                var group = new
                {
                    Name = model.GroupName,
                    Description = model.Description ?? "",
                    DeleteYn = "N",
                    CreatedAt = DateTime.Now,
                    UpdatedAt = DateTime.Now
                };

                var response = await Http.PostAsJsonAsync("odata/wics/Groups", group);

                if (response.IsSuccessStatusCode)
                {
                    NotificationService.Notify(new NotificationMessage
                    {
                        Severity = NotificationSeverity.Success,
                        Summary = "그룹 생성 성공",
                        Detail = $"'{model.GroupName}' 그룹이 성공적으로 생성되었습니다.",
                        Duration = 4000
                    });

                    DialogService.Close(true);
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    errorVisible = true;
                    error = $"그룹 생성 중 오류가 발생했습니다: {errorContent}";
                }
            }
            catch (Exception ex)
            {
                errorVisible = true;
                error = $"그룹 생성 중 오류가 발생했습니다: {ex.Message}";
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
