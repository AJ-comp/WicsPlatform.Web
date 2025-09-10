using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Radzen;
using System.Net.Http.Json;

namespace WicsPlatform.Client.Dialogs;

public partial class AddMediaToPlaylistDialog
{
    [Inject]
    protected IJSRuntime JSRuntime { get; set; }

    [Inject]
    protected DialogService DialogService { get; set; }

    [Inject]
    protected NotificationService NotificationService { get; set; }

    [Inject]
    protected HttpClient Http { get; set; }

    [Inject]
    protected wicsService WicsService { get; set; }

    [Parameter]
    public IEnumerable<WicsPlatform.Server.Models.wics.Medium> SelectedMedia { get; set; }

    protected IEnumerable<WicsPlatform.Server.Models.wics.Medium> selectedMedia => SelectedMedia ?? new List<WicsPlatform.Server.Models.wics.Medium>();
    protected IEnumerable<PlaylistViewModel> playlists;
    protected ulong selectedPlaylistId;
    protected string error;
    protected bool errorVisible;
    protected bool isLoading = true;
    protected bool isProcessing = false;

    protected override async Task OnInitializedAsync()
    {
        await LoadPlaylists();
    }

    protected async Task LoadPlaylists()
    {
        try
        {
            isLoading = true;

            var query = new Query
            {
                Filter = "Type eq 1 and (DeleteYn eq 'N')"
            };

            var result = await WicsService.GetGroups(query);

            if (result != null && result.Value != null)
            {
                playlists = result.Value.Select(p => new PlaylistViewModel
                {
                    Id = p.Id,
                    Name = p.Name,
                    Description = p.Description
                }).ToList();

                if (playlists.Any())
                {
                    selectedPlaylistId = playlists.First().Id;
                }
            }
        }
        catch (Exception ex)
        {
            errorVisible = true;
            error = $"플레이리스트 목록을 불러오는 중 오류가 발생했습니다: {ex.Message}";
        }
        finally
        {
            isLoading = false;
        }
    }

    protected async Task AddToPlaylist()
    {
        if (selectedPlaylistId == 0 || !selectedMedia.Any())
        {
            errorVisible = true;
            error = "플레이리스트를 선택하고 미디어가 선택되어 있어야 합니다.";
            return;
        }

        try
        {
            isProcessing = true;
            errorVisible = false;

            int successCount = 0;
            int failCount = 0;

            // 선택한 각 미디어 파일을 플레이리스트에 추가
            foreach (var media in selectedMedia)
            {
                try
                {
                    var checkQuery = new Query { Filter = $"MediaId eq {media.Id} and GroupId eq {selectedPlaylistId}" };
                    var existing = await WicsService.GetMapMediaGroups(checkQuery);
                    if (existing != null && existing.Value != null && existing.Value.Any())
                    {
                        continue;
                    }

                    var map = new WicsPlatform.Server.Models.wics.MapMediaGroup
                    {
                        MediaId = media.Id,
                        GroupId = selectedPlaylistId,
                        CreatedAt = DateTime.Now,
                        UpdatedAt = DateTime.Now
                    };

                    var response = await Http.PostAsJsonAsync("odata/wics/MapMediaGroups", map);

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

            // 결과 알림 표시
            if (successCount > 0)
            {
                var playlistName = playlists.FirstOrDefault(p => p.Id == selectedPlaylistId)?.Name;
                NotificationService.Notify(new NotificationMessage
                {
                    Severity = NotificationSeverity.Success,
                    Summary = "추가 완료",
                    Detail = $"{successCount}개의 미디어가 '{playlistName}' 플레이리스트에 추가되었습니다." +
                            (failCount > 0 ? $" ({failCount}개 실패)" : ""),
                    Duration = 4000
                });

                // 다이얼로그 닫기 (true를 반환하여 변경이 있었음을 알림)
                DialogService.Close(true);
            }
            else if (failCount > 0)
            {
                errorVisible = true;
                error = "모든 미디어를 플레이리스트에 추가하는데 실패했습니다.";
            }
        }
        catch (Exception ex)
        {
            errorVisible = true;
            error = $"플레이리스트에 추가하는 중 오류가 발생했습니다: {ex.Message}";
        }
        finally
        {
            isProcessing = false;
        }
    }

    protected async Task CancelClick()
    {
        DialogService.Close(null);
    }
}

// 플레이리스트 뷰 모델
public class PlaylistViewModel
{
    public ulong Id { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
}
