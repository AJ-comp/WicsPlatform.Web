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
    public partial class BroadcastPlaylistSection
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

        /* ────────────────────── [Life-Cycle] ────────────────────── */
        protected override async Task OnInitializedAsync()
        {
            await LoadPlaylists();
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

        /* ────────────────────── [Panel Toggle] ────────────────────── */
        private async Task TogglePanel()
        {
            await IsCollapsedChanged.InvokeAsync(!IsCollapsed);
        }

        /* ────────────────────── [미디어 재생/중지] ────────────────────── */
        private async Task PlayMedia()
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
                var request = new
                {
                    broadcastId = BroadcastId,
                    mediaIds = mediaIds
                };

                var response = await Http.PostAsJsonAsync("api/mediaplayer/play", request);

                if (response.IsSuccessStatusCode)
                {
                    var result = await response.Content.ReadFromJsonAsync<MediaPlayResponse>();
                    if (result.Success)
                    {
                        isMediaPlaying = true;
                        currentMediaSessionId = result.SessionId;
                        NotifySuccess($"미디어 재생을 시작했습니다. ({mediaIds.Count}개 파일)");
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
            await LoadPlaylistMedia(playlist.Id);
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
        private void OnPlaylistCheckChanged(ulong playlistId, bool isChecked)
        {
            selectedPlaylists[playlistId] = isChecked;
            StateHasChanged();
        }

        // 플레이리스트의 미디어 파일 로드
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