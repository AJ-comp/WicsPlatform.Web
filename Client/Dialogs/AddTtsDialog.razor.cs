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
        // 기본값 설정
        model.VoiceType = "female";
        model.Speed = 1.0m;
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
            var tts = new CreateTtsRequest
            {
                Name = model.Name,
                Content = model.Content,
                DeleteYn = "N",
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now
            };

            // API 호출하여 TTS 생성
            var response = await Http.PostAsJsonAsync("odata/wics/Tts", tts);

            // 응답 확인
            if (response.IsSuccessStatusCode)
            {
                // 성공 알림 표시
                NotificationService.Notify(new NotificationMessage
                {
                    Severity = NotificationSeverity.Success,
                    Summary = "TTS 생성 성공",
                    Detail = $"'{model.Name}' TTS가 성공적으로 생성되었습니다.",
                    Duration = 4000
                });

                // 다이얼로그 닫기 및 데이터 반환
                DialogService.Close(true);
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                errorVisible = true;
                error = $"TTS 생성 중 오류가 발생했습니다: {errorContent}";
            }
        }
        catch (Exception ex)
        {
            errorVisible = true;
            error = $"TTS 생성 중 오류가 발생했습니다: {ex.Message}";
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
    public decimal Speed { get; set; }
}

// 음성 옵션 모델
public class VoiceOption
{
    public string Value { get; set; }
    public string Text { get; set; }
}
