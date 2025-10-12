using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using Radzen;
using System.Net.Http;
using System.Net.Http.Json;
using WicsPlatform.Shared;

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
        [Inject] protected wicsService WicsService { get; set; }
        [Inject] protected HttpClient Http { get; set; }

        [Inject] protected ILogger<BroadcastPlaylistSection> _logger { get; set; }

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

        /* ────────────────────── [Life-Cycle] ────────────────────── */
        protected override async Task OnInitializedAsync()
        {
            await LoadPlaylists();
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
            }
            catch { }
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
            return playlistMedia.Where(m => selectedMedia.ContainsKey(m.Id) && selectedMedia[m.Id]);
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

        private bool HasAnyPlaylistSelected()
        {
            return selectedPlaylist != null || GetSelectedPlaylists().Any();
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