using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.JSInterop;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Radzen;
using Radzen.Blazor;
using System.Net.Http;
using System.Net.Http.Json;
using WicsPlatform.Client.Dialogs;

namespace WicsPlatform.Client.Pages
{
    public partial class ManageSpeaker : IAsyncDisposable
    {
        [Inject]
        protected IJSRuntime JSRuntime { get; set; }

        [Inject]
        protected NavigationManager NavigationManager { get; set; }

        [Inject]
        protected DialogService DialogService { get; set; }

        [Inject]
        protected TooltipService TooltipService { get; set; }

        [Inject]
        protected ContextMenuService ContextMenuService { get; set; }

        [Inject]
        protected NotificationService NotificationService { get; set; }

        [Inject]
        protected SecurityService Security { get; set; }

        [Inject]
        protected wicsService WicsService { get; set; }

        [Inject]
        protected HttpClient Http { get; set; }

        // JS 모듈 관련 필드
        private IJSObjectReference? _jsModule;
        private DotNetObjectReference<ManageSpeaker>? _dotNetRef;

        // 스피커 관련 필드
        protected RadzenDataGrid<WicsPlatform.Server.Models.wics.Speaker> speakersGrid;
        protected IEnumerable<WicsPlatform.Server.Models.wics.Speaker> speakers = new List<WicsPlatform.Server.Models.wics.Speaker>();
        protected IEnumerable<WicsPlatform.Server.Models.wics.Speaker> allSpeakers = new List<WicsPlatform.Server.Models.wics.Speaker>();
        protected bool isLoading = true;

        // 스피커 그룹 관련 필드
        protected RadzenDataGrid<WicsPlatform.Server.Models.wics.Group> groupsGrid;
        protected IEnumerable<WicsPlatform.Server.Models.wics.Group> speakerGroups = new List<WicsPlatform.Server.Models.wics.Group>();
        protected bool isLoadingGroups = true;

        // 채널 데이터
        protected IEnumerable<WicsPlatform.Server.Models.wics.Channel> channels = new List<WicsPlatform.Server.Models.wics.Channel>();

        // 필터 관련 필드
        protected string speakerNameFilter = "";
        protected string ipAddressFilter = "";
        protected string locationFilter = "";
        protected ulong? channelFilter = null;

        // 탭 인덱스
        protected int selectedTabIndex = 0;
        protected HashSet<ulong> expandedGroups = new HashSet<ulong>();

        // 선택된 스피커와 그룹
        protected IList<WicsPlatform.Server.Models.wics.Speaker> selectedSpeakers = new List<WicsPlatform.Server.Models.wics.Speaker>();
        protected ulong? selectedGroupId = null;
        protected bool selectAllChecked = false;

        // 스피커-그룹 매핑 데이터
        protected IEnumerable<WicsPlatform.Server.Models.wics.MapSpeakerGroup> speakerGroupMappings = new List<WicsPlatform.Server.Models.wics.MapSpeakerGroup>();

        // 그룹 선택 관련 필드
        protected IList<WicsPlatform.Server.Models.wics.Group> selectedGroups = new List<WicsPlatform.Server.Models.wics.Group>();
        protected IEnumerable<WicsPlatform.Server.Models.wics.Speaker> groupSpeakers = new List<WicsPlatform.Server.Models.wics.Speaker>();
        protected bool isLoadingGroupSpeakers = false;

        // 드래그 앤 드롭 관련 필드
        protected bool isDragging = false;
        protected ulong? isDraggingOverGroup = null;
        protected WicsPlatform.Server.Models.wics.Speaker draggingSpeaker = null;

        // 중복 방지를 위한 처리 중 플래그
        private bool isAddingToGroup = false;

        // 그룹에 스피커를 추가하는 진행률
        private int addProgress = 0;
        private int addTotal = 0;

        protected int AddProgressPercent => addTotal == 0 ? 0 : (int)((double)addProgress / addTotal * 100);

        protected override async Task OnInitializedAsync()
        {
            await LoadChannels();
            await LoadSpeakers();
            await LoadSpeakerGroups();
            await LoadSpeakerGroupMappings();
        }

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            if (firstRender)
            {
                try
                {
                    _jsModule = await JSRuntime.InvokeAsync<IJSObjectReference>("import", "./js/dragdrop.js");
                    _dotNetRef = DotNetObjectReference.Create(this);
                    await _jsModule.InvokeVoidAsync("initializeDragDrop", _dotNetRef);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"드래그 드롭 초기화 실패: {ex.Message}");
                }
            }
            else
            {
                // 스피커 목록이 변경되었을 때 드래그 드롭 재초기화
                if (_jsModule != null && _dotNetRef != null)
                {
                    await _jsModule.InvokeVoidAsync("refreshDragDrop", _dotNetRef);
                }
            }
        }

        protected async Task LoadChannels()
        {
            try
            {
                var result = await WicsService.GetChannels();
                channels = result.Value.AsODataEnumerable();
            }
            catch (Exception ex)
            {
                NotificationService.Notify(new NotificationMessage
                {
                    Severity = NotificationSeverity.Error,
                    Summary = "오류",
                    Detail = $"채널 목록을 불러오는 중 오류가 발생했습니다: {ex.Message}",
                    Duration = 4000
                });
            }
        }

        protected async Task LoadSpeakers()
        {
            try
            {
                isLoading = true;

                var query = new Radzen.Query
                {
                    Expand = "Channel",
                    Filter = "DeleteYn eq 'N' or DeleteYn eq null"
                };

                var result = await WicsService.GetSpeakers(query);
                allSpeakers = result.Value.AsODataEnumerable();
                speakers = allSpeakers;

                // 필터 적용
                ApplyFilters();
            }
            catch (Exception ex)
            {
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
                isLoading = false;
            }
        }

        protected async Task LoadSpeakerGroups()
        {
            try
            {
                isLoadingGroups = true;

                // Type이 0인 스피커 그룹만 가져오도록 필터 수정
                var query = new Radzen.Query
                {
                    Filter = "(DeleteYn eq 'N' or DeleteYn eq null) and Type eq 0"  // Type eq 0 조건 추가
                };

                var result = await WicsService.GetGroups(query);
                speakerGroups = result.Value.AsODataEnumerable();
            }
            catch (Exception ex)
            {
                NotificationService.Notify(new NotificationMessage
                {
                    Severity = NotificationSeverity.Error,
                    Summary = "오류",
                    Detail = $"스피커 그룹 목록을 불러오는 중 오류가 발생했습니다: {ex.Message}",
                    Duration = 4000
                });
            }
            finally
            {
                isLoadingGroups = false;
            }
        }

        protected async Task LoadSpeakerGroupMappings()
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
                NotificationService.Notify(new NotificationMessage
                {
                    Severity = NotificationSeverity.Error,
                    Summary = "오류",
                    Detail = $"스피커-그룹 매핑 정보를 불러오는 중 오류가 발생했습니다: {ex.Message}",
                    Duration = 4000
                });
            }
        }

        // JavaScript에서 호출되는 드롭 핸들러
        [JSInvokable]
        public async Task HandleDropFromJS(int groupId)
        {
            // 중복 호출 방지
            if (isAddingToGroup)
            {
                Console.WriteLine("이미 추가 처리 중입니다. 중복 호출 무시.");
                return;
            }

            var group = speakerGroups.FirstOrDefault(g => g.Id == (ulong)groupId);
            if (group != null && selectedSpeakers.Any())
            {
                await AddSelectedSpeakersToGroup(group);

                isDragging = false;
                isDraggingOverGroup = null;
                draggingSpeaker = null;

                // body에서 dragging 클래스 제거
                await JSRuntime.InvokeVoidAsync("document.body.classList.remove", "dragging");

                await InvokeAsync(StateHasChanged);
            }
        }

        // 드래그 앤 드롭 핸들러 (UI 상태 관리용으로 유지)
        protected async Task HandleDragStart(WicsPlatform.Server.Models.wics.Speaker speaker)
        {
            isDragging = true;
            draggingSpeaker = speaker;

            // body에 dragging 클래스 추가
            await JSRuntime.InvokeVoidAsync("document.body.classList.add", "dragging");
        }

        protected async Task HandleDragEnd(DragEventArgs e)
        {
            isDragging = false;
            draggingSpeaker = null;
            isDraggingOverGroup = null;

            // body에서 dragging 클래스 제거
            await JSRuntime.InvokeVoidAsync("document.body.classList.remove", "dragging");
        }

        protected void HandleDragEnter(WicsPlatform.Server.Models.wics.Group group)
        {
            if (isDragging)
            {
                isDraggingOverGroup = group.Id;
            }
        }

        protected void HandleDragLeave(WicsPlatform.Server.Models.wics.Group group)
        {
            if (isDraggingOverGroup == group.Id)
            {
                isDraggingOverGroup = null;
            }
        }

        protected async Task HandleDrop(WicsPlatform.Server.Models.wics.Group group)
        {
            // 중복 호출 방지
            if (isAddingToGroup)
            {
                Console.WriteLine("이미 추가 처리 중입니다. 중복 호출 무시.");
                return;
            }

            if (selectedSpeakers.Any())
            {
                await AddSelectedSpeakersToGroup(group);
                isDragging = false;
                isDraggingOverGroup = null;
                draggingSpeaker = null;

                // body에서 dragging 클래스 제거
                await JSRuntime.InvokeVoidAsync("document.body.classList.remove", "dragging");

                await InvokeAsync(StateHasChanged);
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

            // 중복 처리 방지
            if (isAddingToGroup)
            {
                Console.WriteLine("이미 처리 중입니다.");
                return;
            }

            try
            {
                isAddingToGroup = true;
                await InvokeAsync(StateHasChanged); // 처리 중 오버레이 표시

                // 선택된 스피커의 복사본 생성 (처리 중 변경 방지)
                var speakersToAdd = selectedSpeakers.ToList();

                addProgress = 0;
                addTotal = speakersToAdd.Count;
                StateHasChanged(); // 처리 중 오버레이 표시

                int successCount = 0;
                int failCount = 0;
                int alreadyExistsCount = 0;

                // 먼저 현재 매핑 정보를 다시 로드
                await LoadSpeakerGroupMappings();

                foreach (var speaker in speakersToAdd)
                {
                    try
                    {
                        // 이미 해당 그룹에 속해있는지 확인
                        var existingMapping = speakerGroupMappings
                            .FirstOrDefault(m => m.SpeakerId == speaker.Id && m.GroupId == group.Id && m.LastYn == "Y");

                        if (existingMapping != null)
                        {
                            alreadyExistsCount++;
                            continue;
                        }

                        // 새로운 매핑 생성
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

                    // 진행률 업데이트
                    addProgress++;
                    StateHasChanged();
                }

                // 결과 알림 표시
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

                    // 데이터 새로고침
                    await LoadSpeakerGroupMappings();

                    // 선택 초기화
                    selectedSpeakers.Clear();
                    selectAllChecked = false;

                    // UI 업데이트
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
            }
            finally
            {
                addProgress = addTotal;
                isAddingToGroup = false;
                StateHasChanged();
            }
        }

        // 스피커 선택 토글
        protected void ToggleSpeakerSelection(WicsPlatform.Server.Models.wics.Speaker speaker)
        {
            if (IsSpeakerSelected(speaker))
            {
                selectedSpeakers = selectedSpeakers.Where(s => s.Id != speaker.Id).ToList();
            }
            else
            {
                selectedSpeakers.Add(speaker);
            }
        }

        // 선택 초기화
        protected void ClearSelection()
        {
            selectedSpeakers.Clear();
            selectAllChecked = false;
        }

        // 스피커가 속한 그룹 목록 반환
        protected IEnumerable<string> GetSpeakerGroups(ulong speakerId)
        {
            return speakerGroupMappings
                .Where(m => m.SpeakerId == speakerId && m.Group != null)
                .Select(m => m.Group.Name)
                .Distinct();
        }

        // 그룹에 속한 스피커 수 반환
        protected int GetSpeakerCountInGroup(ulong groupId)
        {
            return speakerGroupMappings
                .Where(m => m.GroupId == groupId)
                .Select(m => m.SpeakerId)
                .Distinct()
                .Count();
        }

        // 그룹에 속한 스피커 목록 반환
        protected async Task<IEnumerable<WicsPlatform.Server.Models.wics.Speaker>> GetSpeakersInGroup(ulong groupId)
        {
            var speakerIds = speakerGroupMappings
                .Where(m => m.GroupId == groupId && m.LastYn == "Y")
                .Select(m => m.SpeakerId)
                .Distinct();

            return allSpeakers.Where(s => speakerIds.Contains(s.Id));
        }

        // 스피커 선택 상태 확인
        protected bool IsSpeakerSelected(WicsPlatform.Server.Models.wics.Speaker speaker)
        {
            return selectedSpeakers.Any(s => s.Id == speaker.Id);
        }

        // 개별 스피커 선택 변경
        protected void SpeakerSelectionChanged(bool selected, WicsPlatform.Server.Models.wics.Speaker speaker)
        {
            if (selected)
            {
                if (!IsSpeakerSelected(speaker))
                {
                    selectedSpeakers.Add(speaker);
                }
            }
            else
            {
                selectedSpeakers = selectedSpeakers.Where(s => s.Id != speaker.Id).ToList();
            }

            UpdateSelectAllCheckbox();
            StateHasChanged();
        }

        // 전체 선택 체크박스 변경
        protected void SelectAllSpeakersChanged(bool selected)
        {
            if (selected)
            {
                selectedSpeakers = speakers.ToList();
            }
            else
            {
                selectedSpeakers.Clear();
            }

            speakersGrid?.Reload();
            StateHasChanged();
        }

        // 전체 선택 체크박스 상태 업데이트
        protected void UpdateSelectAllCheckbox()
        {
            selectAllChecked = speakers.Any() && selectedSpeakers.Count == speakers.Count();
        }

        // 선택된 스피커들을 그룹에 추가
        protected async Task AddSpeakersToGroup()
        {
            if (selectedSpeakers == null || !selectedSpeakers.Any() || selectedGroupId == null)
            {
                NotificationService.Notify(new NotificationMessage
                {
                    Severity = NotificationSeverity.Warning,
                    Summary = "선택 필요",
                    Detail = "추가할 스피커와 그룹을 선택하세요.",
                    Duration = 3000
                });
                return;
            }

            try
            {
                int successCount = 0;
                int failCount = 0;

                foreach (var speaker in selectedSpeakers)
                {
                    try
                    {
                        // 이미 해당 그룹에 속해있는지 확인
                        var existingMapping = speakerGroupMappings
                            .FirstOrDefault(m => m.SpeakerId == speaker.Id && m.GroupId == selectedGroupId.Value);

                        if (existingMapping != null)
                        {
                            // 이미 그룹에 속해있음
                            continue;
                        }

                        // 새로운 매핑 생성
                        var mapSpeakerGroup = new WicsPlatform.Server.Models.wics.MapSpeakerGroup
                        {
                            SpeakerId = speaker.Id,
                            GroupId = selectedGroupId.Value,
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
                }

                if (successCount > 0)
                {
                    var groupName = speakerGroups.FirstOrDefault(g => g.Id == selectedGroupId.Value)?.Name;
                    NotificationService.Notify(new NotificationMessage
                    {
                        Severity = NotificationSeverity.Success,
                        Summary = "추가 완료",
                        Detail = $"{successCount}개의 스피커가 '{groupName}' 그룹에 추가되었습니다." +
                                (failCount > 0 ? $" ({failCount}개 실패)" : ""),
                        Duration = 4000
                    });

                    // 데이터 새로고침
                    await LoadSpeakerGroupMappings();
                    await LoadSpeakers();

                    // 선택 초기화
                    selectedSpeakers.Clear();
                    selectedGroupId = null;
                    selectAllChecked = false;

                    // 선택된 그룹이 있다면 해당 그룹의 스피커 목록 새로고침
                    if (selectedGroups != null && selectedGroups.Any())
                    {
                        await LoadGroupSpeakers(selectedGroups.First().Id);
                    }

                    // UI 업데이트
                    speakersGrid?.Reload();
                    StateHasChanged();
                }
                else
                {
                    NotificationService.Notify(new NotificationMessage
                    {
                        Severity = NotificationSeverity.Warning,
                        Summary = "추가 실패",
                        Detail = "모든 스피커들이 이미 해당 그룹에 속해있거나 추가에 실패했습니다.",
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

                    var response = await Http.PatchAsJsonAsync($"odata/wics/MapSpeakerGroups(Id={mapping.Id})", updateData);

                    if (response.IsSuccessStatusCode)
                    {
                        NotificationService.Notify(new NotificationMessage
                        {
                            Severity = NotificationSeverity.Success,
                            Summary = "삭제 완료",
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
                            Summary = "삭제 실패",
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
            }
        }

        protected void ApplyFilters()
        {
            var filteredSpeakers = allSpeakers;

            if (!string.IsNullOrWhiteSpace(speakerNameFilter))
            {
                filteredSpeakers = filteredSpeakers.Where(s => s.Name.Contains(speakerNameFilter, StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrWhiteSpace(ipAddressFilter))
            {
                filteredSpeakers = filteredSpeakers.Where(s => s.Ip.Contains(ipAddressFilter, StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrWhiteSpace(locationFilter))
            {
                filteredSpeakers = filteredSpeakers.Where(s => s.Location.Contains(locationFilter, StringComparison.OrdinalIgnoreCase));
            }

            //if (channelFilter.HasValue)
            //{
            //    filteredSpeakers = filteredSpeakers.Where(s => s.ChannelId == channelFilter.Value);
            //}

            speakers = filteredSpeakers;
            speakersGrid?.Reload();
        }

        // 그룹 선택 시 해당 그룹에 속한 스피커 로드
        protected async Task ViewGroupSpeakers(WicsPlatform.Server.Models.wics.Group group)
        {
            await LoadGroupSpeakers(group.Id);
        }

        // 그룹에 속한 스피커 로드
        protected async Task LoadGroupSpeakers(ulong groupId)
        {
            try
            {
                isLoadingGroupSpeakers = true;
                StateHasChanged();

                groupSpeakers = await GetSpeakersInGroup(groupId);
            }
            catch (Exception ex)
            {
                NotificationService.Notify(new NotificationMessage
                {
                    Severity = NotificationSeverity.Error,
                    Summary = "오류",
                    Detail = $"그룹 스피커 목록을 불러오는 중 오류가 발생했습니다: {ex.Message}",
                    Duration = 4000
                });
            }
            finally
            {
                isLoadingGroupSpeakers = false;
                StateHasChanged();
            }
        }

        protected async Task OpenAddSpeakerDialog()
        {
            var result = await DialogService.OpenAsync<AddSpeakerDialog>("스피커 추가",
                null,
                new DialogOptions
                {
                    Width = "700px",
                    Height = "auto",
                    Resizable = false,
                    Draggable = true
                });

            if (result == true)
            {
                await LoadSpeakers();
            }
        }

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

        protected async Task OpenEditSpeakerDialog(WicsPlatform.Server.Models.wics.Speaker speaker)
        {
            var parameters = new Dictionary<string, object>
            {
                { "SpeakerId", speaker.Id }
            };

            var result = await DialogService.OpenAsync<EditSpeakerDialog>("스피커 정보 수정",
                parameters,
                new DialogOptions
                {
                    Width = "700px",
                    Height = "auto",
                    Resizable = false,
                    Draggable = true
                });

            if (result == true)
            {
                await LoadSpeakers();
                await LoadSpeakerGroupMappings();

                // 선택된 그룹이 있다면 해당 그룹의 스피커 목록 새로고침
                if (selectedGroups != null && selectedGroups.Any())
                {
                    await LoadGroupSpeakers(selectedGroups.First().Id);
                }
            }
        }

        protected async Task OpenDeleteSpeakerDialog(WicsPlatform.Server.Models.wics.Speaker speaker)
        {
            var result = await DialogService.Confirm($"'{speaker.Name}' 스피커를 삭제하시겠습니까?", "스피커 삭제",
                new ConfirmOptions() { OkButtonText = "삭제", CancelButtonText = "취소" });

            if (result == true)
            {
                await DeleteSpeaker(speaker);
            }
        }

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

                var response = await Http.PatchAsJsonAsync($"odata/wics/Speakers(Id={speaker.Id})", updateData);

                if (response.IsSuccessStatusCode)
                {
                    NotificationService.Notify(new NotificationMessage
                    {
                        Severity = NotificationSeverity.Success,
                        Summary = "삭제 완료",
                        Detail = $"'{speaker.Name}' 스피커가 삭제되었습니다.",
                        Duration = 4000
                    });

                    await LoadSpeakers();

                    // 선택된 그룹이 있다면 해당 그룹의 스피커 목록 새로고침
                    if (selectedGroups != null && selectedGroups.Any())
                    {
                        await LoadGroupSpeakers(selectedGroups.First().Id);
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
            }
        }

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

                // 선택된 그룹이 수정되었으면 갱신
                if (selectedGroups != null && selectedGroups.Any() && selectedGroups.First().Id == group.Id)
                {
                    await LoadGroupSpeakers(group.Id);
                }
            }
        }

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

                var response = await Http.PatchAsJsonAsync($"odata/wics/Groups(Id={group.Id})", updateData);

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
                    await LoadSpeakers(); // 스피커 목록도 새로고침

                    // 선택된 그룹이 삭제되었으면 비움
                    if (selectedGroups.Any() && selectedGroups.First().Id == group.Id)
                    {
                        selectedGroups = new List<WicsPlatform.Server.Models.wics.Group>();
                        groupSpeakers = new List<WicsPlatform.Server.Models.wics.Speaker>();
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
            }
        }

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

                    await Http.PatchAsJsonAsync($"odata/wics/MapSpeakerGroups(Id={mapping.Id})", updateData);
                }

                await LoadSpeakerGroupMappings();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"그룹 매핑 해제 중 오류: {ex.Message}");
            }
        }

        protected void ViewSpeakerDetails(WicsPlatform.Server.Models.wics.Speaker speaker)
        {
            // 스피커 상세 보기 구현 (나중에 추가)
        }

        protected void ViewGroupDetails(WicsPlatform.Server.Models.wics.Group group)
        {
            // 그룹 상세 보기 구현 (나중에 추가)
        }

        protected BadgeStyle GetStatusBadgeStyle(byte state)
        {
            return state switch
            {
                0 => BadgeStyle.Danger,
                1 => BadgeStyle.Success,
                _ => BadgeStyle.Light
            };
        }

        protected string GetStatusText(byte state)
        {
            return state switch
            {
                0 => "오프라인",
                1 => "온라인",
                _ => "알 수 없음"
            };
        }

        // 그룹 확장/축소 토글
        protected void ToggleGroupExpansion(ulong groupId)
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

        // 동기식으로 그룹의 스피커 목록 가져오기
        protected IEnumerable<WicsPlatform.Server.Models.wics.Speaker> GetSpeakersInGroupSync(ulong groupId)
        {
            var speakerIds = speakerGroupMappings
                .Where(m => m.GroupId == groupId && m.LastYn == "Y")
                .Select(m => m.SpeakerId)
                .Distinct();

            return allSpeakers.Where(s => speakerIds.Contains(s.Id));
        }

        // 그룹 카드의 CSS 클래스를 반환하는 헬퍼 메서드
        protected string GetGroupCardClass(WicsPlatform.Server.Models.wics.Group group)
        {
            var classes = new List<string> { "group-card" };

            if (selectedGroups?.Any(g => g.Id == group.Id) == true)
            {
                classes.Add("selected");
            }

            if (isDraggingOverGroup == group.Id)
            {
                classes.Add("drag-over");
            }

            return string.Join(" ", classes);
        }

        // 스피커 카드의 CSS 클래스를 반환하는 헬퍼 메서드
        protected string GetSpeakerCardClass(WicsPlatform.Server.Models.wics.Speaker speaker)
        {
            var classes = new List<string> { "speaker-card" };

            if (IsSpeakerSelected(speaker))
            {
                classes.Add("selected");
            }

            if (isDragging && IsSpeakerSelected(speaker))
            {
                classes.Add("dragging");
            }

            return string.Join(" ", classes);
        }

        // Dispose 메서드
        public async ValueTask DisposeAsync()
        {
            if (_jsModule != null)
            {
                await _jsModule.DisposeAsync();
            }
            _dotNetRef?.Dispose();
        }
    }
}