using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Net.Http;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using Radzen;
using WicsPlatform.Client.Dialogs;

namespace WicsPlatform.Client.Pages.SubPages
{
    public partial class BroadcastSpeakerSection
    {
        [Parameter] public WicsPlatform.Server.Models.wics.Channel Channel { get; set; }
        [Parameter] public bool IsCollapsed { get; set; }
        [Parameter] public EventCallback<bool> IsCollapsedChanged { get; set; }

        [Inject] protected NotificationService NotificationService { get; set; }
        [Inject] protected DialogService DialogService { get; set; }
        [Inject] protected wicsService WicsService { get; set; }
        [Inject] protected ILogger<BroadcastSpeakerSection> Logger { get; set; }
        [Inject] protected HttpClient Http { get; set; }
        [Inject] protected IJSRuntime JSRuntime { get; set; }

        private IJSObjectReference jsModule;
        private DotNetObjectReference<BroadcastSpeakerSection> dotNetRef;

        // 그룹 및 스피커 관련 필드
        private IEnumerable<WicsPlatform.Server.Models.wics.Group> speakerGroups = new List<WicsPlatform.Server.Models.wics.Group>();
        private List<ulong> selectedGroups = new List<ulong>();
        private bool isLoadingGroups = false;

        private IEnumerable<WicsPlatform.Server.Models.wics.Speaker> allSpeakers = new List<WicsPlatform.Server.Models.wics.Speaker>();
        private bool isLoadingSpeakers = false;

        private IEnumerable<WicsPlatform.Server.Models.wics.MapSpeakerGroup> speakerGroupMappings = new List<WicsPlatform.Server.Models.wics.MapSpeakerGroup>();

        // 확장된 그룹 추적
        private HashSet<ulong> expandedGroups = new HashSet<ulong>();

        // 현재 보고 있는 그룹
        private WicsPlatform.Server.Models.wics.Group viewingGroup = null;

        // 개별 스피커 선택 추적
        private HashSet<ulong> selectedSpeakers = new HashSet<ulong>();

        // 드래그 앤 드롭 관련 필드
        private bool isDragging = false;
        private ulong? isDraggingOverGroup = null;
        private bool isAddingToGroup = false;
        private int addProgress = 0;
        private int addTotal = 0;

        protected int AddProgressPercent => addTotal == 0 ? 0 : (int)((double)addProgress / addTotal * 100);

        private WicsPlatform.Server.Models.wics.Channel _previousChannel = null;
        private Task _loadingMappingsTask = null;

        protected override async Task OnInitializedAsync()
        {
            Logger.LogInformation("BroadcastSpeakerSection OnInitializedAsync 시작");
            await LoadSpeakerData();
        }

        protected override Task OnParametersSetAsync()
        {
            // 채널 변경 추적만 수행
            // LoadChannelMappings는 복구 프로세스에서 명시적으로 호출됨
            if (Channel != null && Channel != _previousChannel)
            {
                _previousChannel = Channel;
                Logger.LogInformation($"OnParametersSetAsync: 채널 변경 감지 - {Channel.Id}");
            }
            return Task.CompletedTask;
        }

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            if (firstRender)
            {
                try
                {
                    // 버전 쿼리 스트링으로 캐시 무효화
                    var version = DateTime.Now.Ticks.ToString();
                    jsModule = await JSRuntime.InvokeAsync<IJSObjectReference>("import", $"./js/dragdrop.js?v={version}");
                    dotNetRef = DotNetObjectReference.Create(this);
                    await jsModule.InvokeVoidAsync("initializeDragDrop", dotNetRef);
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "JavaScript 모듈 로드 실패");
                }
            }
        }

        protected void OnRowRender(RowRenderEventArgs<WicsPlatform.Server.Models.wics.Speaker> args)
        {
            if (IsSpeakerSelected(args.Data.Id))
            {
                args.Attributes.Add("draggable", "true");
                
                if (args.Attributes.TryGetValue("class", out var existingClass))
                {
                    args.Attributes["class"] = $"{existingClass} speaker-draggable";
                }
                else
                {
                    args.Attributes.Add("class", "speaker-draggable");
                }
            }
        }

        [JSInvokable]
        public async Task HandleDropFromJS(ulong groupId)
        {
            Logger.LogInformation($"===== HandleDropFromJS 호출됨! GroupId: {groupId}, 선택된 스피커 수: {selectedSpeakers.Count} =====");
            
            var group = speakerGroups.FirstOrDefault(g => g.Id == groupId);
            if (group != null && selectedSpeakers.Any())
            {
                await AddSelectedSpeakersToGroup(group);
            }
            
            isDragging = false;
            isDraggingOverGroup = null;
            await InvokeAsync(StateHasChanged);
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

                var query = new Radzen.Query
                {
                    Filter = "DeleteYn eq 'N' or DeleteYn eq null"
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

                var query = new Radzen.Query
                {
                    Expand = "Channel",
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
                var query = new Radzen.Query
                {
                    Expand = "Group,Speaker",
                    Filter = "LastYn eq 'Y'"
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

        public async Task LoadChannelMappings()
        {
            if (Channel == null)
            {
                selectedGroups.Clear();
                selectedSpeakers.Clear();
                return;
            }

            // 중복 호출 방지 - 이미 로드 중이면 해당 Task 완료를 기다림
            if (_loadingMappingsTask != null && !_loadingMappingsTask.IsCompleted)
            {
                Logger.LogInformation($"채널 {Channel.Id}의 매핑 로드가 이미 진행 중 - 완료 대기");
                await _loadingMappingsTask;
                return;
            }

            _loadingMappingsTask = LoadChannelMappingsInternal();
            await _loadingMappingsTask;
        }

        private async Task LoadChannelMappingsInternal()
        {
            try
            {
                Logger.LogInformation($"채널 {Channel.Id}의 그룹/스피커 매핑 로드 시작");

                // 채널에 할당된 그룹 로드 (스피커 그룹만, Type=0)
                var groupQuery = new Radzen.Query
                {
                    Filter = $"ChannelId eq {Channel.Id} and (DeleteYn eq 'N' or DeleteYn eq null)"
                };
                
                var groupMaps = await WicsService.GetMapChannelGroups(groupQuery);
                
                // Type=0인 스피커 그룹만 필터링
                var channelGroupIds = new List<ulong>();
                foreach (var mapping in groupMaps.Value.AsODataEnumerable())
                {
                    var groupQuery2 = new Radzen.Query { Filter = $"Id eq {mapping.GroupId}" };
                    var groups = await WicsService.GetGroups(groupQuery2);
                    var group = groups.Value.FirstOrDefault();
                    
                    Logger.LogInformation($"그룹 조회: GroupId={mapping.GroupId}, Found={group != null}, Type={group?.Type}");
                    
                    if (group != null && group.Type == 0) // Type 0 = 스피커 그룹
                    {
                        Logger.LogInformation($"✓ 스피커 그룹 추가: GroupId={mapping.GroupId}");
                        channelGroupIds.Add(mapping.GroupId);
                    }
                    else
                    {
                        Logger.LogInformation($"✗ 스피커 그룹 아님 - 건너뜀: GroupId={mapping.GroupId}, Type={group?.Type}");
                    }
                }

                // 채널에 할당된 스피커 로드 (map_channel_speaker만 사용)
                var speakerQuery = new Radzen.Query
                {
                    Filter = $"ChannelId eq {Channel.Id} and (DeleteYn eq 'N' or DeleteYn eq null)"
                };
                
                var speakerMaps = await WicsService.GetMapChannelSpeakers(speakerQuery);
                var channelSpeakerIds = speakerMaps.Value.AsODataEnumerable().Select(s => s.SpeakerId).ToHashSet();

                // 그룹 선택 상태 설정 (UI 편의 기능)
                selectedGroups.Clear();
                selectedGroups.AddRange(channelGroupIds);

                // 스피커 선택 상태 설정 (map_channel_speaker만 사용, 그룹과 무관)
                // 그룹은 단지 빠른 선택을 위한 UI 기능일 뿐이므로
                // 최종 선택된 스피커 목록만 복구
                selectedSpeakers.Clear();
                foreach (var speakerId in channelSpeakerIds)
                {
                    selectedSpeakers.Add(speakerId);
                }

                Logger.LogInformation($"채널 {Channel.Id}: {selectedGroups.Count}개 그룹, {selectedSpeakers.Count}개 스피커 선택됨");
                
                await InvokeAsync(StateHasChanged);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, $"채널 {Channel.Id}의 매핑 로드 실패");
            }
        }

        // 그룹 선택 변경
        private void OnGroupSelectionChanged(ulong groupId, bool isSelected)
        {
            if (isSelected)
            {
                if (!selectedGroups.Contains(groupId))
                {
                    selectedGroups.Add(groupId);
                    Logger.LogInformation($"그룹 선택: {groupId}");

                    // 그룹 선택 시 해당 그룹의 모든 스피커도 선택
                    var speakersInGroup = GetSpeakersInGroupSync(groupId).Select(s => s.Id);
                    foreach (var speakerId in speakersInGroup)
                    {
                        selectedSpeakers.Add(speakerId);
                    }
                }
            }
            else
            {
                selectedGroups.Remove(groupId);
                Logger.LogInformation($"그룹 선택 해제: {groupId}");

                // 그룹 선택 해제 시 해당 그룹의 모든 스피커도 선택 해제
                var speakersInGroup = GetSpeakersInGroupSync(groupId).Select(s => s.Id);
                foreach (var speakerId in speakersInGroup)
                {
                    selectedSpeakers.Remove(speakerId);
                }
            }

            StateHasChanged();
        }

        // 개별 스피커 선택 변경
        private async Task OnSpeakerSelectionChanged(ulong speakerId, bool isSelected)
        {
            if (isSelected)
            {
                selectedSpeakers.Add(speakerId);
                Logger.LogInformation($"스피커 선택: {speakerId}");
            }
            else
            {
                selectedSpeakers.Remove(speakerId);
                Logger.LogInformation($"스피커 선택 해제: {speakerId}");
            }

            StateHasChanged();
            
            // JavaScript 드래그 핸들러 새로고침
            if (jsModule != null && dotNetRef != null)
            {
                try
                {
                    await jsModule.InvokeVoidAsync("refreshDragDrop", dotNetRef);
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "refreshDragDrop 호출 실패");
                }
            }
        }

        // 스피커 선택 여부 확인
        protected bool IsSpeakerSelected(ulong speakerId)
        {
            return selectedSpeakers.Contains(speakerId);
        }

        // 그룹 상세 보기 및 선택 토글
        private void ViewGroupDetails(WicsPlatform.Server.Models.wics.Group group)
        {
            viewingGroup = group;
            
            // 그룹 선택 상태 토글
            bool isCurrentlySelected = selectedGroups.Contains(group.Id);
            OnGroupSelectionChanged(group.Id, !isCurrentlySelected);
            
            Logger.LogInformation($"그룹 클릭: {group.Name} (ID: {group.Id}), 선택: {!isCurrentlySelected}");
        }

        // 그룹 확장/축소 토글
        private void ToggleGroupExpansion(ulong groupId)
        {
            if (expandedGroups.Contains(groupId))
            {
                expandedGroups.Remove(groupId);
            }
            else
            {
                expandedGroups.Add(groupId);
            }
            StateHasChanged();
        }

        // 그룹 선택/해제 (public으로 유지 - ManageBroadCast에서 호출)
        public async Task ToggleGroupSelection(ulong groupId)
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

        // 그룹 내 온라인 스피커 수 가져오기
        protected int GetOnlineSpeakerCount(ulong groupId)
        {
            var speakerIds = speakerGroupMappings
                .Where(m => m.GroupId == groupId && m.LastYn == "Y")
                .Select(m => m.SpeakerId)
                .Distinct();

            return allSpeakers
                .Where(s => speakerIds.Contains(s.Id) && s.State == 1)
                .Count();
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

        // 선택된 그룹의 온라인 스피커 가져오기 (개별 선택 반영)
        public List<WicsPlatform.Server.Models.wics.Speaker> GetOnlineSpeakers()
        {
            // 개별 선택된 스피커만 사용
            var onlineSpeakers = allSpeakers
                .Where(s => selectedSpeakers.Contains(s.Id) && s.State == 1)
                .ToList();

            return onlineSpeakers;
        }

        // 선택된 그룹의 오프라인 스피커 가져오기 (개별 선택 반영)
        public List<WicsPlatform.Server.Models.wics.Speaker> GetOfflineSpeakers()
        {
            // 개별 선택된 스피커만 사용
            var offlineSpeakers = allSpeakers
                .Where(s => selectedSpeakers.Contains(s.Id) && s.State != 1)
                .ToList();

            return offlineSpeakers;
        }

        // 선택된 그룹 ID 목록 가져오기
        public List<ulong> GetSelectedGroups()
        {
            return selectedGroups.ToList();
        }

        // 선택된 스피커 ID 목록 가져오기
        public List<ulong> GetSelectedSpeakers()
        {
            return selectedSpeakers.ToList();
        }

        // 선택 초기화
        public void ClearSelection()
        {
            selectedGroups.Clear();
            selectedSpeakers.Clear();
            Logger.LogInformation("선택 초기화됨");
            StateHasChanged();
        }

        // 동기식으로 그룹의 스피커 목록 가져오기
        public IEnumerable<WicsPlatform.Server.Models.wics.Speaker> GetSpeakersInGroupSync(ulong groupId)
        {
            var speakerIds = speakerGroupMappings
                .Where(m => m.GroupId == groupId && m.LastYn == "Y")
                .Select(m => m.SpeakerId)
                .Distinct();

            return allSpeakers.Where(s => speakerIds.Contains(s.Id));
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
            state == 0 ? BadgeStyle.Light : BadgeStyle.Light;

        protected string GetSpeakerStatusText(byte state) =>
            state == 1 ? "온라인" :
            state == 0 ? "오프라인" : "알 수 없음";

        // 드래그 앤 드롭 핸들러
        protected void HandleDragStart(WicsPlatform.Server.Models.wics.Speaker speaker)
        {
            isDragging = true;
            Logger.LogInformation($"드래그 시작: {speaker.Name} (ID: {speaker.Id})");
        }

        protected void HandleDragEnd(DragEventArgs e)
        {
            isDragging = false;
            isDraggingOverGroup = null;
            Logger.LogInformation("드래그 종료");
        }

        protected void HandleDragEnter(WicsPlatform.Server.Models.wics.Group group)
        {
            if (isDragging && selectedSpeakers.Any())
            {
                isDraggingOverGroup = group.Id;
                Logger.LogInformation($"드래그 오버: {group.Name}");
            }
        }

        protected void HandleDragLeave(WicsPlatform.Server.Models.wics.Group group)
        {
            if (isDraggingOverGroup == group.Id)
            {
                isDraggingOverGroup = null;
            }
        }


        // 선택된 스피커들을 그룹에 추가
        protected async Task AddSelectedSpeakersToGroup(WicsPlatform.Server.Models.wics.Group group)
        {
            if (!selectedSpeakers.Any())
            {
                NotificationService.Notify(new NotificationMessage
                {
                    Severity = NotificationSeverity.Warning,
                    Summary = "선택 필요",
                    Detail = "추가할 스피커를 선택하세요.",
                    Duration = 3000
                });
                return;
            }

            if (isAddingToGroup)
            {
                Logger.LogInformation("이미 처리 중입니다.");
                return;
            }

            try
            {
                isAddingToGroup = true;
                StateHasChanged();

                var speakersToAdd = allSpeakers.Where(s => selectedSpeakers.Contains(s.Id)).ToList();

                addProgress = 0;
                addTotal = speakersToAdd.Count;
                StateHasChanged();

                int successCount = 0;
                int failCount = 0;
                int alreadyExistsCount = 0;

                await LoadSpeakerGroupMappings();

                foreach (var speaker in speakersToAdd)
                {
                    try
                    {
                        var existingMapping = speakerGroupMappings
                            .FirstOrDefault(m => m.SpeakerId == speaker.Id && m.GroupId == group.Id && m.LastYn == "Y");

                        if (existingMapping != null)
                        {
                            alreadyExistsCount++;
                            continue;
                        }

                        var mapSpeakerGroup = new WicsPlatform.Server.Models.wics.MapSpeakerGroup
                        {
                            SpeakerId = speaker.Id,
                            GroupId = group.Id,
                            LastYn = "Y",
                            CreatedAt = DateTime.Now,
                            UpdatedAt = DateTime.Now
                        };

                        var response = await Http.PostAsJsonAsync("odata/wics/MapSpeakerGroups", mapSpeakerGroup);

                        if (response.IsSuccessStatusCode)
                        {
                            successCount++;
                        }
                        else
                        {
                            failCount++;
                        }
                    }
                    catch
                    {
                        failCount++;
                    }

                    addProgress++;
                    StateHasChanged();
                }

                if (successCount > 0)
                {
                    NotificationService.Notify(new NotificationMessage
                    {
                        Severity = NotificationSeverity.Success,
                        Summary = "추가 완료",
                        Detail = $"{successCount}개의 스피커가 '{group.Name}' 그룹에 추가되었습니다." +
                                (alreadyExistsCount > 0 ? $" ({alreadyExistsCount}개는 이미 그룹에 속해있음)" : "") +
                                (failCount > 0 ? $" ({failCount}개 실패)" : ""),
                        Duration = 4000
                    });

                    await LoadSpeakerGroupMappings();
                    StateHasChanged();
                }
                else if (alreadyExistsCount > 0)
                {
                    NotificationService.Notify(new NotificationMessage
                    {
                        Severity = NotificationSeverity.Info,
                        Summary = "알림",
                        Detail = $"선택한 스피커들은 이미 '{group.Name}' 그룹에 속해있습니다.",
                        Duration = 4000
                    });
                }
                else
                {
                    NotificationService.Notify(new NotificationMessage
                    {
                        Severity = NotificationSeverity.Warning,
                        Summary = "추가 실패",
                        Detail = "스피커를 그룹에 추가하는데 실패했습니다.",
                        Duration = 4000
                    });
                }
            }
            catch (Exception ex)
            {
                NotificationService.Notify(new NotificationMessage
                {
                    Severity = NotificationSeverity.Error,
                    Summary = "오류",
                    Detail = $"스피커 그룹 추가 중 오류 발생: {ex.Message}",
                    Duration = 4000
                });
                Logger.LogError(ex, "스피커 그룹 추가 중 오류");
            }
            finally
            {
                addProgress = addTotal;
                isAddingToGroup = false;
                StateHasChanged();
            }
        }

        // 스피커 그룹 추가 다이얼로그 열기
        protected async Task OpenAddSpeakerGroupDialog()
        {
            var result = await DialogService.OpenAsync<AddSpeakerGroupDialog>("스피커 그룹 추가",
                null,
                new DialogOptions
                {
                    Width = "600px",
                    Height = "auto",
                    Resizable = false,
                    Draggable = true
                });

            if (result == true)
            {
                await LoadSpeakerGroups();
            }
        }

        // 스피커 그룹 편집 다이얼로그 열기
        protected async Task OpenEditGroupDialog(WicsPlatform.Server.Models.wics.Group group)
        {
            var parameters = new Dictionary<string, object>
            {
                { "GroupId", group.Id }
            };

            var result = await DialogService.OpenAsync<EditSpeakerGroupDialog>("스피커 그룹 수정",
                parameters,
                new DialogOptions
                {
                    Width = "600px",
                    Height = "auto",
                    Resizable = false,
                    Draggable = true
                });

            if (result == true)
            {
                await LoadSpeakerGroups();
                await LoadSpeakerGroupMappings();
            }
        }

        // 스피커 그룹 삭제 확인 다이얼로그 열기
        protected async Task OpenDeleteGroupDialog(WicsPlatform.Server.Models.wics.Group group)
        {
            var speakerCount = GetSpeakerCountInGroup(group.Id);

            string message;
            if (speakerCount > 0)
            {
                message = $"'{group.Name}' 그룹에는 {speakerCount}개의 스피커가 포함되어 있습니다.\n그래도 삭제하시겠습니까?";
            }
            else
            {
                message = $"'{group.Name}' 그룹을 삭제하시겠습니까?";
            }

            var result = await DialogService.Confirm(message, "스피커 그룹 삭제",
                new ConfirmOptions() { OkButtonText = "삭제", CancelButtonText = "취소" });

            if (result == true)
            {
                await DeleteSpeakerGroup(group);
            }
        }

        // 스피커 그룹 삭제 실행
        protected async Task DeleteSpeakerGroup(WicsPlatform.Server.Models.wics.Group group)
        {
            try
            {
                // 소프트 삭제를 위한 업데이트
                var updateData = new
                {
                    DeleteYn = "Y",
                    UpdatedAt = DateTime.Now
                };

                var response = await Http.PatchAsJsonAsync($"odata/wics/Groups({group.Id})", updateData);

                if (response.IsSuccessStatusCode)
                {
                    NotificationService.Notify(new NotificationMessage
                    {
                        Severity = NotificationSeverity.Success,
                        Summary = "삭제 완료",
                        Detail = $"'{group.Name}' 그룹이 삭제되었습니다.",
                        Duration = 4000
                    });

                    // 그룹이 삭제되면 해당 그룹에 속한 스피커들의 그룹 연결도 해제
                    await RemoveSpeakersFromDeletedGroup(group.Id);

                    await LoadSpeakerGroups();
                    await LoadSpeakerGroupMappings();

                    // 선택된 그룹이 삭제되었으면 선택 해제
                    if (selectedGroups.Contains(group.Id))
                    {
                        selectedGroups.Remove(group.Id);
                    }
                }
                else
                {
                    NotificationService.Notify(new NotificationMessage
                    {
                        Severity = NotificationSeverity.Error,
                        Summary = "삭제 실패",
                        Detail = "스피커 그룹 삭제 중 오류가 발생했습니다.",
                        Duration = 4000
                    });
                }
            }
            catch (Exception ex)
            {
                NotificationService.Notify(new NotificationMessage
                {
                    Severity = NotificationSeverity.Error,
                    Summary = "오류",
                    Detail = $"스피커 그룹 삭제 중 오류가 발생했습니다: {ex.Message}",
                    Duration = 4000
                });
                Logger.LogError(ex, "스피커 그룹 삭제 중 오류");
            }
        }

        // 삭제된 그룹의 스피커 매핑 해제
        protected async Task RemoveSpeakersFromDeletedGroup(ulong groupId)
        {
            try
            {
                // 해당 그룹의 모든 매핑을 LastYn = 'N'으로 업데이트
                var mappings = speakerGroupMappings.Where(m => m.GroupId == groupId);

                foreach (var mapping in mappings)
                {
                    var updateData = new
                    {
                        LastYn = "N",
                        UpdatedAt = DateTime.Now
                    };

                    await Http.PatchAsJsonAsync($"odata/wics/MapSpeakerGroups({mapping.Id})", updateData);
                }

                await LoadSpeakerGroupMappings();
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "그룹 매핑 해제 중 오류");
            }
        }

        // 스피커를 그룹에서 제거하는 다이얼로그 열기
        protected async Task OpenRemoveSpeakerFromGroupDialog(WicsPlatform.Server.Models.wics.Speaker speaker, WicsPlatform.Server.Models.wics.Group group)
        {
            var result = await DialogService.Confirm(
                $"'{speaker.Name}' 스피커를 '{group.Name}' 그룹에서 제거하시겠습니까?",
                "스피커 제거",
                new ConfirmOptions()
                {
                    OkButtonText = "제거",
                    CancelButtonText = "취소"
                });

            if (result == true)
            {
                await RemoveSpeakerFromGroup(speaker, group);
            }
        }

        // 스피커를 그룹에서 제거
        protected async Task RemoveSpeakerFromGroup(WicsPlatform.Server.Models.wics.Speaker speaker, WicsPlatform.Server.Models.wics.Group group)
        {
            try
            {
                var mapping = speakerGroupMappings
                    .FirstOrDefault(m => m.SpeakerId == speaker.Id && m.GroupId == group.Id && m.LastYn == "Y");

                if (mapping != null)
                {
                    // 소프트 삭제를 위한 업데이트
                    var updateData = new
                    {
                        LastYn = "N",
                        UpdatedAt = DateTime.Now
                    };

                    var response = await Http.PatchAsJsonAsync($"odata/wics/MapSpeakerGroups({mapping.Id})", updateData);

                    if (response.IsSuccessStatusCode)
                    {
                        NotificationService.Notify(new NotificationMessage
                        {
                            Severity = NotificationSeverity.Success,
                            Summary = "제거 완료",
                            Detail = $"'{speaker.Name}' 스피커가 '{group.Name}' 그룹에서 제거되었습니다.",
                            Duration = 3000
                        });

                        // 데이터 새로고침
                        await LoadSpeakerGroupMappings();

                        // 그룹 스피커 목록 새로고침
                        if (expandedGroups.Contains(group.Id))
                        {
                            StateHasChanged();
                        }
                    }
                    else
                    {
                        NotificationService.Notify(new NotificationMessage
                        {
                            Severity = NotificationSeverity.Error,
                            Summary = "제거 실패",
                            Detail = "스피커를 그룹에서 제거하는 중 오류가 발생했습니다.",
                            Duration = 4000
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                NotificationService.Notify(new NotificationMessage
                {
                    Severity = NotificationSeverity.Error,
                    Summary = "오류",
                    Detail = $"스피커 제거 중 오류 발생: {ex.Message}",
                    Duration = 4000
                });
                Logger.LogError(ex, "스피커 제거 중 오류");
            }
        }

        // 스피커 추가 다이얼로그 열기
        protected async Task OpenAddSpeakerDialog()
        {
            var result = await DialogService.OpenAsync<AddSpeakerDialog>("스피커 추가",
                null,
                new DialogOptions
                {
                    Width = "820px",
                    Height = "auto",
                    Resizable = false,
                    Draggable = true
                });

            if (result == true)
            {
                await LoadAllSpeakers();
                await LoadSpeakerGroupMappings();
            }
        }

        // 스피커 편집 다이얼로그 열기
        protected async Task OpenEditSpeakerDialog(WicsPlatform.Server.Models.wics.Speaker speaker)
        {
            var parameters = new Dictionary<string, object>
            {
                { "SpeakerId", speaker.Id }
            };

            var result = await DialogService.OpenAsync<EditSpeakerDialog>("스피커 수정",
                parameters,
                new DialogOptions
                {
                    Width = "820px",
                    Height = "auto",
                    Resizable = false,
                    Draggable = true
                });

            if (result == true)
            {
                await LoadAllSpeakers();
                await LoadSpeakerGroupMappings();
            }
        }

        // 스피커 삭제 확인 다이얼로그 열기
        protected async Task OpenDeleteSpeakerDialog(WicsPlatform.Server.Models.wics.Speaker speaker)
        {
            var groupNames = GetSpeakerGroups(speaker.Id).ToList();
            
            string message;
            if (groupNames.Any())
            {
                message = $"'{speaker.Name}' 스피커는 다음 그룹에 속해 있습니다:\n" +
                         $"{string.Join(", ", groupNames)}\n\n" +
                         $"그래도 삭제하시겠습니까?";
            }
            else
            {
                message = $"'{speaker.Name}' 스피커를 삭제하시겠습니까?";
            }

            var result = await DialogService.Confirm(message, "스피커 삭제",
                new ConfirmOptions() { OkButtonText = "삭제", CancelButtonText = "취소" });

            if (result == true)
            {
                await DeleteSpeaker(speaker);
            }
        }

        // 스피커 삭제 실행
        protected async Task DeleteSpeaker(WicsPlatform.Server.Models.wics.Speaker speaker)
        {
            try
            {
                // 소프트 삭제를 위한 업데이트
                var updateData = new
                {
                    DeleteYn = "Y",
                    UpdatedAt = DateTime.Now
                };

                var response = await Http.PatchAsJsonAsync($"odata/wics/Speakers({speaker.Id})", updateData);

                if (response.IsSuccessStatusCode)
                {
                    NotificationService.Notify(new NotificationMessage
                    {
                        Severity = NotificationSeverity.Success,
                        Summary = "삭제 완료",
                        Detail = $"'{speaker.Name}' 스피커가 삭제되었습니다.",
                        Duration = 4000
                    });

                    // 스피커가 삭제되면 해당 스피커의 그룹 연결도 해제
                    await RemoveSpeakerFromAllGroups(speaker.Id);

                    await LoadAllSpeakers();
                    await LoadSpeakerGroupMappings();

                    // 선택된 스피커가 삭제되었으면 선택 해제
                    if (selectedSpeakers.Contains(speaker.Id))
                    {
                        selectedSpeakers.Remove(speaker.Id);
                    }
                }
                else
                {
                    NotificationService.Notify(new NotificationMessage
                    {
                        Severity = NotificationSeverity.Error,
                        Summary = "삭제 실패",
                        Detail = "스피커 삭제 중 오류가 발생했습니다.",
                        Duration = 4000
                    });
                }
            }
            catch (Exception ex)
            {
                NotificationService.Notify(new NotificationMessage
                {
                    Severity = NotificationSeverity.Error,
                    Summary = "오류",
                    Detail = $"스피커 삭제 중 오류가 발생했습니다: {ex.Message}",
                    Duration = 4000
                });
                Logger.LogError(ex, "스피커 삭제 중 오류");
            }
        }

        // 스피커를 모든 그룹에서 제거
        protected async Task RemoveSpeakerFromAllGroups(ulong speakerId)
        {
            try
            {
                // 해당 스피커의 모든 매핑을 LastYn = 'N'으로 업데이트
                var mappings = speakerGroupMappings.Where(m => m.SpeakerId == speakerId);

                foreach (var mapping in mappings)
                {
                    var updateData = new
                    {
                        LastYn = "N",
                        UpdatedAt = DateTime.Now
                    };

                    await Http.PatchAsJsonAsync($"odata/wics/MapSpeakerGroups({mapping.Id})", updateData);
                }

                await LoadSpeakerGroupMappings();
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "스피커 매핑 해제 중 오류");
            }
        }
    }
}