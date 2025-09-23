using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Radzen;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Net.Http.Json;
using System.Threading.Tasks;

namespace WicsPlatform.Client.Dialogs;

public partial class AddScheduleDialog
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

    // 폼 모델
    protected ScheduleFormModel model = new ScheduleFormModel();
    protected string error;
    protected bool errorVisible;
    protected bool isProcessing = false;

    // 요일 데이터
    protected Dictionary<string, string> weekdays = new Dictionary<string, string>
    {
        { "Mon", "월" },
        { "Tue", "화" },
        { "Wed", "수" },
        { "Thu", "목" },
        { "Fri", "금" },
        { "Sat", "토" },
        { "Sun", "일" }
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

    // 콘텐츠 관련
    protected IEnumerable<WicsPlatform.Server.Models.wics.Medium> availableMedia = new List<WicsPlatform.Server.Models.wics.Medium>();
    protected IEnumerable<WicsPlatform.Server.Models.wics.Tt> availableTts = new List<WicsPlatform.Server.Models.wics.Tt>();
    protected HashSet<ulong> selectedMediaIds = new HashSet<ulong>();
    protected HashSet<ulong> selectedTtsIds = new HashSet<ulong>();
    protected int selectedTabIndex = 0;

    protected override async Task OnInitializedAsync()
    {
        // 기본값 설정
        model.StartTime = new TimeOnly(9, 0);
        model.RepeatCount = 1;
        model.SampleRate = 48000;
        model.ChannelCount = 1;
        model.Volume = 0.5f;

        // 모든 요일 선택 (기본값)
        model.Monday = "Y";
        model.Tuesday = "Y";
        model.Wednesday = "Y";
        model.Thursday = "Y";
        model.Friday = "Y";
        model.Saturday = "Y";
        model.Sunday = "Y";

        // 미디어 및 TTS 목록 로드
        await LoadAvailableContent();
    }

    // 사용 가능한 콘텐츠 로드
    private async Task LoadAvailableContent()
    {
        try
        {
            // 미디어 로드
            var mediaQuery = new Radzen.Query
            {
                Filter = "(DeleteYn eq 'N' or DeleteYn eq null)",
                OrderBy = "CreatedAt desc"
            };
            var mediaResult = await WicsService.GetMedia(mediaQuery);
            availableMedia = mediaResult.Value.AsODataEnumerable();

            // TTS 로드
            var ttsQuery = new Radzen.Query
            {
                Filter = "(DeleteYn eq 'N' or DeleteYn eq null)",
                OrderBy = "CreatedAt desc"
            };
            var ttsResult = await WicsService.GetTts(ttsQuery);
            availableTts = ttsResult.Value.AsODataEnumerable();
        }
        catch (Exception ex)
        {
            errorVisible = true;
            error = $"콘텐츠 목록을 불러오는 중 오류가 발생했습니다: {ex.Message}";
        }
    }

    // 요일 관련 메서드
    protected bool GetWeekdayValue(string day)
    {
        return day switch
        {
            "Mon" => model.Monday == "Y",
            "Tue" => model.Tuesday == "Y",
            "Wed" => model.Wednesday == "Y",
            "Thu" => model.Thursday == "Y",
            "Fri" => model.Friday == "Y",
            "Sat" => model.Saturday == "Y",
            "Sun" => model.Sunday == "Y",
            _ => false
        };
    }

    protected void ToggleWeekday(string day)
    {
        switch (day)
        {
            case "Mon": model.Monday = model.Monday == "Y" ? "N" : "Y"; break;
            case "Tue": model.Tuesday = model.Tuesday == "Y" ? "N" : "Y"; break;
            case "Wed": model.Wednesday = model.Wednesday == "Y" ? "N" : "Y"; break;
            case "Thu": model.Thursday = model.Thursday == "Y" ? "N" : "Y"; break;
            case "Fri": model.Friday = model.Friday == "Y" ? "N" : "Y"; break;
            case "Sat": model.Saturday = model.Saturday == "Y" ? "N" : "Y"; break;
            case "Sun": model.Sunday = model.Sunday == "Y" ? "N" : "Y"; break;
        }
    }

    protected string GetWeekdayFullName(string day)
    {
        return day switch
        {
            "Mon" => "월요일",
            "Tue" => "화요일",
            "Wed" => "수요일",
            "Thu" => "목요일",
            "Fri" => "금요일",
            "Sat" => "토요일",
            "Sun" => "일요일",
            _ => ""
        };
    }

    // 빠른 선택 버튼
    protected void SelectAllDays()
    {
        model.Monday = model.Tuesday = model.Wednesday = model.Thursday =
        model.Friday = model.Saturday = model.Sunday = "Y";
    }

    protected void SelectWeekdays()
    {
        model.Monday = model.Tuesday = model.Wednesday = model.Thursday = model.Friday = "Y";
        model.Saturday = model.Sunday = "N";
    }

    protected void SelectWeekend()
    {
        model.Monday = model.Tuesday = model.Wednesday = model.Thursday = model.Friday = "N";
        model.Saturday = model.Sunday = "Y";
    }

    protected void ClearWeekdays()
    {
        model.Monday = model.Tuesday = model.Wednesday = model.Thursday =
        model.Friday = model.Saturday = model.Sunday = "N";
    }

    // 미디어 선택 토글
    protected void ToggleMediaSelection(ulong mediaId)
    {
        if (selectedMediaIds.Contains(mediaId))
        {
            selectedMediaIds.Remove(mediaId);
        }
        else
        {
            selectedMediaIds.Add(mediaId);
        }
    }

    // TTS 선택 토글
    protected void ToggleTtsSelection(ulong ttsId)
    {
        if (selectedTtsIds.Contains(ttsId))
        {
            selectedTtsIds.Remove(ttsId);
        }
        else
        {
            selectedTtsIds.Add(ttsId);
        }
    }

    // 폼 제출
    protected async Task FormSubmit()
    {
        try
        {
            isProcessing = true;
            errorVisible = false;

            // 유효성 검사
            if (string.IsNullOrWhiteSpace(model.Name))
            {
                errorVisible = true;
                error = "스케줄명은 필수 항목입니다.";
                isProcessing = false;
                return;
            }

            // 최소 하나의 요일이 선택되어야 함
            if (model.Monday != "Y" && model.Tuesday != "Y" && model.Wednesday != "Y" &&
                model.Thursday != "Y" && model.Friday != "Y" && model.Saturday != "Y" && model.Sunday != "Y")
            {
                errorVisible = true;
                error = "최소 하나 이상의 요일을 선택해야 합니다.";
                isProcessing = false;
                return;
            }

            // Schedule 모델 직접 생성
            var schedule = new WicsPlatform.Server.Models.wics.Schedule
            {
                Name = model.Name,
                Description = model.Description ?? "",
                SampleRate = (uint)model.SampleRate,
                ChannelCount = (byte)model.ChannelCount,
                Volume = model.Volume,
                StartTime = model.StartTime,
                Monday = model.Monday,
                Tuesday = model.Tuesday,
                Wednesday = model.Wednesday,
                Thursday = model.Thursday,
                Friday = model.Friday,
                Saturday = model.Saturday,
                Sunday = model.Sunday,
                RepeatCount = (byte)model.RepeatCount,
                DeleteYn = "N",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };


            // WicsService의 CreateSchedule 메서드 사용
            var createdSchedule = await WicsService.CreateSchedule(schedule);

            if (createdSchedule != null)
            {
                // 미디어 매핑 저장
                if (selectedMediaIds.Any())
                {
                    foreach (var mediaId in selectedMediaIds)
                    {
                        var mediaMapping = new WicsPlatform.Server.Models.wics.MapScheduleMedium
                        {
                            ScheduleId = createdSchedule.Id,
                            MediaId = mediaId,
                            DeleteYn = "N",
                            CreatedAt = DateTime.UtcNow,
                            UpdatedAt = DateTime.UtcNow
                        };

                        await WicsService.CreateMapScheduleMedium(mediaMapping);
                    }
                }

                // TTS 매핑 저장
                if (selectedTtsIds.Any())
                {
                    foreach (var ttsId in selectedTtsIds)
                    {
                        var ttsMapping = new WicsPlatform.Server.Models.wics.MapScheduleTt
                        {
                            ScheduleId = createdSchedule.Id,
                            TtsId = ttsId,
                            DeleteYn = "N",
                            CreatedAt = DateTime.UtcNow,
                            UpdatedAt = DateTime.UtcNow
                        };

                        await WicsService.CreateMapScheduleTt(ttsMapping);
                    }
                }

                // 성공 알림 표시
                var weekdayText = GetSelectedWeekdaysText();
                NotificationService.Notify(new NotificationMessage
                {
                    Severity = NotificationSeverity.Success,
                    Summary = "스케줄 생성 성공",
                    Detail = $"'{model.Name}' 스케줄이 생성되었습니다. ({model.StartTime:HH:mm}, {weekdayText})",
                    Duration = 4000
                });

                // 다이얼로그 닫기 및 데이터 반환
                DialogService.Close(true);
            }
        }
        catch (Exception ex)
        {
            errorVisible = true;
            error = $"스케줄 생성 중 오류가 발생했습니다: {ex.Message}";
        }
        finally
        {
            isProcessing = false;
        }
    }

    // 선택된 요일 텍스트 반환
    private string GetSelectedWeekdaysText()
    {
        var days = new List<string>();

        if (model.Monday == "Y") days.Add("월");
        if (model.Tuesday == "Y") days.Add("화");
        if (model.Wednesday == "Y") days.Add("수");
        if (model.Thursday == "Y") days.Add("목");
        if (model.Friday == "Y") days.Add("금");
        if (model.Saturday == "Y") days.Add("토");
        if (model.Sunday == "Y") days.Add("일");

        if (days.Count == 7) return "매일";
        if (days.Count == 5 && !days.Contains("토") && !days.Contains("일")) return "평일";
        if (days.Count == 2 && days.Contains("토") && days.Contains("일")) return "주말";

        return string.Join(", ", days);
    }

    protected async Task CancelClick()
    {
        await Task.Delay(100);
        DialogService.Close(null);
    }
}

// 스케줄 생성 요청 모델
public class CreateScheduleRequest
{
    public string Name { get; set; }
    public string Description { get; set; }
    public uint SampleRate { get; set; }
    public byte ChannelCount { get; set; }
    public float Volume { get; set; }
    public DateTime StartTime { get; set; }
    public string Monday { get; set; }
    public string Tuesday { get; set; }
    public string Wednesday { get; set; }
    public string Thursday { get; set; }
    public string Friday { get; set; }
    public string Saturday { get; set; }
    public string Sunday { get; set; }
    public byte RepeatCount { get; set; }
    public string DeleteYn { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

// 스케줄 생성/수정 폼 모델
public class ScheduleFormModel
{
    [Required(ErrorMessage = "스케줄 이름은 필수입니다.")]
    [StringLength(100, ErrorMessage = "스케줄 이름은 100자 이내로 입력해주세요.")]
    public string Name { get; set; }

    [StringLength(500, ErrorMessage = "설명은 500자 이내로 입력해주세요.")]
    public string Description { get; set; }

    [Required(ErrorMessage = "시작 시간은 필수입니다.")]
    public TimeOnly StartTime { get; set; }

    public string Monday { get; set; } = "N";
    public string Tuesday { get; set; } = "N";
    public string Wednesday { get; set; } = "N";
    public string Thursday { get; set; } = "N";
    public string Friday { get; set; } = "N";
    public string Saturday { get; set; } = "N";
    public string Sunday { get; set; } = "N";

    [Range(0, 255, ErrorMessage = "반복 횟수는 0~255 사이의 값이어야 합니다.")]
    public int RepeatCount { get; set; }

    public int SampleRate { get; set; }
    public int ChannelCount { get; set; }

    [Range(0, 1, ErrorMessage = "볼륨은 0~1 사이의 값이어야 합니다.")]
    public float Volume { get; set; }
}