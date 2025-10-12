using Microsoft.AspNetCore.Components;
using Radzen;
using WicsPlatform.Client.Dialogs;
using WicsPlatform.Client.Services;
using WicsPlatform.Server.Models.wics;

namespace WicsPlatform.Client.Pages;

public partial class ManageSchedule
{
    [Inject] protected wicsService WicsService { get; set; }
    [Inject] protected NotificationService NotificationService { get; set; }
    [Inject] protected DialogService DialogService { get; set; }
    [Inject] protected ILogger<ManageSchedule> Logger { get; set; }
    [Inject] protected BroadcastWebSocketService WebSocketService { get; set; }
    [Inject] protected NavigationManager NavigationManager { get; set; }

    // 스케줄 관련 필드
    private IEnumerable<Schedule> schedules = new List<Schedule>();
    private Schedule selectedSchedule = null;
    private bool isLoadingSchedules = false;
    private bool isDeletingSchedule = false;

    // 편집 관련 필드
    private Schedule editingSchedule = null;
    private Channel editingChannel = null;
    private bool isSaving = false;
    private DateTime? tempStartDateTime;
    private int tempRepeatCount;
    private int selectedTabIndex = 0;

    // 스케줄별 채널 캐시
    private readonly Dictionary<ulong, Channel> scheduleChannels = new();
    // 스케줄 활성 상태 캐시: 콘텐츠/스피커 설정 여부
    private readonly Dictionary<ulong, (bool HasContent, bool HasSpeakers)> scheduleActivation = new();

    // 콘텐츠 관련 필드
    private IEnumerable<Medium> availableMedia = new List<Medium>();
    private IEnumerable<Tt> availableTts = new List<Tt>();

    // 현재 스케줄의 콘텐츠
    private IEnumerable<Medium> currentScheduleMedia = new List<Medium>();
    private IEnumerable<Tt> currentScheduleTts = new List<Tt>();

    // 현재 스케줄의 SchedulePlay 순서(보기/초기화용) - Id 오름차순 = 저장 순서
    private List<(bool IsMedia, ulong Id)> existingPlayOrder = new();

    // 편집 중인 콘텐츠 선택 상태
    private HashSet<ulong> editingSelectedMediaIds = new HashSet<ulong>();
    private HashSet<ulong> editingSelectedTtsIds = new HashSet<ulong>();

    // 편집 중 선택 순서 추적 (가장 최근에 클릭한 항목이 마지막)
    private class SelectedContent
    {
        public bool IsMedia { get; set; }
        public ulong Id { get; set; }
        public int Seq { get; set; }
    }
    private readonly List<SelectedContent> editingSelectionOrder = new();
    private int nextSelectionSeq = 0;

    // 스피커/그룹 선택 (편집용)
    private IEnumerable<Server.Models.wics.Group> speakerGroups = new List<Server.Models.wics.Group>();
    private IEnumerable<Speaker> allSpeakers = new List<Speaker>();
    private IEnumerable<MapSpeakerGroup> speakerGroupMappings = new List<MapSpeakerGroup>();
    private bool isLoadingGroups = false;
    private bool isLoadingSpeakers = false;
    private HashSet<ulong> editingSelectedGroupIds = new();
    private HashSet<ulong> editingSelectedSpeakerIds = new();
    private Server.Models.wics.Group? viewingGroup = null;
    private HashSet<ulong> expandedGroups = new();

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
        await LoadSpeakerData();
    }

    // UTC로 저장된 TimeOnly를 로컬 시간대로 변환
    private static TimeOnly UtcTimeOnlyToLocal(TimeOnly utcTime)
    {
        var utcDateTime = DateTime.SpecifyKind(DateTime.UtcNow.Date.Add(utcTime.ToTimeSpan()), DateTimeKind.Utc);
        var local = utcDateTime.ToLocalTime();
        return TimeOnly.FromTimeSpan(local.TimeOfDay);
    }

    // 목록/UI 표시용 포맷터
    private string FormatLocalTime(TimeOnly utcTime)
        => UtcTimeOnlyToLocal(utcTime).ToString("HH:mm");

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
            scheduleActivation.Clear();
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


                    // 상태 캐시 채우기: 콘텐츠/스피커 존재 여부
                    var hasContent = await HasContentAsync(sch.Id);
                    var hasSpeakers = channel != null && await HasSpeakersAsync(channel.Id);
                    scheduleActivation[sch.Id] = (hasContent, hasSpeakers);
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

    private async Task<bool> HasContentAsync(ulong scheduleId)
    {
        try
        {
            var q = new Radzen.Query
            {
                Filter = $"ScheduleId eq {scheduleId} and (DeleteYn eq 'N' or DeleteYn eq null)",
                Top = 1
            };
            var result = await WicsService.GetSchedulePlays(q);
            return result.Value.AsODataEnumerable().Any();
        }
        catch
        {
            return false;
        }
    }

    private async Task<bool> HasSpeakersAsync(ulong channelId)
    {
        try
        {
            var q = new Radzen.Query
            {
                Filter = $"ChannelId eq {channelId} and (DeleteYn eq 'N' or DeleteYn eq null)",
                Top = 1
            };
            var result = await WicsService.GetMapChannelSpeakers(q);
            return result.Value.AsODataEnumerable().Any();
        }
        catch
        {
            return false;
        }
    }

    private (bool HasContent, bool HasSpeakers) GetActivationStatus(ulong scheduleId)
    {
        return scheduleActivation.TryGetValue(scheduleId, out var st)
            ? st
            : (HasContent: false, HasSpeakers: false);
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

    private async Task LoadSpeakerData()
    {
        await Task.WhenAll(
            LoadSpeakerGroups(),
            LoadAllSpeakers(),
            LoadSpeakerGroupMappings()
        );
    }

    private async Task LoadSpeakerGroups()
    {
        try
        {
            isLoadingGroups = true;
            StateHasChanged();

            var query = new Radzen.Query
            {
                Filter = "(DeleteYn eq 'N' or DeleteYn eq null) and Type eq 0"
            };
            var result = await WicsService.GetGroups(query);
            speakerGroups = result.Value.AsODataEnumerable();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to load speaker groups");
        }
        finally
        {
            isLoadingGroups = false;
            StateHasChanged();
        }
    }

    private async Task LoadAllSpeakers()
    {
        try
        {
            isLoadingSpeakers = true;
            StateHasChanged();

            var query = new Radzen.Query
            {
                Expand = "Channel",
                Filter = "DeleteYn eq 'N' or DeleteYn eq null"
            };
            var result = await WicsService.GetSpeakers(query);
            allSpeakers = result.Value.AsODataEnumerable();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to load speakers");
        }
        finally
        {
            isLoadingSpeakers = false;
            StateHasChanged();
        }
    }

    private async Task LoadSpeakerGroupMappings()
    {
        try
        {
            var query = new Radzen.Query
            {
                Expand = "Group,Speaker",
                Filter = "LastYn eq 'Y'"
            };
            var result = await WicsService.GetMapSpeakerGroups(query);
            speakerGroupMappings = result.Value.AsODataEnumerable();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to load speaker-group mappings");
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

            // 저장된 순서(=Id asc) 기록
            existingPlayOrder = plays
                .OrderBy(p => p.Id)
                .Select(p => (IsMedia: p.MediaId.HasValue, Id: p.MediaId ?? p.TtsId ?? 0))
                .Where(x => x.Id != 0)
                .ToList();

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
        selectedSchedule = schedule;

        // 편집용 객체 생성
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

        // TimeOnly(UTC 저장)를 로컬 시간대로 변환하여 DateTime으로 설정
        var localTimeOnly = UtcTimeOnlyToLocal(editingSchedule.StartTime);
        tempStartDateTime = DateTime.Today.Add(localTimeOnly.ToTimeSpan());
        tempRepeatCount = editingSchedule.RepeatCount;

        // 선택된 스케줄의 콘텐츠 로드
        await LoadScheduleContent(schedule.Id);

        // 현재 선택된 콘텐츠 ID 복사
        editingSelectedMediaIds = new HashSet<ulong>(currentScheduleMedia.Select(m => m.Id));
        editingSelectedTtsIds = new HashSet<ulong>(currentScheduleTts.Select(t => t.Id));

        // 초기 선택 순서 구성
        BuildInitialSelectionOrder();

        // 스피커/그룹 선택 초기화
        editingSelectedGroupIds.Clear();
        editingSelectedSpeakerIds.Clear();
        viewingGroup = null;
        expandedGroups.Clear();

        if (editingChannel != null)
        {
            await InitializeChannelSpeakerSelections(editingChannel.Id);
        }

        var name = GetChannelNameForSchedule(schedule);
        Logger.LogInformation($"Selected schedule: {name} (ID: {schedule.Id})");
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

            // DateTime을 TimeOnly로 변환 (로컬 -> UTC로 저장)
            if (tempStartDateTime.HasValue)
            {
                var local = DateTime.SpecifyKind(tempStartDateTime.Value, DateTimeKind.Local);
                var utc = local.ToUniversalTime();
                editingSchedule.StartTime = TimeOnly.FromTimeSpan(utc.TimeOfDay);
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

            // 콘텐츠 매핑 업데이트 -> SchedulePlay 기반 (순서 저장)
            await UpdateSchedulePlays(editingSchedule.Id);

            // 채널의 미디어 / TTS 매핑 (map_channel_media / map_channel_tts) 동기화
            if (editingChannel != null)
            {
                await UpdateChannelContentMappings(editingChannel.Id);
            }

            // 스피커/그룹 매핑 업데이트 -> 채널 기준
            if (editingChannel != null)
            {
                await UpdateChannelSpeakerMappings(editingChannel.Id);
            }

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

    // 채널의 map_channel_media / map_channel_tts 테이블을 현재 선택 상태(editingSelectedMediaIds, editingSelectedTtsIds)와 동기화
    private async Task UpdateChannelContentMappings(ulong channelId)
    {
        try
        {
            // 1) 기존 활성 미디어 매핑 로드
            var existingMediaQuery = new Radzen.Query
            {
                Filter = $"ChannelId eq {channelId} and (DeleteYn eq 'N' or DeleteYn eq null)"
            };
            var existingMediaResult = await WicsService.GetMapChannelMedia(existingMediaQuery);
            var existingMediaMaps = existingMediaResult.Value.AsODataEnumerable().ToList();
            var existingMediaIds = existingMediaMaps.Select(m => m.MediaId).ToHashSet();

            // 제거될 매핑: 현재 선택에 없음
            foreach (var map in existingMediaMaps.Where(m => !editingSelectedMediaIds.Contains(m.MediaId)))
            {
                map.DeleteYn = "Y";
                map.UpdatedAt = DateTime.UtcNow;
                await WicsService.UpdateMapChannelMedium(map.Id, map);
            }

            // 추가될 매핑
            var mediaToAdd = editingSelectedMediaIds.Except(existingMediaIds);
            foreach (var mid in mediaToAdd)
            {
                var newMap = new MapChannelMedium
                {
                    ChannelId = channelId,
                    MediaId = mid,
                    DeleteYn = "N",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                await WicsService.CreateMapChannelMedium(newMap);
            }

            // 2) 기존 활성 TTS 매핑 로드
            var existingTtsQuery = new Radzen.Query
            {
                Filter = $"ChannelId eq {channelId} and (DeleteYn eq 'N' or DeleteYn eq null)"
            };
            var existingTtsResult = await WicsService.GetMapChannelTts(existingTtsQuery);
            var existingTtsMaps = existingTtsResult.Value.AsODataEnumerable().ToList();
            var existingTtsIds = existingTtsMaps.Select(t => t.TtsId).ToHashSet();

            // 제거될 TTS 매핑
            foreach (var map in existingTtsMaps.Where(t => !editingSelectedTtsIds.Contains(t.TtsId)))
            {
                map.DeleteYn = "Y";
                // UpdatedAt 속성이 모델에 존재한다는 전제 (다른 코드에서도 사용)
                map.UpdatedAt = DateTime.UtcNow;
                await WicsService.UpdateMapChannelTt(map.Id, map);
            }

            // 추가될 TTS 매핑
            var ttsToAdd = editingSelectedTtsIds.Except(existingTtsIds);
            foreach (var tid in ttsToAdd)
            {
                var newMap = new MapChannelTt
                {
                    ChannelId = channelId,
                    TtsId = tid,
                    DeleteYn = "N",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                await WicsService.CreateMapChannelTt(newMap);
            }

            Logger.LogInformation($"Updated channel content mappings for channel {channelId}: media={editingSelectedMediaIds.Count}, tts={editingSelectedTtsIds.Count}");
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, $"Failed to update channel content mappings for channel {channelId}");
            throw; // 상위에서 실패 알림 처리
        }
    }

    // 스케줄 콘텐츠 업데이트: SchedulePlay 사용
    private async Task UpdateSchedulePlays(ulong scheduleId)
    {
        try
        {
            // 1) 기존 활성 플레이 모두 소프트 삭제 (정렬을 Id 순서에 의존하므로 재작성)
            var existingQuery = new Radzen.Query
            {
                Filter = $"ScheduleId eq {scheduleId} and (DeleteYn eq 'N' or DeleteYn eq null)"
            };
            var existingResult = await WicsService.GetSchedulePlays(existingQuery);
            var existing = existingResult.Value.AsODataEnumerable().ToList();

            foreach (var play in existing)
            {
                play.DeleteYn = "Y";
                play.UpdatedAt = DateTime.UtcNow;
                await WicsService.UpdateSchedulePlay(play.Id, play);
            }

            // 2) 현재 클릭 순서대로 재생 항목을 재생성 (Delay는 의미 없으므로 0)
            var ordered = editingSelectionOrder
                .Where(x => x.IsMedia ? editingSelectedMediaIds.Contains(x.Id) : editingSelectedTtsIds.Contains(x.Id))
                .OrderBy(x => x.Seq)
                .ToList();

            foreach (var item in ordered)
            {
                var newPlay = new SchedulePlay
                {
                    ScheduleId = scheduleId,
                    MediaId = item.IsMedia ? item.Id : null,
                    TtsId = item.IsMedia ? (ulong?)null : item.Id,
                    Delay = 0,
                    DeleteYn = "N",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                await WicsService.CreateSchedulePlay(newPlay);
            }

            Logger.LogInformation($"Recreated schedule plays for schedule {scheduleId} in click order: {ordered.Count} items");
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, $"Failed to update schedule plays (recreate in order) for schedule {scheduleId}");
            throw;
        }
    }

    // 편집 모드에서 미디어 선택 토글
    private void ToggleMediaSelection(ulong mediaId)
    {
        if (editingSelectedMediaIds.Contains(mediaId))
        {
            editingSelectedMediaIds.Remove(mediaId);
            RemoveFromSelectionOrder(true, mediaId);
        }
        else
        {
            editingSelectedMediaIds.Add(mediaId);
            AddToSelectionOrder(true, mediaId);
        }
        StateHasChanged();
    }

    // 편집 모드에서 TTS 선택 토글
    private void ToggleTtsSelection(ulong ttsId)
    {
        if (editingSelectedTtsIds.Contains(ttsId))
        {
            editingSelectedTtsIds.Remove(ttsId);
            RemoveFromSelectionOrder(false, ttsId);
        }
        else
        {
            editingSelectedTtsIds.Add(ttsId);
            AddToSelectionOrder(false, ttsId);
        }
        StateHasChanged();
    }

    // 선택 순서 유틸리티
    private void AddToSelectionOrder(bool isMedia, ulong id)
    {
        if (!editingSelectionOrder.Any(x => x.IsMedia == isMedia && x.Id == id))
        {
            editingSelectionOrder.Add(new SelectedContent
            {
                IsMedia = isMedia,
                Id = id,
                Seq = ++nextSelectionSeq
            });
        }
    }

    private void RemoveFromSelectionOrder(bool isMedia, ulong id)
    {
        editingSelectionOrder.RemoveAll(x => x.IsMedia == isMedia && x.Id == id);
    }

    private void BuildInitialSelectionOrder()
    {
        editingSelectionOrder.Clear();
        nextSelectionSeq = 0;

        // 1) 가능한 경우 기존 SchedulePlay의 저장 순서(Id asc)를 그대로 초기 순서로 사용
        if (existingPlayOrder.Any())
        {
            foreach (var item in existingPlayOrder)
            {
                // 현재 선택된 항목만 반영
                if ((item.IsMedia && editingSelectedMediaIds.Contains(item.Id)) ||
                    (!item.IsMedia && editingSelectedTtsIds.Contains(item.Id)))
                {
                    AddToSelectionOrder(item.IsMedia, item.Id);
                }
            }
        }
        else
        {
            // 2) 폴백: 생성일/ID 기준
            var initial = new List<(bool IsMedia, ulong Id, DateTime CreatedAt, ulong Ord)>();
            foreach (var m in currentScheduleMedia)
            {
                if (editingSelectedMediaIds.Contains(m.Id))
                    initial.Add((true, m.Id, m.CreatedAt, m.Id));
            }
            foreach (var t in currentScheduleTts)
            {
                if (editingSelectedTtsIds.Contains(t.Id))
                    initial.Add((false, t.Id, t.CreatedAt, t.Id));
            }

            foreach (var item in initial.OrderBy(x => x.CreatedAt).ThenBy(x => x.Ord))
            {
                AddToSelectionOrder(item.IsMedia, item.Id);
            }
        }
    }

    // UI 표시용 이름 조회
    private string GetContentName(bool isMedia, ulong id)
    {
        if (isMedia)
        {
            var m = availableMedia.FirstOrDefault(x => x.Id == id);
            return m?.FileName ?? $"미디어 #{id}";
        }
        else
        {
            var t = availableTts.FirstOrDefault(x => x.Id == id);
            return t?.Name ?? $"TTS #{id}";
        }
    }

    // ===== 스피커/그룹 편집 동작 =====
    private async Task InitializeChannelSpeakerSelections(ulong channelId)
    {
        try
        {
            // 그룹 매핑 로드
            var groupQuery = new Radzen.Query
            {
                Filter = $"ChannelId eq {channelId} and (DeleteYn eq 'N' or DeleteYn eq null)"
            };
            var groupMaps = await WicsService.GetMapChannelGroups(groupQuery);
            var groups = groupMaps.Value.AsODataEnumerable().ToList();
            editingSelectedGroupIds = groups.Select(g => g.GroupId).ToHashSet();

            // 스피커 매핑 로드
            var spQuery = new Radzen.Query
            {
                Filter = $"ChannelId eq {channelId} and (DeleteYn eq 'N' or DeleteYn eq null)"
            };
            var spMaps = await WicsService.GetMapChannelSpeakers(spQuery);
            var speakers = spMaps.Value.AsODataEnumerable().ToList();
            var mappedSpeakerIds = speakers.Select(s => s.SpeakerId).ToHashSet();

            if (mappedSpeakerIds.Any())
            {
                editingSelectedSpeakerIds = mappedSpeakerIds;
            }
            else if (editingSelectedGroupIds.Any())
            {
                // 그룹에서 스피커 유추
                var union = new HashSet<ulong>();
                foreach (var gid in editingSelectedGroupIds)
                {
                    foreach (var s in GetSpeakersInGroupSync(gid))
                    {
                        union.Add(s.Id);
                    }
                }
                editingSelectedSpeakerIds = union;
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, $"Failed to initialize channel speaker selections for channel {channelId}");
        }
    }

    private async Task UpdateChannelSpeakerMappings(ulong channelId)
    {
        try
        {
            // 기존 그룹 매핑 읽기
            var existingGroupsQuery = new Radzen.Query
            {
                Filter = $"ChannelId eq {channelId} and (DeleteYn eq 'N' or DeleteYn eq null)"
            };
            var existingGroupsResult = await WicsService.GetMapChannelGroups(existingGroupsQuery);
            var existingGroupMaps = existingGroupsResult.Value.AsODataEnumerable().ToList();

            var existingGroupIds = existingGroupMaps.Select(m => m.GroupId).ToHashSet();

            // 삭제할 그룹 매핑
            foreach (var toRemove in existingGroupMaps.Where(m => !editingSelectedGroupIds.Contains(m.GroupId)))
            {
                toRemove.DeleteYn = "Y";
                toRemove.UpdatedAt = DateTime.UtcNow;
                await WicsService.UpdateMapChannelGroup(toRemove.Id, toRemove);
            }

            // 추가할 그룹 매핑
            var groupsToAdd = editingSelectedGroupIds.Except(existingGroupIds);
            foreach (var gid in groupsToAdd)
            {
                var newMap = new MapChannelGroup
                {
                    ChannelId = channelId,
                    GroupId = gid,
                    DeleteYn = "N",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                await WicsService.CreateMapChannelGroup(newMap);
            }

            // 기존 스피커 매핑 읽기
            var existingSpeakersQuery = new Radzen.Query
            {
                Filter = $"ChannelId eq {channelId} and (DeleteYn eq 'N' or DeleteYn eq null)"
            };
            var existingSpeakersResult = await WicsService.GetMapChannelSpeakers(existingSpeakersQuery);
            var existingSpeakerMaps = existingSpeakersResult.Value.AsODataEnumerable().ToList();
            var existingSpeakerIds = existingSpeakerMaps.Select(m => m.SpeakerId).ToHashSet();

            // 삭제할 스피커 매핑
            foreach (var toRemove in existingSpeakerMaps.Where(m => !editingSelectedSpeakerIds.Contains(m.SpeakerId)))
            {
                toRemove.DeleteYn = "Y";
                toRemove.UpdatedAt = DateTime.UtcNow;
                await WicsService.UpdateMapChannelSpeaker(toRemove.Id, toRemove);
            }

            // 추가할 스피커 매핑
            var speakersToAdd = editingSelectedSpeakerIds.Except(existingSpeakerIds);
            foreach (var sid in speakersToAdd)
            {
                var newMap = new MapChannelSpeaker
                {
                    ChannelId = channelId,
                    SpeakerId = sid,
                    DeleteYn = "N",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                await WicsService.CreateMapChannelSpeaker(newMap);
            }

            Logger.LogInformation($"Updated channel-speaker mappings for channel {channelId}: groups={editingSelectedGroupIds.Count}, speakers={editingSelectedSpeakerIds.Count}");
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, $"Failed to update channel speaker mappings for channel {channelId}");
            throw;
        }
    }

    private bool IsGroupSelected(ulong groupId) => editingSelectedGroupIds.Contains(groupId);

    private void ToggleGroupSelection(ulong groupId)
    {
        if (editingSelectedGroupIds.Contains(groupId))
        {
            editingSelectedGroupIds.Remove(groupId);
            // 그룹 내 스피커 제거 (다른 선택된 그룹에도 속한 스피커는 유지)
            var groupSpeakerIds = GetSpeakersInGroupSync(groupId).Select(s => s.Id).ToList();
            foreach (var sid in groupSpeakerIds)
            {
                // 이 스피커가 다른 선택 그룹에도 포함되지 않으면 제거
                var inOther = editingSelectedGroupIds.Any(gid => GetSpeakersInGroupSync(gid).Any(s => s.Id == sid));
                if (!inOther && editingSelectedSpeakerIds.Contains(sid))
                {
                    editingSelectedSpeakerIds.Remove(sid);
                }
            }
        }
        else
        {
            editingSelectedGroupIds.Add(groupId);
            // 그룹 내 스피커 모두 추가
            foreach (var s in GetSpeakersInGroupSync(groupId))
            {
                editingSelectedSpeakerIds.Add(s.Id);
            }
        }
        StateHasChanged();
    }

    private void SetGroupSelection(ulong groupId, bool selected)
    {
        var currentlySelected = editingSelectedGroupIds.Contains(groupId);
        if (selected == currentlySelected)
        {
            return;
        }
        ToggleGroupSelection(groupId);
    }

    private void ToggleGroupExpansion(ulong groupId)
    {
        if (!expandedGroups.Add(groupId))
        {
            expandedGroups.Remove(groupId);
        }
        StateHasChanged();
    }

    private bool IsExpanded(ulong groupId) => expandedGroups.Contains(groupId);

    private void ViewGroupDetails(Server.Models.wics.Group group)
    {
        viewingGroup = group;
        StateHasChanged();
    }

    private bool IsSpeakerSelected(ulong speakerId) => editingSelectedSpeakerIds.Contains(speakerId);

    private void ToggleSpeakerSelection(ulong speakerId)
    {
        if (editingSelectedSpeakerIds.Contains(speakerId))
        {
            editingSelectedSpeakerIds.Remove(speakerId);
        }
        else
        {
            editingSelectedSpeakerIds.Add(speakerId);
        }
        StateHasChanged();
    }

    private void SetSpeakerSelection(ulong speakerId, bool selected)
    {
        if (selected)
        {
            editingSelectedSpeakerIds.Add(speakerId);
        }
        else
        {
            editingSelectedSpeakerIds.Remove(speakerId);
        }
        StateHasChanged();
    }

    private void SelectAllSpeakersInViewingGroup()
    {
        if (viewingGroup == null) return;
        foreach (var s in GetSpeakersInGroupSync(viewingGroup.Id))
        {
            editingSelectedSpeakerIds.Add(s.Id);
        }
        StateHasChanged();
    }

    private void DeselectAllSpeakersInViewingGroup()
    {
        if (viewingGroup == null) return;
        var ids = GetSpeakersInGroupSync(viewingGroup.Id).Select(s => s.Id).ToHashSet();
        editingSelectedSpeakerIds.RemoveWhere(id => ids.Contains(id));
        StateHasChanged();
    }

    private void ClearSpeakerSelections()
    {
        editingSelectedGroupIds.Clear();
        editingSelectedSpeakerIds.Clear();
        viewingGroup = null;
        expandedGroups.Clear();
        StateHasChanged();
    }

    // 요일 선택 여부 확인
    private bool IsWeekdaySelected(string dayCode)
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

    // 요일 토글
    private void ToggleWeekday(string dayCode)
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

    // 모든 요일 선택
    private void SelectAllDays()
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

    // 평일만 선택
    private void SelectWeekdays()
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

    // 주말만 선택
    private void SelectWeekend()
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

    // 모든 요일 선택 해제
    private void ClearWeekdays()
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

    // 스케줄 상태 배지/텍스트 (콘텐츠+스피커 설정 여부 기준)
    private BadgeStyle GetScheduleBadgeStyle(Schedule schedule)
    {
        if (schedule.DeleteYn == "Y") return BadgeStyle.Danger;
        
        // 채널 상태가 1이면 방송 중
        var channel = GetChannelForSchedule(schedule.Id);
        if (channel?.State == 1) return BadgeStyle.Primary;
        
        var status = GetActivationStatus(schedule.Id);
        return (status.HasContent && status.HasSpeakers) ? BadgeStyle.Success : BadgeStyle.Warning;
    }

    private string GetScheduleStateText(Schedule schedule)
    {
        if (schedule.DeleteYn == "Y") return "비활성";
        
        // 채널 상태가 1이면 방송 중
        var channel = GetChannelForSchedule(schedule.Id);
        if (channel?.State == 1) return "방송 중";
        
        var status = GetActivationStatus(schedule.Id);
        return (status.HasContent && status.HasSpeakers) ? "대기 중" : "미설정";
    }

    // 스케줄이 방송 중인지 확인
    private bool IsScheduleBroadcasting(Schedule schedule)
    {
        var channel = GetChannelForSchedule(schedule.Id);
        return channel?.State == 1;
    }

    // 예약 방송 정지 핸들러
    private async Task StopScheduleBroadcast(Schedule schedule)
    {
        try
        {
            var channel = GetChannelForSchedule(schedule.Id);
            if (channel == null)
            {
                NotificationService.Notify(new NotificationMessage
                {
                    Severity = NotificationSeverity.Warning,
                    Summary = "정지 실패",
                    Detail = "채널 정보를 찾을 수 없습니다.",
                    Duration = 4000
                });
                return;
            }

            // 1. 진행 중인 Broadcast ID 찾기
            var query = new Radzen.Query
            {
                Filter = $"ChannelId eq {channel.Id} and OngoingYn eq 'Y'"
            };
            var broadcasts = await WicsService.GetBroadcasts(query);
            var ongoingBroadcast = broadcasts.Value.FirstOrDefault();

            if (ongoingBroadcast == null)
            {
                NotificationService.Notify(new NotificationMessage
                {
                    Severity = NotificationSeverity.Warning,
                    Summary = "정지 실패",
                    Detail = "진행 중인 방송을 찾을 수 없습니다.",
                    Duration = 4000
                });
                return;
            }

            Logger.LogInformation($"Stopping scheduled broadcast: {channel.Name} (Broadcast ID: {ongoingBroadcast.Id})");

            // 2. 방송 종료 API 호출 - 모든 정리 작업 수행
            var httpClient = new HttpClient { BaseAddress = new Uri(NavigationManager.BaseUri) };
            var response = await httpClient.PostAsync($"api/broadcasts/{ongoingBroadcast.Id}/finalize", null);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                Logger.LogError($"Failed to finalize broadcast: {errorContent}");
                
                NotificationService.Notify(new NotificationMessage
                {
                    Severity = NotificationSeverity.Error,
                    Summary = "방송 중지 실패",
                    Detail = "방송 중지 요청이 실패했습니다.",
                    Duration = 4000
                });
                return;
            }

            Logger.LogInformation($"Successfully stopped scheduled broadcast: {channel.Name}");

            // 3. 캐시 갱신
            await LoadSchedules();

            NotificationService.Notify(new NotificationMessage
            {
                Severity = NotificationSeverity.Success,
                Summary = "방송 중지 완료",
                Detail = $"'{channel.Name}' 예약 방송이 중지되었습니다.",
                Duration = 4000
            });

            await InvokeAsync(StateHasChanged);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, $"Failed to stop scheduled broadcast for schedule {schedule.Id}");
            NotificationService.Notify(new NotificationMessage
            {
                Severity = NotificationSeverity.Error,
                Summary = "오류",
                Detail = "방송 중지 중 오류가 발생했습니다.",
                Duration = 4000
            });
        }
    }

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
            $"'{chName}' 스케줄을 삭제하시겠습니까?\n연결된 콘텐츠 및 채널 매핑도 함께 삭제됩니다.",
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

            // 0. 스케줄과 연결된 채널 목록 가져오기
            var channelsQuery = new Radzen.Query
            {
                Filter = $"ScheduleId eq {schedule.Id} and (DeleteYn eq 'N' or DeleteYn eq null)"
            };
            var channelsResult = await WicsService.GetChannels(channelsQuery);
            var linkedChannels = channelsResult.Value.AsODataEnumerable().ToList();

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

            // 2. 채널 관련 맵 데이터 소프트 삭제 (미디어/tts/그룹/스피커)
            foreach (var ch in linkedChannels)
            {
                // MapChannelMedia
                var mapMediaQuery = new Radzen.Query { Filter = $"ChannelId eq {ch.Id} and (DeleteYn eq 'N' or DeleteYn eq null)" };
                var mapMediaResult = await WicsService.GetMapChannelMedia(mapMediaQuery);
                foreach (var m in mapMediaResult.Value.AsODataEnumerable())
                {
                    m.DeleteYn = "Y";
                    m.UpdatedAt = DateTime.UtcNow;
                    await WicsService.UpdateMapChannelMedium(m.Id, m);
                }

                // MapChannelTts
                var mapTtsQuery = new Radzen.Query { Filter = $"ChannelId eq {ch.Id} and (DeleteYn eq 'N' or DeleteYn eq null)" };
                var mapTtsResult = await WicsService.GetMapChannelTts(mapTtsQuery);
                foreach (var t in mapTtsResult.Value.AsODataEnumerable())
                {
                    t.DeleteYn = "Y";
                    t.UpdatedAt = DateTime.UtcNow;
                    await WicsService.UpdateMapChannelTt(t.Id, t);
                }

                // MapChannelGroups
                var mapGroupsQuery = new Radzen.Query { Filter = $"ChannelId eq {ch.Id} and (DeleteYn eq 'N' or DeleteYn eq null)" };
                var mapGroupsResult = await WicsService.GetMapChannelGroups(mapGroupsQuery);
                foreach (var g in mapGroupsResult.Value.AsODataEnumerable())
                {
                    g.DeleteYn = "Y";
                    g.UpdatedAt = DateTime.UtcNow;
                    await WicsService.UpdateMapChannelGroup(g.Id, g);
                }

                // MapChannelSpeakers
                var mapSpeakersQuery = new Radzen.Query { Filter = $"ChannelId eq {ch.Id} and (DeleteYn eq 'N' or DeleteYn eq null)" };
                var mapSpeakersResult = await WicsService.GetMapChannelSpeakers(mapSpeakersQuery);
                foreach (var s in mapSpeakersResult.Value.AsODataEnumerable())
                {
                    s.DeleteYn = "Y";
                    s.UpdatedAt = DateTime.UtcNow;
                    await WicsService.UpdateMapChannelSpeaker(s.Id, s);
                }

                // 채널 소프트 삭제
                ch.DeleteYn = "Y";
                ch.UpdatedAt = DateTime.UtcNow;
                await WicsService.UpdateChannel(ch.Id, ch);
                Logger.LogInformation($"Soft deleted channel: {ch.Id}");
            }

            // 3. 스케줄 자기 소프트 삭제
            schedule.DeleteYn = "Y";
            schedule.UpdatedAt = DateTime.UtcNow;
            await WicsService.UpdateSchedule(schedule.Id, schedule);

            Logger.LogInformation($"Successfully soft deleted schedule and related data: {chName}");

            // 선택된 스케줄이 삭제된 스케줄이면 선택 해제
            if (selectedSchedule?.Id == schedule.Id)
            {
                selectedSchedule = null;
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

    private IEnumerable<Speaker> GetSpeakersInGroupSync(ulong groupId)
    {
        var speakerIds = speakerGroupMappings
            .Where(m => m.GroupId == groupId && m.LastYn == "Y")
            .Select(m => m.SpeakerId)
            .Distinct();

        return allSpeakers.Where(s => speakerIds.Contains(s.Id));
    }

    private int GetSpeakerCountInGroup(ulong groupId)
    {
        return speakerGroupMappings
            .Where(m => m.GroupId == groupId && m.LastYn == "Y")
            .Select(m => m.SpeakerId)
            .Distinct()
            .Count();
    }

    private int GetOnlineSpeakerCount(ulong groupId)
    {
        var ids = speakerGroupMappings
            .Where(m => m.GroupId == groupId && m.LastYn == "Y")
            .Select(m => m.SpeakerId)
            .Distinct();
        return allSpeakers.Count(s => ids.Contains(s.Id) && s.State == 1);
    }

    private List<Speaker> GetOnlineSpeakersSelected() => allSpeakers.Where(s => editingSelectedSpeakerIds.Contains(s.Id) && s.State == 1).ToList();
    private List<Speaker> GetOfflineSpeakersSelected() => allSpeakers.Where(s => editingSelectedTtsIds.Contains(s.Id) && s.State != 1).ToList();

    private BadgeStyle GetSpeakerStatusBadgeStyle(byte state) => state switch
    {
        1 => BadgeStyle.Success,
        2 => BadgeStyle.Info,
        _ => BadgeStyle.Secondary
    };

    private string GetSpeakerStatusText(byte state) => state switch
    {
        1 => "온라인",
        2 => "유휴",
        _ => "오프라인"
    };
}