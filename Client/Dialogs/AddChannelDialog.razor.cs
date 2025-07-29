using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.JSInterop;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Radzen;
using Radzen.Blazor;
using System.ComponentModel.DataAnnotations;
using System.Net.Http;
using System.Net.Http.Json;

namespace WicsPlatform.Client.Dialogs
{
    public partial class AddChannelDialog
    {
        [Inject]
        protected IJSRuntime JSRuntime { get; set; }

        [Inject]
        protected DialogService DialogService { get; set; }

        [Inject]
        protected NotificationService NotificationService { get; set; }

        [Inject]
        protected HttpClient Http { get; set; }

        protected ChannelFormModel model = new ChannelFormModel();
        protected string error;
        protected bool errorVisible;
        protected bool isProcessing = false;

        protected override void OnInitialized()
        {
            // 초기화 로직이 필요하면 여기에 추가
        }

        protected async Task FormSubmit()
        {
            try
            {
                isProcessing = true;
                errorVisible = false;

                // 폼 유효성 검사
                if (string.IsNullOrWhiteSpace(model.Name))
                {
                    errorVisible = true;
                    error = "채널명은 필수 항목입니다.";
                    isProcessing = false;
                    return;
                }

                // 서버로 전송할 채널 데이터 생성
                var channel = new CreateChannelRequest
                {
                    Name = model.Name,
                    Description = model.Description ?? "",
                    Type = 0, // 기본 타입 설정
                    CreatedAt = DateTime.Now
                };

                // API 호출하여 채널 생성
                var response = await Http.PostAsJsonAsync("odata/wics/Channels", channel);

                // 응답 확인
                if (response.IsSuccessStatusCode)
                {
                    // 성공 알림 표시
                    NotificationService.Notify(new NotificationMessage
                    {
                        Severity = NotificationSeverity.Success,
                        Summary = "채널 생성 성공",
                        Detail = $"'{model.Name}' 채널이 성공적으로 생성되었습니다.",
                        Duration = 4000
                    });

                    // 다이얼로그 닫기 및 데이터 반환
                    DialogService.Close(true);
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    errorVisible = true;
                    error = $"채널 생성 중 오류가 발생했습니다: {errorContent}";
                }
            }
            catch (Exception ex)
            {
                errorVisible = true;
                error = $"채널 생성 중 오류가 발생했습니다: {ex.Message}";
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
    }

    // 채널 생성 요청 모델
    public class CreateChannelRequest
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public byte Type { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    // 채널 생성/수정 폼 모델
    public class ChannelFormModel
    {
        [Required(ErrorMessage = "채널 이름은 필수입니다.")]
        [StringLength(100, ErrorMessage = "채널 이름은 100자 이내로 입력해주세요.")]
        public string Name { get; set; }

        [StringLength(500, ErrorMessage = "설명은 500자 이내로 입력해주세요.")]
        public string Description { get; set; }

        public string Status { get; set; }
    }
}
