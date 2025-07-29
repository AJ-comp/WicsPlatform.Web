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
    public partial class AddSpeakerDialog
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

        protected SpeakerFormModel model = new SpeakerFormModel();
        protected IEnumerable<ChannelViewModel> channels;
        protected string error;
        protected bool errorVisible;
        protected bool isProcessing = false;

        protected override async Task OnInitializedAsync()
        {
            await LoadChannels();
        }

        protected async Task LoadChannels()
        {
            try
            {
                // OData를 사용하여 활성 채널 목록 가져오기
                var response = await Http.GetFromJsonAsync<ODataResponse<ChannelViewModel>>("/odata/wics/Channels");
                if (response != null && response.Value != null)
                {
                    channels = response.Value;
                }
            }
            catch (Exception ex)
            {
                errorVisible = true;
                error = $"채널 목록을 불러오는 중 오류가 발생했습니다: {ex.Message}";
            }
        }

        // IP 주소 중복 확인 메서드
        protected async Task<bool> IsIpAddressExists(string ipAddress)
        {
            try
            {
                // OData를 사용하여 해당 IP 주소의 스피커가 존재하는지 검사
                var response = await Http.GetFromJsonAsync<ODataResponse<SpeakerViewModel>>(
                    $"/odata/wics/Speakers?$filter=Ip eq '{ipAddress}'&$count=true");

                // 결과가 있으면 IP 주소가 이미 존재함
                return response != null && response.Value != null && response.Value.Any();
            }
            catch (Exception ex)
            {
                // 오류 발생 시 로그를 남기고 기본값 반환
                Console.WriteLine($"IP 주소 중복 확인 중 오류: {ex.Message}");
                return false;
            }
        }

        protected async Task FormSubmit()
        {
            try
            {
                isProcessing = true;
                errorVisible = false;

                // IP 주소 중복 확인
                bool ipExists = await IsIpAddressExists(model.Ip);
                if (ipExists)
                {
                    errorVisible = true;
                    error = $"IP 주소 '{model.Ip}'는 이미 등록된 스피커가 사용 중입니다. 다른 IP 주소를 입력해주세요.";
                    isProcessing = false;

                    // 오류 메시지가 표시될 때 스크롤을 맨 위로 이동
                    await JSRuntime.InvokeVoidAsync("scrollToTop");
                    return; // 중복된 IP 주소면 여기서 중단
                }

                // 서버로 전송할 스피커 데이터 생성
                var speaker = new CreateSpeakerRequest
                {
                    Name = model.Name,
                    Ip = model.Ip,
                    Model = model.Model,
                    Password = model.Password,
                    Location = model.Location,
                    State = 0, // 기본 상태 (오프라인)
                    CreatedAt = DateTime.Now,
                    UpdatedAt = DateTime.Now
                };

                // API 호출하여 스피커 생성
                var response = await Http.PostAsJsonAsync("odata/wics/Speakers", speaker);

                // 응답 확인
                if (response.IsSuccessStatusCode)
                {
                    // 성공 알림 표시
                    NotificationService.Notify(new NotificationMessage
                    {
                        Severity = NotificationSeverity.Success,
                        Summary = "스피커 추가 성공",
                        Detail = $"'{model.Name}' 스피커가 성공적으로 추가되었습니다.",
                        Duration = 4000
                    });

                    // 다이얼로그 닫기 및 데이터 반환
                    DialogService.Close(true);
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    errorVisible = true;
                    error = $"스피커 추가 중 오류가 발생했습니다: {errorContent}";

                    // 오류 메시지가 표시될 때 스크롤을 맨 위로 이동
                    await JSRuntime.InvokeVoidAsync("scrollToTop");
                }
            }
            catch (Exception ex)
            {
                errorVisible = true;
                error = $"스피커 추가 중 오류가 발생했습니다: {ex.Message}";

                // 오류 메시지가 표시될 때 스크롤을 맨 위로 이동
                await JSRuntime.InvokeVoidAsync("scrollToTop");
            }
            finally
            {
                isProcessing = false;
            }
        }

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            if (firstRender)
            {
                // 스크롤을 맨 위로 이동하는 JavaScript 함수 정의
                await JSRuntime.InvokeVoidAsync("eval", @"
                    window.scrollToTop = function() {
                        document.querySelector('.rz-dialog-content').scrollTop = 0;
                    }
                ");
            }
        }

        protected async Task CancelClick()
        {
            await Task.Delay(100);
            DialogService.Close(null);
        }
    }

    // OData 응답용 헬퍼 클래스
    public class ODataResponse<T>
    {
        public List<T> Value { get; set; }
        public int Count { get; set; }
    }

    // 채널 목록용 뷰 모델
    public class ChannelViewModel
    {
        public ulong Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
    }

    // 스피커 뷰 모델 (IP 중복 검사용)
    public class SpeakerViewModel
    {
        public ulong Id { get; set; }
        public string Name { get; set; }
        public string Ip { get; set; }
    }

    // 스피커 생성 요청 모델
    public class CreateSpeakerRequest
    {
        public string Name { get; set; }
        public string Ip { get; set; }
        public string Model { get; set; }
        public string Password { get; set; }
        public string Location { get; set; }
        public byte State { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    // 스피커 생성/수정 폼 모델
    public class SpeakerFormModel
    {
        [Required(ErrorMessage = "스피커명은 필수입니다.")]
        [StringLength(100, ErrorMessage = "스피커명은 100자 이내로 입력해주세요.")]
        public string Name { get; set; }

        [Required(ErrorMessage = "IP 주소는 필수입니다.")]
        [StringLength(15, ErrorMessage = "올바른 IP 주소 형식이 아닙니다.")]
        public string Ip { get; set; }

        [Required(ErrorMessage = "모델명은 필수입니다.")]
        [StringLength(100, ErrorMessage = "모델명은 100자 이내로 입력해주세요.")]
        public string Model { get; set; }

        [Required(ErrorMessage = "비밀번호는 필수입니다.")]
        [StringLength(100, ErrorMessage = "비밀번호는 100자 이내로 입력해주세요.")]
        public string Password { get; set; }

        [Required(ErrorMessage = "설치 위치는 필수입니다.")]
        [StringLength(200, ErrorMessage = "설치 위치는 200자 이내로 입력해주세요.")]
        public string Location { get; set; }

        public ulong? GroupId { get; set; }

        public ulong? ChannelId { get; set; }
    }
}
