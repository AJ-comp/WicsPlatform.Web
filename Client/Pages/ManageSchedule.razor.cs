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

    // 편집 관련 필드
    private bool isEditMode = false;
    private Schedule editingSchedule = null;
    private Channel editingChannel = null; // 채널 편집 데이터 (이름/오디오 설정)
    private bool isSaving = false;
    private DateTime? tempStartDateTime;
    private int tempRepeatCount;
    private int selectedTabIndex = 0;

    // 스케줄별 채널 캐시
    private readonly Dictionary<ulong, Channel> scheduleChannels = new();

    // 콘텐츠 관련 필드
    private IEnumerable<Medium> availableMedia = new List<Medium>();
    private IEnumerable<Tt> availableTts = new List<Tt>();

    // 현재 스케줄의 콘텐츠 (보기 모드)
    private IEnumerable<Medium> currentScheduleMedia = new List<Medium>();
    private IEnumerable<Tt> currentScheduleTts = new List<Tt>();

    // 편집 중인 콘텐츠 선택 상태
    private HashSet<ulong> editingSelectedMediaIds = new HashSet<ulong>();
    private HashSet<ulong> editingSelectedTtsIds = new HashSet<ulong>();

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

    // 샘플레이트 옵션 (채널용)
    private readonly List<dynamic> sampleRates = new List<dynamic>
    {
        new { Value = 8000u, Text = "8000 Hz" },
        new { Value = 16000u, Text = "16000 Hz" },
        new { Value = 24000u, Text = "24000 Hz" },
        new { Value = 48000u, Text = "48000 Hz" }
    };

    // 오디오 채널 옵션 (채널용)
    private readonly List<dynamic> audioChannels = new List<dynamic>
    {
        new { Value = (byte)1, Text = "1ch (모노)" },
        new { Value = (byte)2, Text = "2ch (스테레오)" }
    };

    protected override async Task OnInitializedAsync()
    {
        await LoadSchedules();
        await LoadAvailableContent();
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

            // 각 스케줄에 연결된 채널 로드 (첫번째 채널 기준)
            scheduleChannels.Clear();
            foreach (var sch in schedules)
            {
                try
                {
                    var channelQuery = new Radzen.Query
                    {
                        Filter = $"ScheduleId eq {sch.Id} and (DeleteYn eq 'N' or DeleteYn eq null)",
                        Top = 1
                    };
                    var channelResult = await WicsService.GetChannels(channelQuery);
                    var channel = channelResult.Value.AsODataEnumerable().FirstOrDefault();
                    if (channel != null)
                    {
                        scheduleChannels[sch.Id] = channel;
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogWarning(ex, $"Failed to load channel for schedule {sch.Id}");
                }
            }

            Logger.LogInformation($"Loaded {schedules.Count()} schedules with channel mappings");
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

    private async Task LoadAvailableContent()
    {
        try
        {
            // 미디어 로드
            var mediaQuery = new Radzen.Query
            {
                Filter = "DeleteYn eq 'N' or DeleteYn eq null",
                OrderBy = "CreatedAt desc"
            };
            var mediaResult = await WicsService.GetMedia(mediaQuery);
            availableMedia = mediaResult.Value.AsODataEnumerable();

            // TTS 로드
            var ttsQuery = new Radzen.Query
            {
                Filter = "DeleteYn eq 'N' or DeleteYn eq null",
                OrderBy = "CreatedAt desc"
            };
            var ttsResult = await WicsService.GetTts(ttsQuery);
            availableTts = ttsResult.Value.AsODataEnumerable();

            Logger.LogInformation($"Loaded {availableMedia.Count()} media files and {availableTts.Count()} TTS messages");
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to load available content");
        }
    }

    private async Task LoadScheduleContent(ulong scheduleId)
    {
        try
        {
            // SchedulePlay 조회 후 미디어/tts 아이디 분리
            var playQuery = new Radzen.Query
            {
                Filter = $"ScheduleId eq {scheduleId} and (DeleteYn eq 'N' or DeleteYn eq null)"
            };
            var playsResult = await WicsService.GetSchedulePlays(playQuery);
            var plays = playsResult.Value.AsODataEnumerable();

            var mediaIds = plays.Where(p => p.MediaId.HasValue).Select(p => p.MediaId!.Value).Distinct().ToList();
            var ttsIds = plays.Where(p => p.TtsId.HasValue).Select(p => p.TtsId!.Value).Distinct().ToList();

            // 미디어 로드
            if (mediaIds.Any())
            {
                var mediaFilter = string.Join(" or ", mediaIds.Select(id => $"Id eq {id}"));
                var mediaQuery = new Radzen.Query
                {
                    Filter = $"({mediaFilter}) and (DeleteYn eq 'N' or DeleteYn eq null)"
                };
                var mediaResult = await WicsService.GetMedia(mediaQuery);
                currentScheduleMedia = mediaResult.Value.AsODataEnumerable();
            }
            else
            {
                currentScheduleMedia = new List<Medium>();
            }

            // TTS 로드
            if (ttsIds.Any())
            {
                var ttsFilter = string.Join(" or ", ttsIds.Select(id => $"Id eq {id}"));
                var ttsQuery = new Radzen.Query
                {
                    Filter = $"({ttsFilter}) and (DeleteYn eq 'N' or DeleteYn eq null)"
                };
                var ttsResult = await WicsService.GetTts(ttsQuery);
                currentScheduleTts = ttsResult.Value.AsODataEnumerable();
            }
            else
            {
                currentScheduleTts = new List<Tt>();
            }

            Logger.LogInformation($"Loaded content for schedule {scheduleId}: {currentScheduleMedia.Count()} media, {currentScheduleTts.Count()} TTS");
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, $"Failed to load content for schedule {scheduleId}");
            currentScheduleMedia = new List<Medium>();
            currentScheduleTts = new List<Tt>();
        }
    }

    private Channel? GetChannelForSchedule(ulong scheduleId)
    {
        return scheduleChannels.TryGetValue(scheduleId, out var ch) ? ch : null;
    }

    private string GetChannelNameForSchedule(Schedule schedule)
    {
        var ch = GetChannelForSchedule(schedule.Id);
        return ch?.Name ?? $"스케줄 #{schedule.Id}";
    }

    private async Task SelectSchedule(Schedule schedule)
    {
        // 편집 중이면 경고
        if (isEditMode && editingSchedule != null)
        {
            CancelEdit();
        }

        selectedSchedule = schedule;
        isEditMode = false;
        editingSchedule = null;
        editingChannel = null;

        // 선택된 스케줄의 콘텐츠 로드
        await LoadScheduleContent(schedule.Id);

        var name = GetChannelNameForSchedule(schedule);
        Logger.LogInformation($"Selected schedule: {name} (ID: {schedule.Id})");
        StateHasChanged();
    }

    // 편집 시작
    private async Task StartEdit()
    {
        if (selectedSchedule == null) return;

        isEditMode = true;

        // 깊은 복사로 편집용 객체 생성 (스케줄 필드만)
        editingSchedule = new Schedule
        {
            Id = selectedSchedule.Id,
            StartTime = selectedSchedule.StartTime,
            Monday = selectedSchedule.Monday,
            Tuesday = selectedSchedule.Tuesday,
            Wednesday = selectedSchedule.Wednesday,
            Thursday = selectedSchedule.Thursday,
            Friday = selectedSchedule.Friday,
            Saturday = selectedSchedule.Saturday,
            Sunday = selectedSchedule.Sunday,
            RepeatCount = selectedSchedule.RepeatCount,
            DeleteYn = selectedSchedule.DeleteYn,
            CreatedAt = selectedSchedule.CreatedAt,
            UpdatedAt = selectedSchedule.UpdatedAt
        };

        // 채널 편집 데이터 준비
        var ch = GetChannelForSchedule(selectedSchedule.Id);
        if (ch != null)
        {
            editingChannel = new Channel
            {
                Id = ch.Id,
                Name = ch.Name,
                Description = ch.Description,
                SamplingRate = ch.SamplingRate,
                ChannelCount = ch.ChannelCount,
                Volume = ch.Volume,
                MicVolume = ch.MicVolume,
                MediaVolume = ch.MediaVolume,
                TtsVolume = ch.TtsVolume,
                Type = ch.Type,
                Priority = ch.Priority,
                State = ch.State,
                AudioMethod = ch.AudioMethod,
                Codec = ch.Codec,
                BitRate = ch.BitRate,
                ScheduleId = ch.ScheduleId,
                DeleteYn = ch.DeleteYn,
                CreatedAt = ch.CreatedAt,
                UpdatedAt = ch.UpdatedAt
            };
        }

        // TimeOnly를 DateTime으로 변환 (편집용)
        tempStartDateTime = DateTime.Today.Add(editingSchedule.StartTime.ToTimeSpan());
        tempRepeatCount = editingSchedule.RepeatCount;

        // 현재 선택된 콘텐츠 ID 복사
        editingSelectedMediaIds = new HashSet<ulong>(currentScheduleMedia.Select(m => m.Id));
        editingSelectedTtsIds = new HashSet<ulong>(currentScheduleTts.Select(t => t.Id));

        StateHasChanged();
    }

    // 편집 취소
    private void CancelEdit()
    {
        isEditMode = false;
        editingSchedule = null;
        editingChannel = null;
        tempStartDateTime = null;
        tempRepeatCount = 0;
        editingSelectedMediaIds.Clear();
        editingSelectedTtsIds.Clear();
        StateHasChanged();
    }

    // 변경사항 저장
    private async Task SaveSchedule()
    {
        if (editingSchedule == null) return;

        try
        {
            isSaving = true;
            StateHasChanged();

            // 유효성 검사: 채널 이름 필수
            if (editingChannel == null || string.IsNullOrWhiteSpace(editingChannel.Name))
            {
                NotificationService.Notify(new NotificationMessage
                {
                    Severity = NotificationSeverity.Warning,
                    Summary = "입력 확인",
                    Detail = "스케줄 이름(채널 이름)은 필수 항목입니다.",
                    Duration = 4000
                });
                isSaving = false;
                return;
            }

            // 최소 하나의 요일이 선택되어야 함
            if (!IsAnyWeekdaySelected())
            {
                NotificationService.Notify(new NotificationMessage
                {
                    Severity = NotificationSeverity.Warning,
                    Summary = "입력 확인",
                    Detail = "최소 하나 이상의 요일을 선택해야 합니다.",
                    Duration = 4000
                });
                isSaving = false;
                return;
            }

            // DateTime을 TimeOnly로 변환
            if (tempStartDateTime.HasValue)
            {
                editingSchedule.StartTime = TimeOnly.FromDateTime(tempStartDateTime.Value);
            }

            editingSchedule.RepeatCount = (byte)tempRepeatCount;
            editingSchedule.UpdatedAt = DateTime.UtcNow;

            // 서버에 스케줄 업데이트
            await WicsService.UpdateSchedule(editingSchedule.Id, editingSchedule);

            // 채널 업데이트 (오디오 설정 및 이름/설명)
            if (editingChannel != null)
            {
                editingChannel.UpdatedAt = DateTime.UtcNow;
                await WicsService.UpdateChannel(editingChannel.Id, editingChannel);
                // 캐시 갱신
                scheduleChannels[editingSchedule.Id] = editingChannel;
            }

            // 콘텐츠 매핑 업데이트 -> SchedulePlay 기반
            await UpdateSchedulePlays(editingSchedule.Id);

            // 성공 알림
            var name = editingChannel?.Name ?? $"스케줄 #{editingSchedule.Id}";
            NotificationService.Notify(new NotificationMessage
            {
                Severity = NotificationSeverity.Success,
                Summary = "저장 완료",
                Detail = $"'{name}' 스케줄이 수정되었습니다.",
                Duration = 4000
            });

            // 선택된 스케줄 업데이트
            selectedSchedule = editingSchedule;

            // 수정된 콘텐츠 다시 로드
            await LoadScheduleContent(selectedSchedule.Id);

            // 목록 새로고침 (채널 캐시 포함)
            await LoadSchedules();

            // 편집 모드 종료
            isEditMode = false;
            editingSchedule = null;
            editingChannel = null;

            Logger.LogInformation($"Schedule updated: {name} (ID: {selectedSchedule.Id})");
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to update schedule");
            NotificationService.Notify(new NotificationMessage
            {
                Severity = NotificationSeverity.Error,
                Summary = "저장 실패",
                Detail = "스케줄 수정 중 오류가 발생했습니다.",
                Duration = 4000
            });
        }
        finally
        {
            isSaving = false;
            StateHasChanged();
        }
    }

    // 스케줄 콘텐츠 업데이트: SchedulePlay 사용
    private async Task UpdateSchedulePlays(ulong scheduleId)
    {
        try
        {
            // 기존 플레이들 가져오기
            var existingQuery = new Radzen.Query
            {
                Filter = $"ScheduleId eq {scheduleId} and (DeleteYn eq 'N' or DeleteYn eq null)"
            };
            var existingResult = await WicsService.GetSchedulePlays(existingQuery);
            var existing = existingResult.Value.AsODataEnumerable().ToList();

            var existingMediaIds = existing.Where(p => p.MediaId.HasValue).Select(p => p.MediaId!.Value).ToHashSet();
            var existingTtsIds = existing.Where(p => p.TtsId.HasValue).Select(p => p.TtsId!.Value).ToHashSet();

            // 삭제할 미디어 플레이
            var mediaToRemove = existing.Where(p => p.MediaId.HasValue && !editingSelectedMediaIds.Contains(p.MediaId!.Value));
            foreach (var play in mediaToRemove)
            {
                play.DeleteYn = "Y";
                play.UpdatedAt = DateTime.UtcNow;
                await WicsService.UpdateSchedulePlay(play.Id, play);
            }

            // 추가할 미디어 플레이
            var mediaToAdd = editingSelectedMediaIds.Except(existingMediaIds);
            foreach (var mediaId in mediaToAdd)
            {
                var newPlay = new SchedulePlay
                {
                    ScheduleId = scheduleId,
                    MediaId = mediaId,
                    TtsId = null,
                    Delay = 0,
                    DeleteYn = "N",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                await WicsService.CreateSchedulePlay(newPlay);
            }

            // 삭제할 TTS 플레이
            var ttsToRemove = existing.Where(p => p.TtsId.HasValue && !editingSelectedTtsIds.Contains(p.TtsId!.Value));
            foreach (var play in ttsToRemove)
            {
                play.DeleteYn = "Y";
                play.UpdatedAt = DateTime.UtcNow;
                await WicsService.UpdateSchedulePlay(play.Id, play);
            }

            // 추가할 TTS 플레이
            var ttsToAdd = editingSelectedTtsIds.Except(existingTtsIds);
            foreach (var ttsId in ttsToAdd)
            {
                var newPlay = new SchedulePlay
                {
                    ScheduleId = scheduleId,
                    MediaId = null,
                    TtsId = ttsId,
                    Delay = 0,
                    DeleteYn = "N",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                await WicsService.CreateSchedulePlay(newPlay);
            }

            Logger.LogInformation($"Updated schedule plays for schedule {scheduleId}");
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, $"Failed to update schedule plays for schedule {scheduleId}");
            throw;
        }
    }

    // 편집 모드에서 미디어 선택 토글
    private void ToggleMediaSelectionEdit(ulong mediaId)
    {
        if (editingSelectedMediaIds.Contains(mediaId))
        {
            editingSelectedMediaIds.Remove(mediaId);
        }
        else
        {
            editingSelectedMediaIds.Add(mediaId);
        }
        StateHasChanged();
    }

    // 편집 모드에서 TTS 선택 토글
    private void ToggleTtsSelectionEdit(ulong ttsId)
    {
        if (editingSelectedTtsIds.Contains(ttsId))
        {
            editingSelectedTtsIds.Remove(ttsId);
        }
        else
        {
            editingSelectedTtsIds.Add(ttsId);
        }
        StateHasChanged();
    }

    // 편집 모드에서 요일 선택 여부 확인
    private bool IsWeekdaySelectedInEdit(string dayCode)
    {
        if (editingSchedule == null) return false;

        return dayCode switch
        {
            "monday" => editingSchedule.Monday == "Y",
            "tuesday" => editingSchedule.Tuesday == "Y",
            "wednesday" => editingSchedule.Wednesday == "Y",
            "thursday" => editingSchedule.Thursday == "Y",
            "friday" => editingSchedule.Friday == "Y",
            "saturday" => editingSchedule.Saturday == "Y",
            "sunday" => editingSchedule.Sunday == "Y",
            _ => false
        };
    }

    // 편집 모드에서 요일 토글
    private void ToggleWeekdayInEdit(string dayCode)
    {
        if (editingSchedule == null) return;

        switch (dayCode)
        {
            case "monday":
                editingSchedule.Monday = editingSchedule.Monday == "Y" ? "N" : "Y";
                break;
            case "tuesday":
                editingSchedule.Tuesday = editingSchedule.Tuesday == "Y" ? "N" : "Y";
                break;
            case "wednesday":
                editingSchedule.Wednesday = editingSchedule.Wednesday == "Y" ? "N" : "Y";
                break;
            case "thursday":
                editingSchedule.Thursday = editingSchedule.Thursday == "Y" ? "N" : "Y";
                break;
            case "friday":
                editingSchedule.Friday = editingSchedule.Friday == "Y" ? "N" : "Y";
                break;
            case "saturday":
                editingSchedule.Saturday = editingSchedule.Saturday == "Y" ? "N" : "Y";
                break;
            case "sunday":
                editingSchedule.Sunday = editingSchedule.Sunday == "Y" ? "N" : "Y";
                break;
        }
        StateHasChanged();
    }

    // 편집 모드 - 모든 요일 선택
    private void SelectAllDaysEdit()
    {
        if (editingSchedule == null) return;

        editingSchedule.Monday = "Y";
        editingSchedule.Tuesday = "Y";
        editingSchedule.Wednesday = "Y";
        editingSchedule.Thursday = "Y";
        editingSchedule.Friday = "Y";
        editingSchedule.Saturday = "Y";
        editingSchedule.Sunday = "Y";
        StateHasChanged();
    }

    // 편집 모드 - 평일만 선택
    private void SelectWeekdaysEdit()
    {
        if (editingSchedule == null) return;

        editingSchedule.Monday = "Y";
        editingSchedule.Tuesday = "Y";
        editingSchedule.Wednesday = "Y";
        editingSchedule.Thursday = "Y";
        editingSchedule.Friday = "Y";
        editingSchedule.Saturday = "N";
        editingSchedule.Sunday = "N";
        StateHasChanged();
    }

    // 편집 모드 - 주말만 선택
    private void SelectWeekendEdit()
    {
        if (editingSchedule == null) return;

        editingSchedule.Monday = "N";
        editingSchedule.Tuesday = "N";
        editingSchedule.Wednesday = "N";
        editingSchedule.Thursday = "N";
        editingSchedule.Friday = "N";
        editingSchedule.Saturday = "Y";
        editingSchedule.Sunday = "Y";
        StateHasChanged();
    }

    // 편집 모드 - 모든 요일 선택 해제
    private void ClearWeekdaysEdit()
    {
        if (editingSchedule == null) return;

        editingSchedule.Monday = "N";
        editingSchedule.Tuesday = "N";
        editingSchedule.Wednesday = "N";
        editingSchedule.Thursday = "N";
        editingSchedule.Friday = "N";
        editingSchedule.Saturday = "N";
        editingSchedule.Sunday = "N";
        StateHasChanged();
    }

    // 최소 하나의 요일이 선택되었는지 확인
    private bool IsAnyWeekdaySelected()
    {
        if (editingSchedule == null) return false;

        return editingSchedule.Monday == "Y" ||
               editingSchedule.Tuesday == "Y" ||
               editingSchedule.Wednesday == "Y" ||
               editingSchedule.Thursday == "Y" ||
               editingSchedule.Friday == "Y" ||
               editingSchedule.Saturday == "Y" ||
               editingSchedule.Sunday == "Y";
    }

    // 스케줄 상태 배지 스타일 (DeleteYn 기반)
    private BadgeStyle GetScheduleBadgeStyle(string deleteYn) =>
        (deleteYn == "Y") ? BadgeStyle.Danger : BadgeStyle.Success;

    // 스케줄 상태 텍스트
    private string GetScheduleStateText(string deleteYn) =>
        (deleteYn == "Y") ? "비활성" : "활성";

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

    // 스케줄 삭제 확인 다이얼로그
    private async Task ConfirmDeleteSchedule(Schedule schedule)
    {
        var chName = GetChannelNameForSchedule(schedule);
        var result = await DialogService.Confirm(
            $"'{chName}' 스케줄을 삭제하시겠습니까?\n연결된 콘텐츠도 함께 삭제됩니다.",
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

            var chName = GetChannelNameForSchedule(schedule);
            Logger.LogInformation($"Deleting schedule: {chName} (ID: {schedule.Id})");

            // 1. 스케줄과 연결된 SchedulePlay 소프트 삭제
            var playsQuery = new Radzen.Query
            {
                Filter = $"ScheduleId eq {schedule.Id} and (DeleteYn eq 'N' or DeleteYn eq null)"
            };
            var playsResult = await WicsService.GetSchedulePlays(playsQuery);

            foreach (var play in playsResult.Value.AsODataEnumerable())
            {
                play.DeleteYn = "Y";
                play.UpdatedAt = DateTime.UtcNow;
                await WicsService.UpdateSchedulePlay(play.Id, play);
                Logger.LogInformation($"Soft deleted schedule play: {play.Id}");
            }

            // 2. 스케줄 자체 소프트 삭제
            schedule.DeleteYn = "Y";
            schedule.UpdatedAt = DateTime.UtcNow;
            await WicsService.UpdateSchedule(schedule.Id, schedule);

            Logger.LogInformation($"Successfully soft deleted schedule: {chName}");

            // 선택된 스케줄이 삭제된 스케줄이면 선택 해제
            if (selectedSchedule?.Id == schedule.Id)
            {
                selectedSchedule = null;
                isEditMode = false;
                editingSchedule = null;
                editingChannel = null;
            }

            // 스케줄 목록 새로고침
            await LoadSchedules();

            NotificationService.Notify(new NotificationMessage
            {
                Severity = NotificationSeverity.Success,
                Summary = "삭제 완료",
                Detail = $"'{chName}' 스케줄이 삭제되었습니다.",
                Duration = 4000
            });
        }
        catch (Exception ex)
        {
            var chName = GetChannelNameForSchedule(schedule);
            Logger.LogError(ex, $"Failed to delete schedule: {chName}");
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