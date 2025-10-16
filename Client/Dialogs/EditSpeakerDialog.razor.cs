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
using System.Text.Json.Serialization;
using System.Text.Json;

namespace WicsPlatform.Client.Dialogs
{
    public partial class EditSpeakerDialog
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
        public ulong SpeakerId { get; set; }

        protected EditSpeakerFormModel model = new EditSpeakerFormModel();
        protected WicsPlatform.Server.Models.wics.Speaker originalSpeaker;
        // 채널 관련 필드 제거
        // protected IEnumerable<ChannelViewModel> channels;
        protected string error;
        protected bool errorVisible;
        protected bool isProcessing = false;
        protected bool isLoadingData = true;
        protected string originalIpAddress = "";

        // 상태 옵션
        protected List<StateOption> stateOptions = new List<StateOption>
        {
            new StateOption { Value = 0, Text = "오프라인" },
            new StateOption { Value = 1, Text = "온라인" }
        };

        protected override async Task OnInitializedAsync()
        {
            // 채널 목록 로드는 제거 (Speaker 모델에 ChannelId가 없음)
            // await LoadChannels();
            await LoadSpeakerData();
        }

        // 채널 목록 로드는 Speaker 모델에 ChannelId가 없으므로 주석 처리
        /*
        protected async Task LoadChannels()
        {
            try
            {
                // OData를 사용하여 활성 채널 목록 가져오기
                var response = await Http.GetFromJsonAsync<ODataResponse<ChannelViewModel>>(
                    "/odata/wics/Channels?$filter=DeleteYn eq 'N' or DeleteYn eq null");
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
        */

        protected async Task LoadSpeakerData()
        {
            try
            {
                isLoadingData = true;

                // OData를 사용하여 스피커 데이터 가져오기
                var response = await Http.GetFromJsonAsync<WicsPlatform.Server.Models.wics.Speaker>(
                                    $"/odata/wics/Speakers({SpeakerId})");

                if (response != null)
                {
                    originalSpeaker = response;

                    // 모델에 데이터 설정
                    model.Name = originalSpeaker.Name;
                    model.Ip = originalSpeaker.Ip;
                    model.Model = originalSpeaker.Model;
                    model.Password = ""; // 비밀번호는 보안상 표시하지 않음
                    model.Location = originalSpeaker.Location;
                    model.State = originalSpeaker.State;

                    // 원본 IP 주소 저장 (중복 체크시 현재 스피커 제외를 위해)
                    originalIpAddress = originalSpeaker.Ip;
                }
                else
                {
                    errorVisible = true;
                    error = "스피커 정보를 불러올 수 없습니다.";
                }
            }
            catch (Exception ex)
            {
                errorVisible = true;
                error = $"스피커 정보를 불러오는 중 오류가 발생했습니다: {ex.Message}";
            }
            finally
            {
                isLoadingData = false;
            }
        }

        // IP 주소 중복 확인 메서드 (현재 스피커 제외)
        protected async Task<bool> IsIpAddressExists(string ipAddress)
        {
            try
            {
                // 현재 스피커의 IP와 같으면 중복 아님
                if (ipAddress == originalIpAddress)
                {
                    return false;
                }

                // OData를 사용하여 해당 IP 주소의 다른 스피커가 존재하는지 검사
                var response = await Http.GetFromJsonAsync<ODataResponse<SpeakerViewModel>>(
                    $"/odata/wics/Speakers?$filter=Ip eq '{ipAddress}' and Id ne {SpeakerId} and (DeleteYn eq 'N' or DeleteYn eq null)&$count=true");

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

                // IP 주소가 변경되었을 경우에만 중복 확인
                if (model.Ip != originalIpAddress)
                {
                    bool ipExists = await IsIpAddressExists(model.Ip);
                    if (ipExists)
                    {
                        errorVisible = true;
                        error = $"IP 주소 '{model.Ip}'는 이미 다른 스피커가 사용 중입니다. 다른 IP 주소를 입력해주세요.";
                        isProcessing = false;

                        // 오류 메시지가 표시될 때 스크롤을 맨 위로 이동
                        await JSRuntime.InvokeVoidAsync("scrollToTop");
                        return; // 중복된 IP 주소면 여기서 중단
                    }
                }

                // 서버로 전송할 스피커 데이터 생성 (변경된 필드만)
                var updateData = new UpdateSpeakerRequest
                {
                    Name = model.Name,
                    Ip = model.Ip,
                    Model = model.Model,
                    Location = model.Location,
                    State = model.State,
                    UpdatedAt = DateTime.Now
                };

                // 비밀번호가 입력된 경우에만 업데이트에 포함
                if (!string.IsNullOrWhiteSpace(model.Password))
                {
                    updateData.Password = model.Password;
                }

                // JSON 직렬화 옵션 설정
                var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
                {
                    PropertyNamingPolicy = null,
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                };

                // API 호출하여 스피커 업데이트 (PATCH 메서드 사용)
                var response = await Http.PatchAsJsonAsync($"odata/wics/Speakers({SpeakerId})", updateData, options);

                // 응답 확인
                if (response.IsSuccessStatusCode)
                {
                    // 성공 알림 표시
                    NotificationService.Notify(new NotificationMessage
                    {
                        Severity = NotificationSeverity.Success,
                        Summary = "수정 완료",
                        Detail = $"'{model.Name}' 스피커 정보가 성공적으로 수정되었습니다.",
                        Duration = 4000
                    });

                    // 다이얼로그 닫기 및 데이터 반환
                    DialogService.Close(true);
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    errorVisible = true;
                    error = $"스피커 수정 중 오류가 발생했습니다: {errorContent}";

                    // 오류 메시지가 표시될 때 스크롤을 맨 위로 이동
                    await JSRuntime.InvokeVoidAsync("scrollToTop");
                }
            }
            catch (Exception ex)
            {
                errorVisible = true;
                error = $"스피커 수정 중 오류가 발생했습니다: {ex.Message}";

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
                        const dialogContent = document.querySelector('.rz-dialog-content');
                        if (dialogContent) {
                            dialogContent.scrollTop = 0;
                        }
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

    // 스피커 수정 요청 모델
    public class UpdateSpeakerRequest
    {
        public string Name { get; set; }
        public string Ip { get; set; }
        public string Model { get; set; }
        public string Password { get; set; }
        public string Location { get; set; }
        public byte State { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    // 상태 옵션 모델
    public class StateOption
    {
        public byte Value { get; set; }
        public string Text { get; set; }
    }

    // 편집용 스피커 폼 모델
    public class EditSpeakerFormModel
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

        [StringLength(100, ErrorMessage = "비밀번호는 100자 이내로 입력해주세요.")]
        public string Password { get; set; }

        [Required(ErrorMessage = "설치 위치는 필수입니다.")]
        [StringLength(200, ErrorMessage = "설치 위치는 200자 이내로 입력해주세요.")]
        public string Location { get; set; }

        public byte State { get; set; }

        public ulong? ChannelId { get; set; }
    }
}
