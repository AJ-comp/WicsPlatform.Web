using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Diagnostics;
using Microsoft.JSInterop;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Radzen;
using Radzen.Blazor;
using System.ComponentModel.DataAnnotations;
using System.Net.Http;
using System.Net.Http.Json;

namespace WicsPlatform.Client.Dialogs;

public partial class AddTtsDialog
{
    [Inject]
    protected IJSRuntime JSRuntime { get; set; }

    [Inject]
    protected DialogService DialogService { get; set; }

    [Inject]
    protected NotificationService NotificationService { get; set; }

    [Inject]
    protected HttpClient Http { get; set; }

    protected TtsFormModel model = new TtsFormModel();
    protected string error;
    protected bool errorVisible;
    protected bool isProcessing = false;

    // 음성 타입 옵션
    protected List<VoiceOption> voiceTypes = new List<VoiceOption>
    {
        new VoiceOption { Value = "female", Text = "여성 음성" },
        new VoiceOption { Value = "male", Text = "남성 음성" },
        new VoiceOption { Value = "child", Text = "어린이 음성" }
    };

    protected override void OnInitialized()
    {
        Debug.WriteLine("[AddTtsDialog] OnInitialized 호출");
        // 기본값 설정
        model.VoiceType = "female";
    }

    protected async Task FormSubmit()
    {
        Debug.WriteLine("[AddTtsDialog] ========== FormSubmit 시작 ==========");
        
        try
        {
            Debug.WriteLine($"[AddTtsDialog] isProcessing = true");
            isProcessing = true;
            errorVisible = false;

            Debug.WriteLine($"[AddTtsDialog] 입력값 - Name: '{model.Name}', Content 길이: {model.Content?.Length ?? 0}");

            // 폼 유효성 검사
            if (string.IsNullOrWhiteSpace(model.Name))
            {
                Debug.WriteLine("[AddTtsDialog] 유효성 검사 실패: 제목 누락");
                errorVisible = true;
                error = "제목은 필수 항목입니다.";
                isProcessing = false;
                return;
            }

            if (string.IsNullOrWhiteSpace(model.Content))
            {
                Debug.WriteLine("[AddTtsDialog] 유효성 검사 실패: 내용 누락");
                errorVisible = true;
                error = "내용은 필수 항목입니다.";
                isProcessing = false;
                return;
            }

            if (model.Content.Length > 1000)
            {
                Debug.WriteLine("[AddTtsDialog] 유효성 검사 실패: 내용 길이 초과");
                errorVisible = true;
                error = "내용은 1000자를 초과할 수 없습니다.";
                isProcessing = false;
                return;
            }

            Debug.WriteLine("[AddTtsDialog] 유효성 검사 통과");

            // 서버로 전송할 TTS 데이터 생성
            var tts = new CreateTtsRequest
            {
                Name = model.Name,
                Content = model.Content,
                DeleteYn = "N",
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now
            };

            Debug.WriteLine($"[AddTtsDialog] API 호출: POST odata/wics/Tts");

            // API 호출하여 TTS 생성
            var response = await Http.PostAsJsonAsync("odata/wics/Tts", tts);

            Debug.WriteLine($"[AddTtsDialog] API 응답: StatusCode={response.StatusCode}");

            // 응답 확인
            if (response.IsSuccessStatusCode)
            {
                Debug.WriteLine("[AddTtsDialog] TTS 생성 성공!");
                
                // 성공 알림 표시
                NotificationService.Notify(new NotificationMessage
                {
                    Severity = NotificationSeverity.Success,
                    Summary = "TTS 생성 성공",
                    Detail = $"'{model.Name}' TTS가 성공적으로 생성되었습니다.",
                    Duration = 4000
                });

                Debug.WriteLine("[AddTtsDialog] DialogService.Close(true) 호출");
                // 다이얼로그 닫기 및 데이터 반환
                DialogService.Close(true);
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                Debug.WriteLine($"[AddTtsDialog] TTS 생성 실패: {errorContent}");
                errorVisible = true;
                error = $"TTS 생성 중 오류가 발생했습니다: {errorContent}";
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AddTtsDialog] 예외 발생: {ex.Message}");
            Debug.WriteLine($"[AddTtsDialog] 스택 트레이스: {ex.StackTrace}");
            errorVisible = true;
            error = $"TTS 생성 중 오류가 발생했습니다: {ex.Message}";
        }
        finally
        {
            Debug.WriteLine($"[AddTtsDialog] isProcessing = false");
            isProcessing = false;
            Debug.WriteLine("[AddTtsDialog] ========== FormSubmit 종료 ==========");
        }
    }

    protected async Task CancelClick()
    {
        Debug.WriteLine("[AddTtsDialog] CancelClick 호출");
        await Task.Delay(100);
        DialogService.Close(null);
    }


    private string GetVoiceTypeText(string voiceType)
    {
        return voiceType switch
        {
            "female" => "여성 음성",
            "male" => "남성 음성",
            "child" => "어린이 음성",
            _ => "기본 음성"
        };
    }
}

// TTS 생성 요청 모델
public class CreateTtsRequest
{
    public string Name { get; set; }
    public string Content { get; set; }
    public string DeleteYn { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

// TTS 생성/수정 폼 모델
public class TtsFormModel
{
    [Required(ErrorMessage = "제목은 필수입니다.")]
    [StringLength(100, ErrorMessage = "제목은 100자 이내로 입력해주세요.")]
    public string Name { get; set; }

    [Required(ErrorMessage = "내용은 필수입니다.")]
    [StringLength(1000, ErrorMessage = "내용은 1000자 이내로 입력해주세요.")]
    public string Content { get; set; }

    public string VoiceType { get; set; }
}

// 음성 옵션 모델
public class VoiceOption
{
    public string Value { get; set; }
    public string Text { get; set; }
}
