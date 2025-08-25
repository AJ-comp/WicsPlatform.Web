using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Radzen;

namespace WicsPlatform.Client.Pages.SubPages
{
    public partial class BroadcastSpeakerSection
    {
        [Parameter] public WicsPlatform.Server.Models.wics.Channel Channel { get; set; }
        [Parameter] public bool IsCollapsed { get; set; }
        [Parameter] public EventCallback<bool> IsCollapsedChanged { get; set; }

        [Inject] protected NotificationService NotificationService { get; set; }
        [Inject] protected wicsService WicsService { get; set; }
        [Inject] protected ILogger<BroadcastSpeakerSection> Logger { get; set; }

        // 그룹 및 스피커 관련 필드
        private IEnumerable<WicsPlatform.Server.Models.wics.Group> speakerGroups = new List<WicsPlatform.Server.Models.wics.Group>();
        private List<ulong> selectedGroups = new List<ulong>();
        private bool isLoadingGroups = false;

        private IEnumerable<WicsPlatform.Server.Models.wics.Speaker> allSpeakers = new List<WicsPlatform.Server.Models.wics.Speaker>();
        private bool isLoadingSpeakers = false;

        private IEnumerable<WicsPlatform.Server.Models.wics.MapSpeakerGroup> speakerGroupMappings = new List<WicsPlatform.Server.Models.wics.MapSpeakerGroup>();

        protected override async Task OnInitializedAsync()
        {
            Logger.LogInformation("BroadcastSpeakerSection OnInitializedAsync 시작");
            await LoadSpeakerData();
        }

        private async Task TogglePanel()
        {
            await IsCollapsedChanged.InvokeAsync(!IsCollapsed);
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

                // ManageSpeaker와 동일한 쿼리 사용
                var query = new Radzen.Query
                {
                    Filter = "DeleteYn eq 'N' or DeleteYn eq null"  // Type eq 0 조건 제거
                };

                Logger.LogInformation($"스피커 그룹 쿼리: {query.Filter}");

                var result = await WicsService.GetGroups(query);

                // Type이 0인 것만 필터링 (스피커 그룹만)
                speakerGroups = result.Value.AsODataEnumerable()
                    .Where(g => g.Type == 0);  // 0: 스피커 그룹, 1: 플레이리스트

                Logger.LogInformation($"로드된 스피커 그룹 수: {speakerGroups.Count()}");

                foreach (var group in speakerGroups)
                {
                    Logger.LogInformation($"그룹: ID={group.Id}, Name={group.Name}, Type={group.Type}");
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "스피커 그룹 로드 실패");
                NotificationService.Notify(new NotificationMessage
                {
                    Severity = NotificationSeverity.Error,
                    Summary = "오류",
                    Detail = $"스피커 그룹을 불러오는 중 오류가 발생했습니다: {ex.Message}",
                    Duration = 4000
                });
            }
            finally
            {
                isLoadingGroups = false;
                await InvokeAsync(StateHasChanged);
            }
        }

        private async Task LoadAllSpeakers()
        {
            try
            {
                isLoadingSpeakers = true;
                StateHasChanged();

                // ManageSpeaker와 동일한 쿼리 사용
                var query = new Radzen.Query
                {
                    Expand = "Channel",  // Channel 정보도 포함
                    Filter = "DeleteYn eq 'N' or DeleteYn eq null"
                };

                Logger.LogInformation($"스피커 쿼리: {query.Filter}");

                var result = await WicsService.GetSpeakers(query);
                allSpeakers = result.Value.AsODataEnumerable();

                Logger.LogInformation($"로드된 스피커 수: {allSpeakers.Count()}");
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "스피커 로드 실패");
                NotificationService.Notify(new NotificationMessage
                {
                    Severity = NotificationSeverity.Error,
                    Summary = "오류",
                    Detail = $"스피커 목록을 불러오는 중 오류가 발생했습니다: {ex.Message}",
                    Duration = 4000
                });
            }
            finally
            {
                isLoadingSpeakers = false;
                await InvokeAsync(StateHasChanged);
            }
        }

        private async Task LoadSpeakerGroupMappings()
        {
            try
            {
                // ManageSpeaker와 동일한 쿼리 사용
                var query = new Radzen.Query
                {
                    Expand = "Group,Speaker",
                    Filter = "LastYn eq 'Y'"  // 활성 매핑만
                };

                Logger.LogInformation($"스피커 그룹 매핑 쿼리: {query.Filter}");

                var result = await WicsService.GetMapSpeakerGroups(query);
                speakerGroupMappings = result.Value.AsODataEnumerable();

                Logger.LogInformation($"로드된 매핑 수: {speakerGroupMappings.Count()}");
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "스피커 그룹 매핑 로드 실패");
                NotificationService.Notify(new NotificationMessage
                {
                    Severity = NotificationSeverity.Error,
                    Summary = "오류",
                    Detail = $"스피커 그룹 매핑을 불러오는 중 오류가 발생했습니다: {ex.Message}",
                    Duration = 4000
                });
            }
        }

        // 그룹 선택/해제
        protected async Task ToggleGroupSelection(ulong groupId)
        {
            if (selectedGroups.Contains(groupId))
            {
                selectedGroups.Remove(groupId);
                Logger.LogInformation($"그룹 선택 해제: {groupId}");
            }
            else
            {
                selectedGroups.Add(groupId);
                Logger.LogInformation($"그룹 선택: {groupId}");
            }

            await InvokeAsync(StateHasChanged);
        }

        // 그룹이 선택되었는지 확인
        protected bool IsGroupSelected(ulong groupId)
        {
            return selectedGroups.Contains(groupId);
        }

        // 그룹 내 스피커 수 가져오기
        protected int GetSpeakerCountInGroup(ulong groupId)
        {
            var count = speakerGroupMappings
                .Where(m => m.GroupId == groupId && m.LastYn == "Y")
                .Select(m => m.SpeakerId)
                .Distinct()
                .Count();

            Logger.LogDebug($"그룹 {groupId}의 스피커 수: {count}");
            return count;
        }

        // 스피커가 속한 그룹 목록 가져오기  
        public IEnumerable<string> GetSpeakerGroups(ulong speakerId)
        {
            return speakerGroupMappings
                .Where(m => m.SpeakerId == speakerId && m.LastYn == "Y" && m.Group != null)
                .Select(m => m.Group.Name)
                .Distinct();
        }

        // 스피커가 선택된 그룹에 속하는지 확인
        protected bool IsSpeakerInSelectedGroups(ulong speakerId)
        {
            return speakerGroupMappings
                .Any(m => m.SpeakerId == speakerId &&
                         m.LastYn == "Y" &&
                         selectedGroups.Contains(m.GroupId));
        }

        // 선택된 그룹의 총 스피커 수
        protected int GetSelectedSpeakersCount()
        {
            return speakerGroupMappings
                .Where(m => selectedGroups.Contains(m.GroupId) && m.LastYn == "Y")
                .Select(m => m.SpeakerId)
                .Distinct()
                .Count();
        }

        // 선택된 그룹의 온라인 스피커 가져오기
        public List<WicsPlatform.Server.Models.wics.Speaker> GetOnlineSpeakers()
        {
            var selectedSpeakerIds = speakerGroupMappings
                .Where(m => selectedGroups.Contains(m.GroupId) && m.LastYn == "Y")
                .Select(m => m.SpeakerId)
                .Distinct()
                .ToList();

            var onlineSpeakers = allSpeakers
                .Where(s => selectedSpeakerIds.Contains(s.Id) && s.State == 1)
                .ToList();

            Logger.LogInformation($"온라인 스피커 수: {onlineSpeakers.Count}");
            return onlineSpeakers;
        }

        // 선택된 그룹의 오프라인 스피커 가져오기
        public List<WicsPlatform.Server.Models.wics.Speaker> GetOfflineSpeakers()
        {
            var selectedSpeakerIds = speakerGroupMappings
                .Where(m => selectedGroups.Contains(m.GroupId) && m.LastYn == "Y")
                .Select(m => m.SpeakerId)
                .Distinct()
                .ToList();

            var offlineSpeakers = allSpeakers
                .Where(s => selectedSpeakerIds.Contains(s.Id) && s.State != 1)
                .ToList();

            Logger.LogInformation($"오프라인 스피커 수: {offlineSpeakers.Count}");
            return offlineSpeakers;
        }

        // 선택된 그룹 ID 목록 가져오기
        public List<ulong> GetSelectedGroups()
        {
            return selectedGroups.ToList();
        }

        // 선택 초기화
        public void ClearSelection()
        {
            selectedGroups.Clear();
            Logger.LogInformation("선택 초기화됨");
            StateHasChanged();
        }

        // UI Helper Methods
        protected string GetChannelIcon(byte type) => type switch
        {
            0 => "settings_input_antenna",
            1 => "podcasts",
            2 => "campaign",
            _ => "radio"
        };

        protected BadgeStyle GetSpeakerStatusBadgeStyle(byte state) =>
            state == 1 ? BadgeStyle.Success :
            state == 0 ? BadgeStyle.Danger : BadgeStyle.Light;

        protected string GetSpeakerStatusText(byte state) =>
            state == 1 ? "온라인" :
            state == 0 ? "오프라인" : "알 수 없음";
    }
}