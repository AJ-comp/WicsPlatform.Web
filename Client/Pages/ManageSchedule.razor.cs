using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Radzen;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using WicsPlatform.Server.Models.wics;

namespace WicsPlatform.Client.Pages
{
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
                return "반복 없음";
            if (repeatCount == 255)  // byte의 최대값을 무한 반복으로 사용
                return "무한 반복";

            return $"{repeatCount}회 반복";
        }

        // 스케줄 추가 (추후 구현)
        private async Task OpenAddScheduleDialog()
        {
            NotificationService.Notify(new NotificationMessage
            {
                Severity = NotificationSeverity.Info,
                Summary = "준비 중",
                Detail = "스케줄 추가 기능은 추후 업데이트 예정입니다.",
                Duration = 3000
            });
        }

        // 스케줄 편집 (추후 구현)
        private async Task OpenEditScheduleDialog(Schedule schedule)
        {
            NotificationService.Notify(new NotificationMessage
            {
                Severity = NotificationSeverity.Info,
                Summary = "준비 중",
                Detail = "스케줄 편집 기능은 추후 업데이트 예정입니다.",
                Duration = 3000
            });
        }

        // 스케줄 삭제 (추후 구현)
        private async Task DeleteSchedule(Schedule schedule)
        {
            NotificationService.Notify(new NotificationMessage
            {
                Severity = NotificationSeverity.Info,
                Summary = "준비 중",
                Detail = "스케줄 삭제 기능은 추후 업데이트 예정입니다.",
                Duration = 3000
            });
        }
    }
}