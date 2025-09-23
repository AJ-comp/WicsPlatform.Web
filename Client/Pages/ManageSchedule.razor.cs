using Microsoft.AspNetCore.Components;
using Radzen;
using WicsPlatform.Client.Dialogs;
using WicsPlatform.Server.Models.wics;

namespace WicsPlatform.Client.Pages;

public partial class ManageSchedule
{
    [Inject] protected wicsService WicsService { get; set; }
    [Inject] protected NotificationService NotificationService { get; set; }
    [Inject] protected DialogService DialogService { get; set; }
    [Inject] protected ILogger<ManageSchedule> Logger { get; set; }

    // 스케줄 관련 필드
    private IEnumerable<Schedule> schedules = new List<Schedule>();
    private Schedule selectedSchedule = null;
    private bool isLoadingSchedules = false;
    private bool isDeletingSchedule = false;

    // 요일 데이터
    private readonly Dictionary<string, string> weekdays = new Dictionary<string, string>
    {
        { "월", "monday" },
        { "화", "tuesday" },
        { "수", "wednesday" },
        { "목", "thursday" },
        { "금", "friday" },
        { "토", "saturday" },
        { "일", "sunday" }
    };

    protected override async Task OnInitializedAsync()
    {
        await LoadSchedules();
    }

    private async Task LoadSchedules()
    {
        try
        {
            isLoadingSchedules = true;
            StateHasChanged();

            var query = new Radzen.Query
            {
                Filter = "DeleteYn eq 'N' or DeleteYn eq null",
                OrderBy = "CreatedAt desc"
            };

            var result = await WicsService.GetSchedules(query);
            schedules = result.Value.AsODataEnumerable();

            Logger.LogInformation($"Loaded {schedules.Count()} schedules");
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to load schedules");
            NotificationService.Notify(new NotificationMessage
            {
                Severity = NotificationSeverity.Error,
                Summary = "오류",
                Detail = "스케줄 목록을 불러오는 중 오류가 발생했습니다.",
                Duration = 4000
            });
        }
        finally
        {
            isLoadingSchedules = false;
            StateHasChanged();
        }
    }

    private void SelectSchedule(Schedule schedule)
    {
        selectedSchedule = schedule;
        Logger.LogInformation($"Selected schedule: {schedule.Name} (ID: {schedule.Id})");
        StateHasChanged();
    }

    // 스케줄 상태 배지 스타일 (DeleteYn 기반)
    private BadgeStyle GetScheduleBadgeStyle(string deleteYn) =>
        (deleteYn == "Y") ? BadgeStyle.Danger : BadgeStyle.Success;

    // 스케줄 상태 텍스트
    private string GetScheduleStateText(string deleteYn) =>
        (deleteYn == "Y") ? "삭제됨" : "활성";

    // 요일 표시 문자열 생성
    private string GetWeekdaysDisplay(Schedule schedule)
    {
        if (schedule == null) return "";

        var days = new List<string>();

        if (schedule.Monday == "Y") days.Add("월");
        if (schedule.Tuesday == "Y") days.Add("화");
        if (schedule.Wednesday == "Y") days.Add("수");
        if (schedule.Thursday == "Y") days.Add("목");
        if (schedule.Friday == "Y") days.Add("금");
        if (schedule.Saturday == "Y") days.Add("토");
        if (schedule.Sunday == "Y") days.Add("일");

        if (!days.Any()) return "";
        if (days.Count == 7) return "매일";
        if (days.Count == 5 && !days.Contains("토") && !days.Contains("일")) return "평일";
        if (days.Count == 2 && days.Contains("토") && days.Contains("일")) return "주말";

        return string.Join(",", days);
    }

    // 특정 요일이 선택되었는지 확인
    private bool IsWeekdaySelected(Schedule schedule, string dayCode)
    {
        if (schedule == null) return false;

        return dayCode switch
        {
            "monday" => schedule.Monday == "Y",
            "tuesday" => schedule.Tuesday == "Y",
            "wednesday" => schedule.Wednesday == "Y",
            "thursday" => schedule.Thursday == "Y",
            "friday" => schedule.Friday == "Y",
            "saturday" => schedule.Saturday == "Y",
            "sunday" => schedule.Sunday == "Y",
            _ => false
        };
    }

    // 반복 횟수 텍스트
    private string GetRepeatText(byte repeatCount)
    {
        if (repeatCount == 0)
            return "무한 반복";
        if (repeatCount == 255)  // byte의 최대값
            return "무한 반복";

        return $"{repeatCount}회 반복";
    }

    // 스케줄 추가
    private async Task OpenAddScheduleDialog()
    {
        var result = await DialogService.OpenAsync<AddScheduleDialog>(
            "새 예약 방송 만들기",
            null,
            new DialogOptions
            {
                Width = "700px",
                Height = "auto",
                Resizable = false,
                Draggable = true,
                CloseDialogOnOverlayClick = false
            });

        if (result is bool success && success)
        {
            // 스케줄 목록 다시 로드
            await LoadSchedules();

            NotificationService.Notify(new NotificationMessage
            {
                Severity = NotificationSeverity.Success,
                Summary = "생성 완료",
                Detail = "새 예약 방송 스케줄이 생성되었습니다.",
                Duration = 4000
            });
        }
    }

    // 스케줄 삭제 확인 다이얼로그 - MouseEventArgs 파라미터 제거
    private async Task ConfirmDeleteSchedule(Schedule schedule)
    {
        var result = await DialogService.Confirm(
            $"'{schedule.Name}' 스케줄을 삭제하시겠습니까?\n연결된 미디어 및 TTS 매핑도 함께 삭제됩니다.",
            "스케줄 삭제 확인",
            new ConfirmOptions
            {
                OkButtonText = "삭제",
                CancelButtonText = "취소"
            });

        if (result == true)
        {
            await DeleteSchedule(schedule);
        }
    }

    // 스케줄 및 관련 데이터 소프트 삭제
    private async Task DeleteSchedule(Schedule schedule)
    {
        if (isDeletingSchedule) return;

        try
        {
            isDeletingSchedule = true;
            StateHasChanged();

            Logger.LogInformation($"Deleting schedule: {schedule.Name} (ID: {schedule.Id})");

            // 1. 스케줄과 연결된 미디어 매핑 소프트 삭제
            var mediaQuery = new Radzen.Query
            {
                Filter = $"ScheduleId eq {schedule.Id} and (DeleteYn eq 'N' or DeleteYn eq null)"
            };
            var mediaMappings = await WicsService.GetMapScheduleMedia(mediaQuery);

            foreach (var mapping in mediaMappings.Value.AsODataEnumerable())
            {
                mapping.DeleteYn = "Y";
                mapping.UpdatedAt = DateTime.UtcNow;
                await WicsService.UpdateMapScheduleMedium(mapping.Id, mapping);
                Logger.LogInformation($"Soft deleted media mapping: {mapping.Id}");
            }

            // 2. 스케줄과 연결된 TTS 매핑 소프트 삭제
            var ttsQuery = new Radzen.Query
            {
                Filter = $"ScheduleId eq {schedule.Id} and (DeleteYn eq 'N' or DeleteYn eq null)"
            };
            var ttsMappings = await WicsService.GetMapScheduleTts(ttsQuery);

            foreach (var mapping in ttsMappings.Value.AsODataEnumerable())
            {
                mapping.DeleteYn = "Y";
                mapping.UpdatedAt = DateTime.UtcNow;
                await WicsService.UpdateMapScheduleTt(mapping.Id, mapping);
                Logger.LogInformation($"Soft deleted TTS mapping: {mapping.Id}");
            }

            // 3. 스케줄 자체 소프트 삭제
            schedule.DeleteYn = "Y";
            schedule.UpdatedAt = DateTime.UtcNow;
            await WicsService.UpdateSchedule(schedule.Id, schedule);

            Logger.LogInformation($"Successfully soft deleted schedule: {schedule.Name}");

            // 선택된 스케줄이 삭제된 스케줄이면 선택 해제
            if (selectedSchedule?.Id == schedule.Id)
            {
                selectedSchedule = null;
            }

            // 스케줄 목록 새로고침
            await LoadSchedules();

            NotificationService.Notify(new NotificationMessage
            {
                Severity = NotificationSeverity.Success,
                Summary = "삭제 완료",
                Detail = $"'{schedule.Name}' 스케줄이 삭제되었습니다.",
                Duration = 4000
            });
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, $"Failed to delete schedule: {schedule.Name}");
            NotificationService.Notify(new NotificationMessage
            {
                Severity = NotificationSeverity.Error,
                Summary = "삭제 실패",
                Detail = "스케줄 삭제 중 오류가 발생했습니다.",
                Duration = 4000
            });
        }
        finally
        {
            isDeletingSchedule = false;
            StateHasChanged();
        }
    }
}