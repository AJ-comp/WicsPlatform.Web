using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Radzen;
using System.Net.Http;
using System.Net.Http.Json;
using WicsPlatform.Shared;
using Microsoft.JSInterop;

namespace WicsPlatform.Client.Pages.SubPages
{
    public partial class BroadcastPlaylistSection : IDisposable
    {
        /* ────────────────────── [Parameters] ────────────────────── */
        [Parameter] public WicsPlatform.Server.Models.wics.Channel Channel { get; set; }
        [Parameter] public bool IsCollapsed { get; set; }
        [Parameter] public EventCallback<bool> IsCollapsedChanged { get; set; }

        // 방송 관련 파라미터 추가
        [Parameter] public bool IsBroadcasting { get; set; }
        [Parameter] public string BroadcastId { get; set; }

        /* ────────────────────── [DI] ────────────────────── */
        [Inject] protected NotificationService NotificationService { get; set; }
        [Inject] protected DialogService DialogService { get; set; }
        [Inject] protected wicsService WicsService { get; set; }
        [Inject] protected HttpClient Http { get; set; }
        [Inject] protected IJSRuntime JSRuntime { get; set; }

        [Inject] protected ILogger<BroadcastPlaylistSection> _logger { get; set; }

        private IJSObjectReference jsModule;
        private IJSObjectReference jsPlayer;
        private DotNetObjectReference<BroadcastPlaylistSection> dotNetRef;

        /* ────────────────────── [State - 기존] ────────────────────── */
        // 플레이리스트 관련 필드
        private IEnumerable<WicsPlatform.Server.Models.wics.Group> playlists = new List<WicsPlatform.Server.Models.wics.Group>();
        private WicsPlatform.Server.Models.wics.Group selectedPlaylist = null;
        private IEnumerable<WicsPlatform.Server.Models.wics.Medium> playlistMedia = new List<WicsPlatform.Server.Models.wics.Medium>();
        private bool isLoadingPlaylists = false;
        private bool isLoadingMedia = false;

        // 플레이리스트 미디어 개수 캐시
        private Dictionary<ulong, int> playlistMediaCounts = new Dictionary<ulong, int>();

        // 체크박스 관련 필드
        private Dictionary<ulong, bool> selectedPlaylists = new Dictionary<ulong, bool>();
        private Dictionary<ulong, bool> selectedMedia = new Dictionary<ulong, bool>();
        private bool selectAllMedia = false;

        // UI 관련 필드 추가
        private HashSet<ulong> expandedPlaylists = new HashSet<ulong>();
        private WicsPlatform.Server.Models.wics.Group viewingPlaylist = null;
        private IEnumerable<WicsPlatform.Server.Models.wics.Medium> allMedia = new List<WicsPlatform.Server.Models.wics.Medium>();
        private IEnumerable<WicsPlatform.Server.Models.wics.MapMediaGroup> mediaGroupMappings = new List<WicsPlatform.Server.Models.wics.MapMediaGroup>();

        /* ────────────────────── [State - 미디어 재생 관련 추가] ────────────────────── */
        private bool isMediaPlaying = false;
        private bool isMediaActionInProgress = false;
        private string currentMediaSessionId = null;

        // Mini-player state
        private string _nowPlayingText = string.Empty;
        private string _nowPlayingTime = string.Empty;
        private System.Timers.Timer _npTimer;

        // alias for Razor handler
        private Task OnSeek(int seconds) => Seek(seconds);

        // 드래그 앤 드롭 관련 필드
        private bool isDragging = false;
        private ulong? isDraggingOverPlaylist = null;
        private bool isAddingToPlaylist = false;
        private int addProgress = 0;
        private int addTotal = 0;

        protected int AddProgressPercent => addTotal == 0 ? 0 : (int)((double)addProgress / addTotal * 100);

        // 미디어 재생 관련 필드
        private ulong? playingMediaId = null;

        /* ────────────────────── [Life-Cycle] ────────────────────── */
        protected override async Task OnInitializedAsync()
        {
            await Task.WhenAll(
                LoadPlaylists(),
                LoadAllMedia(),
                LoadMediaGroupMappings()
            );
            SetupNowPlayingTimer();
        }

        protected override async Task OnParametersSetAsync()
        {
            // 방송이 종료되면 미디어 재생 상태 초기화
            if (!IsBroadcasting && isMediaPlaying)
            {
                isMediaPlaying = false;
                currentMediaSessionId = null;
                StateHasChanged();
            }
        }

        public void Dispose()
        {
            try
            {
                _npTimer?.Stop();
                _npTimer?.Dispose();
                _npTimer = null;
                dotNetRef?.Dispose();
            }
            catch { }
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
                    jsPlayer = await JSRuntime.InvokeAsync<IJSObjectReference>("import", $"./js/mediaplayer.js?v={version}");
                    dotNetRef = DotNetObjectReference.Create(this);
                    await jsModule.InvokeVoidAsync("initializeDragDrop", dotNetRef);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "JavaScript 모듈 로드 실패");
                }
            }
        }

        private void SetupNowPlayingTimer()
        {
            _npTimer?.Stop();
            _npTimer?.Dispose();
            _npTimer = new System.Timers.Timer(2000);
            _npTimer.Elapsed += async (_, __) => await QueryNowPlaying();
            _npTimer.AutoReset = true;
            _npTimer.Enabled = true;
        }

        private async Task QueryNowPlaying()
        {
            try
            {
                if (!IsBroadcasting || string.IsNullOrEmpty(BroadcastId)) return;
                if (!ulong.TryParse(BroadcastId, out var bid)) return;

                var res = await Http.GetFromJsonAsync<WicsPlatform.Shared.MediaStatusResponse>($"api/mediaplayer/now-playing/{bid}");
                if (res?.Success == true)
                {
                    _nowPlayingText = string.IsNullOrEmpty(res.CurrentMediaFileName) ? string.Empty : res.CurrentMediaFileName;
                    _nowPlayingTime = string.IsNullOrEmpty(res.CurrentPosition) ? string.Empty : $"{res.CurrentPosition} / {res.TotalDuration}";
                    await InvokeAsync(StateHasChanged);
                }
            }
            catch { }
        }

        private async Task Seek(int seconds)
        {
            try
            {
                if (!IsBroadcasting || string.IsNullOrEmpty(BroadcastId)) return;
                if (!ulong.TryParse(BroadcastId, out var bid)) return;
                var req = new { BroadcastId = bid, Seconds = seconds };
                await Http.PostAsJsonAsync("api/mediaplayer/seek", req);
            }
            catch { }
        }

        private async Task SkipNext()
        {
            try
            {
                if (!IsBroadcasting || string.IsNullOrEmpty(BroadcastId)) return;
                if (!ulong.TryParse(BroadcastId, out var bid)) return;
                isMediaActionInProgress = true;
                StateHasChanged();
                var req = new { BroadcastId = bid };
                var resp = await Http.PostAsJsonAsync("api/mediaplayer/next", req);
                if (resp.IsSuccessStatusCode)
                {
                    NotifyInfo("다음 미디어로 이동합니다.");
                    _ = QueryNowPlaying();
                }
                else
                {
                    var msg = await resp.Content.ReadAsStringAsync();
                    NotifyError("다음 미디어 이동 실패", new Exception(msg));
                }
            }
            catch (Exception ex)
            {
                NotifyError("다음 미디어 이동 중 오류", ex);
            }
            finally
            {
                isMediaActionInProgress = false;
                StateHasChanged();
            }
        }

        // 미디어 선택 복구 메서드 추가
        public async Task RecoverSelectedMedia(List<ulong> mediaIds)
        {
            try
            {
                selectedMedia.Clear();

                // 플레이리스트가 로드되지 않았다면 로드
                if (!playlists.Any())
                {
                    await LoadPlaylists();
                }

                // 모든 미디어 데이터 로드 (플레이리스트와 무관하게)
                var allMediaQuery = new Radzen.Query
                {
                    Filter = $"(DeleteYn eq 'N' or DeleteYn eq null)",
                    OrderBy = "CreatedAt desc"
                };

                var allMediaResult = await WicsService.GetMedia(allMediaQuery);
                var allAvailableMedia = allMediaResult.Value.AsODataEnumerable();

                // 미디어 ID로 선택 복구
                foreach (var mediaId in mediaIds)
                {
                    if (allAvailableMedia.Any(m => m.Id == mediaId))
                    {
                        selectedMedia[mediaId] = true;
                    }
                }

                // 복구된 미디어를 playlistMedia에 설정 (UI 표시용)
                playlistMedia = allAvailableMedia.Where(m => mediaIds.Contains(m.Id));

                _logger.LogInformation($"Recovered {mediaIds.Count} media selections");
                await InvokeAsync(StateHasChanged);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to recover media selections");
            }
        }

        /* ────────────────────── [Panel Toggle] ────────────────────── */
        private async Task TogglePanel()
        {
            await IsCollapsedChanged.InvokeAsync(!IsCollapsed);
        }

        /* ────────────────────── [미디어 재생/중지] ────────────────────── */
        private async Task PlayMedia()
        {
            await PlayMediaInternal(shuffle: false);
        }

        private async Task PlayMediaRandom()
        {
            await PlayMediaInternal(shuffle: true);
        }

        private async Task PlayMediaInternal(bool shuffle)
        {
            if (!IsBroadcasting || string.IsNullOrEmpty(BroadcastId) || !HasSelectedMedia())
            {
                NotifyWarning("미디어를 재생하려면 방송 중이어야 하고 미디어가 선택되어 있어야 합니다.");
                return;
            }

            try
            {
                isMediaActionInProgress = true;
                StateHasChanged();

                // 선택된 미디어 ID 가져오기
                var mediaIds = GetSelectedMedia().Select(m => m.Id).ToList();
                if (!mediaIds.Any())
                {
                    NotifyWarning("선택된 미디어가 없습니다.");
                    return;
                }

                // MediaPlayerController의 play 엔드포인트 호출
                var request = new MediaPlayRequest
                {
                    BroadcastId = ulong.TryParse(BroadcastId, out var bid) ? bid : 0UL,
                    MediaIds = mediaIds,
                    Shuffle = shuffle
                };

                var response = await Http.PostAsJsonAsync("api/mediaplayer/play", request);

                if (response.IsSuccessStatusCode)
                {
                    var result = await response.Content.ReadFromJsonAsync<MediaPlayResponse>();
                    if (result.Success)
                    {
                        isMediaPlaying = true;
                        currentMediaSessionId = result.SessionId;
                        if (shuffle)
                        {
                            NotifySuccess($"랜덤재생을 시작했습니다. ({mediaIds.Count}개 파일)");
                        }
                        else
                        {
                            NotifySuccess($"미디어 재생을 시작했습니다. ({mediaIds.Count}개 파일)");
                        }
                    }
                    else
                    {
                        NotifyError("미디어 재생 실패", new Exception(result.Message));
                    }
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    NotifyError("미디어 재생 요청 실패", new Exception($"Status: {response.StatusCode}, Error: {errorContent}"));
                }
            }
            catch (Exception ex)
            {
                NotifyError("미디어 재생 중 오류가 발생했습니다", ex);
            }
            finally
            {
                isMediaActionInProgress = false;
                StateHasChanged();
            }
        }

        private async Task StopMedia()
        {
            if (!IsBroadcasting || string.IsNullOrEmpty(BroadcastId))
            {
                NotifyWarning("방송 중이 아닙니다.");
                return;
            }

            try
            {
                isMediaActionInProgress = true;
                StateHasChanged();

                // MediaPlayerController의 stop 엔드포인트 호출
                var request = new
                {
                    broadcastId = BroadcastId
                };

                var response = await Http.PostAsJsonAsync("api/mediaplayer/stop", request);

                if (response.IsSuccessStatusCode)
                {
                    var result = await response.Content.ReadFromJsonAsync<MediaStopResponse>();
                    if (result.Success)
                    {
                        isMediaPlaying = false;
                        currentMediaSessionId = null;
                        NotifyInfo("미디어 재생을 중지했습니다.");
                    }
                    else
                    {
                        NotifyError("미디어 중지 실패", new Exception(result.Message));
                    }
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    NotifyError("미디어 중지 요청 실패", new Exception($"Status: {response.StatusCode}, Error: {errorContent}"));
                }
            }
            catch (Exception ex)
            {
                NotifyError("미디어 중지 중 오류가 발생했습니다", ex);
            }
            finally
            {
                isMediaActionInProgress = false;
                StateHasChanged();
            }
        }

        /* ────────────────────── [플레이리스트 관련 - 기존] ────────────────────── */
        private async Task LoadPlaylists()
        {
            try
            {
                isLoadingPlaylists = true;
                var query = new Radzen.Query
                {
                    Filter = "(DeleteYn eq 'N' or DeleteYn eq null) and Type eq 1",
                    OrderBy = "CreatedAt desc"
                };

                var result = await WicsService.GetGroups(query);
                playlists = result.Value.AsODataEnumerable();

                // 선택된 플레이리스트 딕셔너리 초기화
                selectedPlaylists.Clear();
                foreach (var playlist in playlists)
                {
                    selectedPlaylists[playlist.Id] = false;
                }

                // 각 플레이리스트의 미디어 개수를 비동기로 로드
                foreach (var playlist in playlists)
                {
                    _ = LoadPlaylistMediaCount(playlist.Id);
                }
            }
            catch (Exception ex)
            {
                NotifyError("플레이리스트 목록을 불러오는 중 오류가 발생했습니다", ex);
            }
            finally
            {
                isLoadingPlaylists = false;
            }
        }

        // 플레이리스트 카드 클릭 시
        private async Task OnPlaylistCardClicked(WicsPlatform.Server.Models.wics.Group playlist)
        {
            selectedPlaylist = playlist;

            // 체크박스로 선택된 플레이리스트가 있으면 전체 집계, 아니면 단일 플레이리스트 로드
            if (GetSelectedPlaylists().Any())
            {
                await LoadSelectedPlaylistsMedia();
            }
            else
            {
                await LoadPlaylistMedia(playlist.Id);
            }
        }

        // 플레이리스트 선택 상태 확인
        private bool IsPlaylistSelected(ulong playlistId)
        {
            return selectedPlaylists.ContainsKey(playlistId) && selectedPlaylists[playlistId];
        }

        // 미디어 선택 상태 확인
        private bool IsMediaSelected(ulong mediaId)
        {
            return selectedMedia.ContainsKey(mediaId) && selectedMedia[mediaId];
        }

        // 플레이리스트 체크박스 변경 시
        private async Task OnPlaylistCheckChanged(ulong playlistId, bool isChecked)
        {
            selectedPlaylists[playlistId] = isChecked;

            // 선택된 플레이리스트 기준으로 미디어 집계
            await LoadSelectedPlaylistsMedia();

            // 선택이 모두 해제되면 단일 선택도 해제
            if (!GetSelectedPlaylists().Any())
            {
                selectedPlaylist = null;
            }

            StateHasChanged();
        }

        // 하나의 플레이리스트에 속한 미디어 파일 로드 (단일)
        private async Task LoadPlaylistMedia(ulong playlistId)
        {
            try
            {
                isLoadingMedia = true;
                playlistMedia = new List<WicsPlatform.Server.Models.wics.Medium>();
                selectedMedia.Clear();
                selectAllMedia = true; // 기본적으로 모든 미디어 선택

                Console.WriteLine($"Loading media for playlist ID: {playlistId}");

                // MapMediaGroups를 통해 조회 - MediaId로 Media 목록을 가져옴
                var mapQuery = new Radzen.Query
                {
                    Filter = $"GroupId eq {playlistId}",
                    OrderBy = "CreatedAt desc"
                };

                var mapResult = await WicsService.GetMapMediaGroups(mapQuery);

                if (mapResult.Value != null && mapResult.Value.Any())
                {
                    Console.WriteLine($"Found {mapResult.Value.Count()} mappings");

                    // MediaId 목록 추출
                    var mediaIds = mapResult.Value.Select(m => m.MediaId).Where(id => id > 0).Distinct().ToList();

                    if (mediaIds.Any())
                    {
                        // Media 테이블에서 해당 ID들의 미디어 정보 조회
                        var mediaFilter = string.Join(" or ", mediaIds.Select(id => $"Id eq {id}"));
                        var mediaQuery = new Radzen.Query
                        {
                            Filter = $"({mediaFilter}) and (DeleteYn eq 'N' or DeleteYn eq null)",
                            OrderBy = "CreatedAt desc"
                        };

                        var mediaResult = await WicsService.GetMedia(mediaQuery);

                        if (mediaResult.Value != null && mediaResult.Value.Any())
                        {
                            playlistMedia = mediaResult.Value.AsODataEnumerable();
                            Console.WriteLine($"PlaylistMedia assigned: {playlistMedia.Count()} items");

                            // 모든 미디어를 기본적으로 선택
                            foreach (var media in playlistMedia)
                            {
                                selectedMedia[media.Id] = true;
                                Console.WriteLine($"- Media: {media.FileName}, ID: {media.Id}");
                            }
                        }
                    }
                }
                else
                {
                    Console.WriteLine("No media found in MapMediaGroups");
                    playlistMedia = new List<WicsPlatform.Server.Models.wics.Medium>();
                    selectAllMedia = false;
                }

                StateHasChanged();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading media: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");

                NotifyError("미디어 파일 목록을 불러오는 중 오류가 발생했습니다", ex);

                playlistMedia = new List<WicsPlatform.Server.Models.wics.Medium>();
                selectAllMedia = false;
            }
            finally
            {
                isLoadingMedia = false;
                StateHasChanged();
            }
        }

        // 여러 플레이리스트에 속한 미디어 파일 집계 로드 (멀티)
        private async Task LoadSelectedPlaylistsMedia()
        {
            try
            {
                isLoadingMedia = true;
                playlistMedia = new List<WicsPlatform.Server.Models.wics.Medium>();
                selectedMedia.Clear();
                selectAllMedia = true;

                var selectedIds = selectedPlaylists.Where(kvp => kvp.Value).Select(kvp => kvp.Key).Distinct().ToList();
                if (!selectedIds.Any())
                {
                    selectAllMedia = false;
                    StateHasChanged();
                    return;
                }

                // MapMediaGroups에서 선택된 모든 그룹에 대한 매핑 조회
                var mapFilter = string.Join(" or ", selectedIds.Select(id => $"GroupId eq {id}"));
                var mapQuery = new Radzen.Query
                {
                    Filter = mapFilter,
                    OrderBy = "CreatedAt desc"
                };
                var mapResult = await WicsService.GetMapMediaGroups(mapQuery);

                if (mapResult.Value != null && mapResult.Value.Any())
                {
                    var mediaIds = mapResult.Value
                        .Select(m => m.MediaId)
                        .Where(id => id > 0)
                        .Distinct()
                        .ToList();

                    if (mediaIds.Any())
                    {
                        var mediaFilter = string.Join(" or ", mediaIds.Select(id => $"Id eq {id}"));
                        var mediaQuery = new Radzen.Query
                        {
                            Filter = $"({mediaFilter}) and (DeleteYn eq 'N' or DeleteYn eq null)",
                            OrderBy = "CreatedAt desc"
                        };

                        var mediaResult = await WicsService.GetMedia(mediaQuery);
                        if (mediaResult.Value != null && mediaResult.Value.Any())
                        {
                            playlistMedia = mediaResult.Value.AsODataEnumerable();

                            // 기본적으로 모두 선택
                            foreach (var media in playlistMedia)
                            {
                                selectedMedia[media.Id] = true;
                            }
                        }
                    }
                }
                else
                {
                    playlistMedia = new List<WicsPlatform.Server.Models.wics.Medium>();
                    selectAllMedia = false;
                }

                // 전체 선택 상태 계산
                selectAllMedia = playlistMedia.Any() && playlistMedia.All(m => selectedMedia.ContainsKey(m.Id) && selectedMedia[m.Id]);

                StateHasChanged();
            }
            catch (Exception ex)
            {
                NotifyError("선택된 플레이리스트의 미디어를 불러오는 중 오류가 발생했습니다", ex);
                playlistMedia = new List<WicsPlatform.Server.Models.wics.Medium>();
                selectAllMedia = false;
            }
            finally
            {
                isLoadingMedia = false;
                StateHasChanged();
            }
        }

        // 미디어 체크박스 변경 시
        private void OnMediaCheckChanged(ulong mediaId, bool isChecked)
        {
            selectedMedia[mediaId] = isChecked;

            // 전체 선택 상태 업데이트
            selectAllMedia = playlistMedia.All(m => selectedMedia.ContainsKey(m.Id) && selectedMedia[m.Id]);

            StateHasChanged();
        }

        // 전체 선택 체크박스 변경 시
        private void OnSelectAllMediaChanged(bool isChecked)
        {
            selectAllMedia = isChecked;

            foreach (var media in playlistMedia)
            {
                selectedMedia[media.Id] = isChecked;
            }

            StateHasChanged();
        }

        // 플레이리스트의 미디어 개수 가져오기
        private string GetPlaylistMediaCount(ulong playlistId)
        {
            if (playlistMediaCounts.ContainsKey(playlistId))
            {
                return $"{playlistMediaCounts[playlistId]}개";
            }

            // 비동기로 카운트 로드
            _ = LoadPlaylistMediaCount(playlistId);

            return "로딩중...";
        }

        private async Task LoadPlaylistMediaCount(ulong playlistId)
        {
            try
            {
                int count = 0;

                // MapMediaGroups에서 조회
                var mapQuery = new Radzen.Query
                {
                    Filter = $"GroupId eq {playlistId}"
                };
                var mapResult = await WicsService.GetMapMediaGroups(mapQuery);

                if (mapResult.Value != null)
                {
                    count = mapResult.Value.Count();
                }

                playlistMediaCounts[playlistId] = count;

                StateHasChanged();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading media count for playlist {playlistId}: {ex.Message}");
                playlistMediaCounts[playlistId] = 0;
            }
        }

        /* ────────────────────── [Public Methods] ────────────────────── */

        // 선택된 플레이리스트 목록 가져오기
        public IEnumerable<WicsPlatform.Server.Models.wics.Group> GetSelectedPlaylists()
        {
            return playlists.Where(p => selectedPlaylists.ContainsKey(p.Id) && selectedPlaylists[p.Id]);
        }

        // 선택된 미디어 목록 가져오기
        public IEnumerable<WicsPlatform.Server.Models.wics.Medium> GetSelectedMedia()
        {
            // playlistMedia가 아닌 allMedia에서 선택된 미디어 가져오기
            // 이렇게 해야 우측 전체 미디어 목록에서 선택한 미디어도 포함됨
            return allMedia.Where(m => selectedMedia.ContainsKey(m.Id) && selectedMedia[m.Id]);
        }

        // 선택된 미디어가 있는지 확인
        public bool HasSelectedMedia()
        {
            return selectedMedia.Any(kvp => kvp.Value);
        }

        // 미디어 재생 상태 초기화 (외부에서 호출 가능)
        public void ResetMediaPlaybackState()
        {
            isMediaPlaying = false;
            currentMediaSessionId = null;
            StateHasChanged();
        }

        // 미디어 재생 시 현재 위치로 이동
        private async Task SeekToCurrentMediaPosition()
        {
            if (!IsBroadcasting || string.IsNullOrEmpty(BroadcastId) || string.IsNullOrEmpty(currentMediaSessionId))
            {
                return;
            }

            try
            {
                // 현재 미디어의 시작 시간을 가져오기 위해 일단 정지
                await StopMedia();

                // 잠시 대기
                await Task.Delay(500);

                // 다시 재생 요청
                await Seek(0);
                await PlayMediaInternal(shuffle: false);
            }
            catch (Exception ex)
            {
                NotifyError("미디어 위치 변경 중 오류가 발생했습니다", ex);
            }
        }

        private string GetPlaylistItemClass(ulong id)
        {
            var isSel = selectedPlaylist?.Id == id;
            return $"playlist-item {(isSel ? "selected" : string.Empty)}";
        }

        // 헤더 표시 텍스트
        private string GetMediaHeaderIndicator()
        {
            var sel = GetSelectedPlaylists().ToList();
            if (sel.Count == 1) return $"({sel[0].Name})";
            if (sel.Count > 1) return $"(선택된 플레이리스트 {sel.Count}개)";
            if (selectedPlaylist != null) return $"({selectedPlaylist.Name})";
            return null;
        }

        /* ────────────────────── [UI 관련 메서드 추가] ────────────────────── */
        // 전체 미디어 로드
        private async Task LoadAllMedia()
        {
            try
            {
                isLoadingMedia = true;
                var query = new Radzen.Query
                {
                    Filter = "(DeleteYn eq 'N' or DeleteYn eq null)",
                    OrderBy = "CreatedAt desc"
                };

                var result = await WicsService.GetMedia(query);
                allMedia = result.Value.AsODataEnumerable();

                foreach (var media in allMedia)
                {
                    if (!selectedMedia.ContainsKey(media.Id))
                    {
                        selectedMedia[media.Id] = false;
                    }
                }
            }
            catch (Exception ex)
            {
                NotifyError("미디어 목록을 불러오는 중 오류가 발생했습니다", ex);
            }
            finally
            {
                isLoadingMedia = false;
            }
        }

        // 미디어-그룹 매핑 로드
        private async Task LoadMediaGroupMappings()
        {
            try
            {
                var query = new Radzen.Query
                {
                    Expand = "Group,Medium",
                    Filter = "(DeleteYn eq 'N' or DeleteYn eq null) and (Medium/DeleteYn eq 'N' or Medium/DeleteYn eq null)"
                };

                var result = await WicsService.GetMapMediaGroups(query);
                mediaGroupMappings = result.Value.AsODataEnumerable();
            }
            catch (Exception ex)
            {
                NotifyError("미디어 그룹 매핑을 불러오는 중 오류가 발생했습니다", ex);
            }
        }

        // 플레이리스트 확장/축소 토글
        private void TogglePlaylistExpansion(ulong playlistId)
        {
            if (expandedPlaylists.Contains(playlistId))
            {
                expandedPlaylists.Remove(playlistId);
            }
            else
            {
                expandedPlaylists.Add(playlistId);
            }
            StateHasChanged();
        }

        // 플레이리스트 상세 보기
        private void ViewPlaylistDetails(WicsPlatform.Server.Models.wics.Group playlist)
        {
            viewingPlaylist = playlist;
            bool isCurrentlySelected = selectedPlaylists.ContainsKey(playlist.Id) && selectedPlaylists[playlist.Id];
            OnPlaylistSelectionChanged(playlist.Id, !isCurrentlySelected);
        }

        // 플레이리스트 선택 변경
        private void OnPlaylistSelectionChanged(ulong playlistId, bool isSelected)
        {
            selectedPlaylists[playlistId] = isSelected;

            if (isSelected)
            {
                var mediaInPlaylist = GetMediaInPlaylistSync(playlistId).Select(m => m.Id);
                foreach (var mediaId in mediaInPlaylist)
                {
                    selectedMedia[mediaId] = true;
                }
            }
            else
            {
                var mediaInPlaylist = GetMediaInPlaylistSync(playlistId).Select(m => m.Id);
                foreach (var mediaId in mediaInPlaylist)
                {
                    selectedMedia[mediaId] = false;
                }
            }

            StateHasChanged();
        }

        // 개별 미디어 선택 변경
        private async Task OnMediaSelectionChanged(ulong mediaId, bool isSelected)
        {
            selectedMedia[mediaId] = isSelected;
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
                    _logger.LogError(ex, "refreshDragDrop 호출 실패");
                }
            }
        }

        // 플레이리스트의 미디어 개수 가져오기 (int)
        private int GetPlaylistMediaCountInt(ulong playlistId)
        {
            // mediaGroupMappings에서 실제 개수를 계산하여 일관성 유지
            var count = mediaGroupMappings
                .Where(m => m.GroupId == playlistId)
                .Select(m => m.MediaId)
                .Distinct()
                .Count();
            
            return count;
        }

        // 플레이리스트 내 미디어 목록 가져오기
        private IEnumerable<WicsPlatform.Server.Models.wics.Medium> GetMediaInPlaylistSync(ulong playlistId)
        {
            var mediaIds = mediaGroupMappings
                .Where(m => m.GroupId == playlistId)
                .Select(m => m.MediaId)
                .Distinct();

            return allMedia.Where(m => mediaIds.Contains(m.Id));
        }

        // 선택 초기화
        public void ClearSelection()
        {
            foreach (var key in selectedPlaylists.Keys.ToList())
            {
                selectedPlaylists[key] = false;
            }
            foreach (var key in selectedMedia.Keys.ToList())
            {
                selectedMedia[key] = false;
            }
            viewingPlaylist = null;
            StateHasChanged();
        }

        // 플레이리스트 추가 다이얼로그 열기
        protected async Task OpenAddPlaylistDialog()
        {
            var result = await DialogService.OpenAsync<WicsPlatform.Client.Dialogs.AddPlaylistDialog>("플레이리스트 추가",
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
                await LoadPlaylists();
            }
        }

        // 미디어 추가 다이얼로그 열기
        protected async Task OpenAddMediaDialog()
        {
            var result = await DialogService.OpenAsync<WicsPlatform.Client.Dialogs.AddMediaDialog>("미디어 파일 추가",
                null,
                new DialogOptions
                {
                    Width = "650px",
                    Height = "auto",
                    Resizable = false,
                    Draggable = true
                });

            if (result == true)
            {
                // 미디어 목록 새로고침
                await LoadAllMedia();
                await LoadMediaGroupMappings();
                StateHasChanged();
            }
        }

        // 미디어를 플레이리스트에서 제거하는 다이얼로그 열기
        protected async Task OpenRemoveMediaFromPlaylistDialog(WicsPlatform.Server.Models.wics.Medium media, WicsPlatform.Server.Models.wics.Group playlist)
        {
            var result = await DialogService.Confirm(
                $"'{media.FileName}' 미디어를 '{playlist.Name}' 플레이리스트에서 제거하시겠습니까?",
                "미디어 제거",
                new ConfirmOptions()
                {
                    OkButtonText = "제거",
                    CancelButtonText = "취소"
                });

            if (result == true)
            {
                await RemoveMediaFromPlaylist(media, playlist);
            }
        }

        // 미디어를 플레이리스트에서 제거
        protected async Task RemoveMediaFromPlaylist(WicsPlatform.Server.Models.wics.Medium media, WicsPlatform.Server.Models.wics.Group playlist)
        {
            try
            {
                var mapping = mediaGroupMappings
                    .FirstOrDefault(m => m.MediaId == media.Id && m.GroupId == playlist.Id);

                if (mapping != null)
                {
                    // 매핑 삭제 (하드 삭제)
                    var response = await Http.DeleteAsync($"odata/wics/MapMediaGroups({mapping.Id})");

                    if (response.IsSuccessStatusCode)
                    {
                        NotifySuccess($"'{media.FileName}' 미디어가 '{playlist.Name}' 플레이리스트에서 제거되었습니다.");

                        // 데이터 새로고침
                        await LoadMediaGroupMappings();
                        await LoadPlaylists();

                        // 플레이리스트 목록 새로고침
                        if (expandedPlaylists.Contains(playlist.Id))
                        {
                            StateHasChanged();
                        }
                    }
                    else
                    {
                        NotifyError("제거 실패", new Exception("미디어를 플레이리스트에서 제거하는 중 오류가 발생했습니다."));
                    }
                }
            }
            catch (Exception ex)
            {
                NotifyError("미디어 제거 중 오류 발생", ex);
                _logger.LogError(ex, "미디어 제거 중 오류");
            }
        }

        // 미디어 재생/정지 토글
        protected async Task PlayMedia(WicsPlatform.Server.Models.wics.Medium media)
        {
            if (jsPlayer == null)
            {
                NotifyWarning("미디어 플레이어를 초기화하는 중입니다. 잠시 후 다시 시도해주세요.");
                return;
            }

            try
            {
                // 현재 재생 중인 미디어를 클릭한 경우 정지
                if (playingMediaId == media.Id)
                {
                    await jsPlayer.InvokeVoidAsync("stopMedia");
                    playingMediaId = null;
                    NotifyInfo($"'{media.FileName}' 재생을 중지했습니다.");
                    StateHasChanged();
                    return;
                }

                // 다른 미디어 재생
                var url = media.FullPath;
                if (!string.IsNullOrEmpty(url) && !url.StartsWith("/Uploads/", StringComparison.OrdinalIgnoreCase))
                {
                    url = $"/Uploads/{System.IO.Path.GetFileName(url)}";
                }

                await jsPlayer.InvokeVoidAsync("playMedia", url);
                playingMediaId = media.Id;
                NotifyInfo($"'{media.FileName}' 재생 중...");
                StateHasChanged();
            }
            catch (Exception ex)
            {
                NotifyError("미디어 재생 실패", ex);
                _logger.LogError(ex, "미디어 재생 중 오류");
                playingMediaId = null;
            }
        }

        // 미디어가 재생 중인지 확인
        protected bool IsMediaPlaying(ulong mediaId)
        {
            return playingMediaId == mediaId;
        }

        // 미디어 삭제 다이얼로그 열기
        protected async Task OpenDeleteMediaDialog(WicsPlatform.Server.Models.wics.Medium media)
        {
            var result = await DialogService.Confirm(
                $"'{media.FileName}' 미디어를 삭제하시겠습니까?\n\n삭제된 미디어는 모든 플레이리스트에서 제거됩니다.",
                "미디어 삭제",
                new ConfirmOptions()
                {
                    OkButtonText = "삭제",
                    CancelButtonText = "취소"
                });

            if (result == true)
            {
                await DeleteMedia(media);
            }
        }

        // 미디어 삭제
        protected async Task DeleteMedia(WicsPlatform.Server.Models.wics.Medium media)
        {
            try
            {
                // 1. 해당 미디어와 연결된 모든 MapMediaGroup 찾기
                var mapQuery = new Radzen.Query
                {
                    Filter = $"MediaId eq {media.Id}"
                };
                var mapResult = await WicsService.GetMapMediaGroups(mapQuery);
                
                if (mapResult.Value != null && mapResult.Value.Any())
                {
                    // 모든 매핑 레코드 삭제
                    foreach (var mapping in mapResult.Value)
                    {
                        mapping.DeleteYn = "Y";
                        mapping.UpdatedAt = DateTime.Now;
                        await WicsService.UpdateMapMediaGroup(mapping.Id, mapping);
                    }
                }

                // 2. 미디어 Soft Delete: DeleteYn을 Y로 변경
                media.DeleteYn = "Y";
                media.UpdatedAt = DateTime.Now;
                await WicsService.UpdateMedium(media.Id, media);
                
                NotifySuccess($"'{media.FileName}' 미디어가 모든 플레이리스트에서 제거되고 삭제되었습니다.");

                // 데이터 새로고침
                await LoadAllMedia();
                await LoadMediaGroupMappings();
                StateHasChanged();
            }
            catch (Exception ex)
            {
                NotifyError("미디어 삭제 중 오류 발생", ex);
                _logger.LogError(ex, "미디어 삭제 중 오류");
            }
        }

        /* ────────────────────── [Drag & Drop Handlers] ────────────────────── */
        protected void OnRowRender(RowRenderEventArgs<WicsPlatform.Server.Models.wics.Medium> args)
        {
            if (IsMediaSelected(args.Data.Id))
            {
                args.Attributes.Add("draggable", "true");
                
                if (args.Attributes.TryGetValue("class", out var existingClass))
                {
                    args.Attributes["class"] = $"{existingClass} media-draggable";
                }
                else
                {
                    args.Attributes.Add("class", "media-draggable");
                }
            }
        }

        [JSInvokable]
        public async Task HandleDropFromJS(ulong playlistId)
        {
            _logger.LogInformation($"===== HandleDropFromJS 호출됨! PlaylistId: {playlistId}, 선택된 미디어 수: {selectedMedia.Count(kvp => kvp.Value)} =====");
            
            var playlist = playlists.FirstOrDefault(p => p.Id == playlistId);
            if (playlist != null && selectedMedia.Any(kvp => kvp.Value))
            {
                await AddSelectedMediaToPlaylist(playlist);
            }
            
            isDragging = false;
            isDraggingOverPlaylist = null;
            await InvokeAsync(StateHasChanged);
        }

        protected void HandleDragStart(WicsPlatform.Server.Models.wics.Medium media)
        {
            isDragging = true;
            _logger.LogInformation($"드래그 시작: {media.FileName} (ID: {media.Id})");
        }

        protected void HandleDragEnd(DragEventArgs e)
        {
            isDragging = false;
            isDraggingOverPlaylist = null;
            _logger.LogInformation("드래그 종료");
        }

        protected void HandleDragEnter(WicsPlatform.Server.Models.wics.Group playlist)
        {
            if (isDragging && selectedMedia.Any(kvp => kvp.Value))
            {
                isDraggingOverPlaylist = playlist.Id;
                _logger.LogInformation($"드래그 오버: {playlist.Name}");
            }
        }

        protected void HandleDragLeave(WicsPlatform.Server.Models.wics.Group playlist)
        {
            if (isDraggingOverPlaylist == playlist.Id)
            {
                isDraggingOverPlaylist = null;
            }
        }

        // 선택된 미디어들을 플레이리스트에 추가
        protected async Task AddSelectedMediaToPlaylist(WicsPlatform.Server.Models.wics.Group playlist)
        {
            var selectedMediaIds = selectedMedia.Where(kvp => kvp.Value).Select(kvp => kvp.Key).ToList();
            
            if (!selectedMediaIds.Any())
            {
                NotifyWarning("추가할 미디어를 선택하세요.");
                return;
            }

            if (isAddingToPlaylist)
            {
                _logger.LogInformation("이미 처리 중입니다.");
                return;
            }

            try
            {
                isAddingToPlaylist = true;
                StateHasChanged();

                var mediaToAdd = allMedia.Where(m => selectedMediaIds.Contains(m.Id)).ToList();

                addProgress = 0;
                addTotal = mediaToAdd.Count;
                StateHasChanged();

                int successCount = 0;
                int failCount = 0;
                int alreadyExistsCount = 0;

                await LoadMediaGroupMappings();

                foreach (var media in mediaToAdd)
                {
                    try
                    {
                        var existingMapping = mediaGroupMappings
                            .FirstOrDefault(m => m.MediaId == media.Id && m.GroupId == playlist.Id);

                        if (existingMapping != null)
                        {
                            alreadyExistsCount++;
                            continue;
                        }

                        var mapMediaGroup = new WicsPlatform.Server.Models.wics.MapMediaGroup
                        {
                            MediaId = media.Id,
                            GroupId = playlist.Id,
                            CreatedAt = DateTime.Now,
                            UpdatedAt = DateTime.Now
                        };

                        var response = await Http.PostAsJsonAsync("odata/wics/MapMediaGroups", mapMediaGroup);

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
                    NotifySuccess($"{successCount}개의 미디어가 '{playlist.Name}' 플레이리스트에 추가되었습니다." +
                                (alreadyExistsCount > 0 ? $" ({alreadyExistsCount}개는 이미 플레이리스트에 속해있음)" : "") +
                                (failCount > 0 ? $" ({failCount}개 실패)" : ""));

                    await LoadMediaGroupMappings();
                    await LoadPlaylists();
                    StateHasChanged();
                }
                else if (alreadyExistsCount > 0)
                {
                    NotifyInfo($"선택한 미디어들은 이미 '{playlist.Name}' 플레이리스트에 속해있습니다.");
                }
                else
                {
                    NotifyWarning("미디어를 플레이리스트에 추가하는데 실패했습니다.");
                }
            }
            catch (Exception ex)
            {
                NotifyError("미디어 플레이리스트 추가 중 오류 발생", ex);
                _logger.LogError(ex, "미디어 플레이리스트 추가 중 오류");
            }
            finally
            {
                addProgress = addTotal;
                isAddingToPlaylist = false;
                StateHasChanged();
            }
        }

        /* ────────────────────── [Helpers] ────────────────────── */
        private void NotifySuccess(string message) => Notify(NotificationSeverity.Success, "완료", message);
        private void NotifyInfo(string message) => Notify(NotificationSeverity.Info, "안내", message);
        private void NotifyWarning(string message) => Notify(NotificationSeverity.Warning, "경고", message);
        private void NotifyError(string summary, Exception ex) =>
            Notify(NotificationSeverity.Error, summary, ex.Message);

        private void Notify(NotificationSeverity severity, string summary, string detail) =>
            NotificationService.Notify(new NotificationMessage
            {
                Severity = severity,
                Summary = summary,
                Detail = detail,
                Duration = 4000
            });
    }
}