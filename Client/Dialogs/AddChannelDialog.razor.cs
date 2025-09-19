using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Radzen;
using System.ComponentModel.DataAnnotations;
using System.Net.Http.Json;

namespace WicsPlatform.Client.Dialogs;

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

    // 채널 타입 옵션 - 타입별 기본 우선순위 포함
    protected List<ChannelTypeOption> channelTypes = new List<ChannelTypeOption>
    {
        new ChannelTypeOption { Value = 0, Text = "일반 방송", DefaultPriority = 125 },  // 101~150 중간값
        new ChannelTypeOption { Value = 1, Text = "긴급 방송", DefaultPriority = 25 },   // 0~50 중간값
        new ChannelTypeOption { Value = 2, Text = "음악 방송", DefaultPriority = 200 },  // 151~255 중간값
        new ChannelTypeOption { Value = 3, Text = "공지 방송", DefaultPriority = 75 }    // 51~100 중간값
    };

    // 샘플레이트 옵션
    protected List<SampleRateOption> sampleRates = new List<SampleRateOption>
    {
        new SampleRateOption { Value = 8000, Text = "8000 Hz (전화품질)" },
        new SampleRateOption { Value = 16000, Text = "16000 Hz (광대역)" },
        new SampleRateOption { Value = 24000, Text = "24000 Hz (고품질)" },
        new SampleRateOption { Value = 48000, Text = "48000 Hz (프로페셔널)" }
    };

    // 오디오 채널 옵션
    protected List<AudioChannelOption> audioChannels = new List<AudioChannelOption>
    {
        new AudioChannelOption { Value = 1, Text = "모노 (1채널)" },
        new AudioChannelOption { Value = 2, Text = "스테레오 (2채널)" }
    };

    protected override void OnInitialized()
    {
        // 기본값 설정 - 일반 방송
        model.Type = 0;
        model.Priority = 125; // 일반 방송 기본 우선순위
        model.SamplingRate = 48000;
        model.ChannelCount = 1;
    }

    // 채널 타입 변경 시 권장 우선순위 자동 설정
    protected void OnChannelTypeChanged(object value)
    {
        if (value is byte typeValue)
        {
            var selectedType = channelTypes.FirstOrDefault(t => t.Value == typeValue);
            if (selectedType != null)
            {
                model.Priority = selectedType.DefaultPriority;
                StateHasChanged();
            }
        }
    }

    // 우선순위 레벨 텍스트 반환
    protected string GetPriorityLevel(int priority)
    {
        if (priority <= 50)
            return "(긴급)";
        else if (priority <= 100)
            return "(공지)";
        else if (priority <= 150)
            return "(일반)";
        else
            return "(음악)";
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

            // 우선순위 범위 검증
            if (model.Priority < 0 || model.Priority > 255)
            {
                errorVisible = true;
                error = "우선순위는 0~255 사이의 값이어야 합니다.";
                isProcessing = false;
                return;
            }

            // 서버로 전송할 채널 데이터 생성
            var channel = new CreateChannelRequest
            {
                Name = model.Name,
                Description = model.Description ?? "",
                Type = model.Type,
                Priority = model.Priority,
                SamplingRate = model.SamplingRate,
                ChannelCount = model.ChannelCount,
                State = 1, // 기본 활성 상태
                Volume = 1.0f,
                MicVolume = 0.5f,
                MediaVolume = 0.5f,
                TtsVolume = 0.5f,
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now
            };

            // API 호출하여 채널 생성
            var response = await Http.PostAsJsonAsync("odata/wics/Channels", channel);

            // 응답 확인
            if (response.IsSuccessStatusCode)
            {
                var channelTypeName = channelTypes.FirstOrDefault(t => t.Value == model.Type)?.Text ?? "채널";

                // 성공 알림 표시
                NotificationService.Notify(new NotificationMessage
                {
                    Severity = NotificationSeverity.Success,
                    Summary = "채널 생성 성공",
                    Detail = $"'{model.Name}' {channelTypeName}이 성공적으로 생성되었습니다. (우선순위: {model.Priority})",
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
    public int Priority { get; set; }
    public int SamplingRate { get; set; }
    public int ChannelCount { get; set; }
    public sbyte State { get; set; }
    public float Volume { get; set; }
    public float MicVolume { get; set; }
    public float MediaVolume { get; set; }
    public float TtsVolume { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

// 채널 생성/수정 폼 모델
public class ChannelFormModel
{
    [Required(ErrorMessage = "채널 이름은 필수입니다.")]
    [StringLength(100, ErrorMessage = "채널 이름은 100자 이내로 입력해주세요.")]
    public string Name { get; set; }

    [StringLength(500, ErrorMessage = "설명은 500자 이내로 입력해주세요.")]
    public string Description { get; set; }

    [Required(ErrorMessage = "채널 타입은 필수입니다.")]
    public byte Type { get; set; }

    [Required(ErrorMessage = "우선순위는 필수입니다.")]
    [Range(0, 255, ErrorMessage = "우선순위는 0~255 사이의 값이어야 합니다.")]
    public int Priority { get; set; }

    public int SamplingRate { get; set; }
    public int ChannelCount { get; set; }
}

// 옵션 클래스들
public class ChannelTypeOption
{
    public byte Value { get; set; }
    public string Text { get; set; }
    public int DefaultPriority { get; set; }  // 타입별 기본 우선순위
}

public class SampleRateOption
{
    public int Value { get; set; }
    public string Text { get; set; }
}

public class AudioChannelOption
{
    public int Value { get; set; }
    public string Text { get; set; }
}