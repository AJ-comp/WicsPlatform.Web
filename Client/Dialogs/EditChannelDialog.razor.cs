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
    public partial class EditChannelDialog
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

        protected ChannelFormModel model = new ChannelFormModel();
        protected string error;
        protected bool errorVisible;
        protected bool isProcessing = false;

        protected override async Task OnInitializedAsync()
        {
            await LoadChannelData();
        }

        protected async Task LoadChannelData()
        {
            try
            {
                isProcessing = true;

                // 채널 데이터 가져오기
                var channel = await Http.GetFromJsonAsync<ChannelData>($"odata/wics/Channels(Id={ChannelId})");

                if (channel != null)
                {
                    // 모델에 데이터 설정
                    model.Name = channel.Name;
                    model.Description = channel.Description;
                }
                else
                {
                    errorVisible = true;
                    error = "채널 정보를 불러올 수 없습니다.";
                }
            }
            catch (Exception ex)
            {
                errorVisible = true;
                error = $"채널 정보를 불러오는 중 오류가 발생했습니다: {ex.Message}";
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

                // 폼 유효성 검사
                if (string.IsNullOrWhiteSpace(model.Name))
                {
                    errorVisible = true;
                    error = "채널명은 필수 항목입니다.";
                    isProcessing = false;
                    return;
                }

                // 서버로 전송할 채널 데이터 생성
                var channel = new UpdateChannelRequest
                {
                    Name = model.Name,
                    Description = model.Description ?? "",
                    UpdatedAt = DateTime.UtcNow  // UTC 시간으로 설정
                };

                // API 호출하여 채널 업데이트 (PATCH 메서드 사용)
                var response = await Http.PatchAsJsonAsync($"odata/wics/Channels(Id={ChannelId})", channel);

                // 응답 확인
                if (response.IsSuccessStatusCode)
                {
                    // 성공 알림 표시
                    NotificationService.Notify(new NotificationMessage
                    {
                        Severity = NotificationSeverity.Success,
                        Summary = "채널 수정 성공",
                        Detail = $"'{model.Name}' 채널이 성공적으로 수정되었습니다.",
                        Duration = 4000
                    });

                    // 다이얼로그 닫기 및 데이터 반환
                    DialogService.Close(true);
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    errorVisible = true;
                    error = $"채널 수정 중 오류가 발생했습니다: {errorContent}";
                }
            }
            catch (Exception ex)
            {
                errorVisible = true;
                error = $"채널 수정 중 오류가 발생했습니다: {ex.Message}";
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

    // 채널 수정 요청 모델
    public class UpdateChannelRequest
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public DateTime UpdatedAt { get; set; }  // UpdatedAt 필드 추가
    }

    // 채널 데이터 응답 모델
    public class ChannelData
    {
        public ulong Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public sbyte State { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
