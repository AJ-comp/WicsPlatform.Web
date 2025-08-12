using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using Radzen;

namespace WicsPlatform.Client.Pages.SubPages
{
    public partial class BroadcastPlaylistSection
    {
        [Parameter] public WicsPlatform.Server.Models.wics.Channel Channel { get; set; }
        [Parameter] public bool IsCollapsed { get; set; }
        [Parameter] public EventCallback<bool> IsCollapsedChanged { get; set; }

        [Inject] protected NotificationService NotificationService { get; set; }
        [Inject] protected wicsService WicsService { get; set; }

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

        protected override async Task OnInitializedAsync()
        {
            await LoadPlaylists();
        }

        private async Task TogglePanel()
        {
            await IsCollapsedChanged.InvokeAsync(!IsCollapsed);
        }

        // 플레이리스트 로드
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
                NotificationService.Notify(new NotificationMessage
                {
                    Severity = NotificationSeverity.Error,
                    Summary = "오류",
                    Detail = $"플레이리스트 목록을 불러오는 중 오류가 발생했습니다: {ex.Message}",
                    Duration = 4000
                });
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

        // 플레이리스트 선택 시 (기존 메서드 - ListBox용)
        private async Task OnPlaylistSelected(object value)
        {
            Console.WriteLine($"OnPlaylistSelected called. SelectedPlaylist: {selectedPlaylist?.Name ?? "null"}");

            if (selectedPlaylist != null)
            {
                await LoadPlaylistMedia(selectedPlaylist.Id);
            }
            else
            {
                playlistMedia = new List<WicsPlatform.Server.Models.wics.Medium>();
            }

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

                NotificationService.Notify(new NotificationMessage
                {
                    Severity = NotificationSeverity.Error,
                    Summary = "오류",
                    Detail = $"미디어 파일 목록을 불러오는 중 오류가 발생했습니다: {ex.Message}",
                    Duration = 4000
                });

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
    }
}
