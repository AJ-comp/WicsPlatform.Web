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
    public partial class EditTtsDialog
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
        public ulong TtsId { get; set; }

        protected TtsEditFormModel model = new TtsEditFormModel();
        protected TtsData originalTts;
        protected string error;
        protected bool errorVisible;
        protected bool isProcessing = false;

        protected override async Task OnInitializedAsync()
        {
            await LoadTtsData();
        }

        protected async Task LoadTtsData()
        {
            try
            {
                isProcessing = true;

                // TTS 데이터 가져오기
                originalTts = await Http.GetFromJsonAsync<TtsData>($"odata/wics/Tts({TtsId})");

                if (originalTts != null)
                {
                    // 모델에 데이터 설정
                    model.Name = originalTts.Name;
                    model.Content = originalTts.Content;
                }
                else
                {
                    errorVisible = true;
                    error = "TTS 정보를 불러올 수 없습니다.";
                }
            }
            catch (Exception ex)
            {
                errorVisible = true;
                error = $"TTS 정보를 불러오는 중 오류가 발생했습니다: {ex.Message}";
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
                    error = "제목은 필수 항목입니다.";
                    isProcessing = false;
                    return;
                }

                if (string.IsNullOrWhiteSpace(model.Content))
                {
                    errorVisible = true;
                    error = "내용은 필수 항목입니다.";
                    isProcessing = false;
                    return;
                }

                if (model.Content.Length > 1000)
                {
                    errorVisible = true;
                    error = "내용은 1000자를 초과할 수 없습니다.";
                    isProcessing = false;
                    return;
                }

                // 서버로 전송할 TTS 데이터 생성
                var tts = new UpdateTtsRequest
                {
                    Name = model.Name,
                    Content = model.Content,
                    UpdatedAt = DateTime.UtcNow
                };

                // API 호출하여 TTS 업데이트 (PATCH 메서드 사용)
                var response = await Http.PatchAsJsonAsync($"odata/wics/Tts({TtsId})", tts);

                // 응답 확인
                if (response.IsSuccessStatusCode)
                {
                    // 성공 알림 표시
                    NotificationService.Notify(new NotificationMessage
                    {
                        Severity = NotificationSeverity.Success,
                        Summary = "TTS 수정 성공",
                        Detail = $"'{model.Name}' TTS가 성공적으로 수정되었습니다.",
                        Duration = 4000
                    });

                    // 다이얼로그 닫기 및 데이터 반환
                    DialogService.Close(true);
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    errorVisible = true;
                    error = $"TTS 수정 중 오류가 발생했습니다: {errorContent}";
                }
            }
            catch (Exception ex)
            {
                errorVisible = true;
                error = $"TTS 수정 중 오류가 발생했습니다: {ex.Message}";
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

    // TTS 수정 요청 모델
    public class UpdateTtsRequest
    {
        public string Name { get; set; }
        public string Content { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    // TTS 수정 폼 모델
    public class TtsEditFormModel
    {
        [Required(ErrorMessage = "제목은 필수입니다.")]
        [StringLength(100, ErrorMessage = "제목은 100자 이내로 입력해주세요.")]
        public string Name { get; set; }

        [Required(ErrorMessage = "내용은 필수입니다.")]
        [StringLength(1000, ErrorMessage = "내용은 1000자 이내로 입력해주세요.")]
        public string Content { get; set; }
    }

    // TTS 데이터 응답 모델
    public class TtsData
    {
        public ulong Id { get; set; }
        public string Name { get; set; }
        public string Content { get; set; }
        public string DeleteYn { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}