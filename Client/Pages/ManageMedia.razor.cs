using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.JSInterop;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.Forms;
using Radzen;
using Radzen.Blazor;
using System.Net.Http;
using System.Net.Http.Json;
using WicsPlatform.Client.Dialogs;
using System.IO;

namespace WicsPlatform.Client.Pages
{
    public partial class ManageMedia : IAsyncDisposable
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
        private IJSObjectReference? _jsPlayer;
        private DotNetObjectReference<ManageMedia>? _dotNetRef;

        // 파일 업로드 관련 필드
        protected bool isDraggingOver = false;
        protected bool isUploading = false;
        protected int uploadProgress = 0;
        protected string uploadingFileName = "";
        protected string fileInputKey = Guid.NewGuid().ToString();

        // 미디어 관련 필드
        protected RadzenDataGrid<WicsPlatform.Server.Models.wics.Medium> mediaGrid;
        protected IEnumerable<WicsPlatform.Server.Models.wics.Medium> media = new List<WicsPlatform.Server.Models.wics.Medium>();
        protected IEnumerable<WicsPlatform.Server.Models.wics.Medium> allMedia = new List<WicsPlatform.Server.Models.wics.Medium>();
        protected bool isLoading = true;

        // 플레이리스트(그룹) 관련 필드
        protected IEnumerable<WicsPlatform.Server.Models.wics.Group> playlists = new List<WicsPlatform.Server.Models.wics.Group>();
        protected bool isLoadingPlaylists = true;

        // 필터 관련 필드
        protected string mediaNameFilter = "";
        protected ulong? playlistFilter = null;

        // 확장/축소 관련 필드
        protected HashSet<ulong> expandedPlaylists = new HashSet<ulong>();

        // 선택된 미디어와 플레이리스트
        protected IList<WicsPlatform.Server.Models.wics.Medium> selectedMedia = new List<WicsPlatform.Server.Models.wics.Medium>();
        protected IList<WicsPlatform.Server.Models.wics.Group> selectedPlaylists = new List<WicsPlatform.Server.Models.wics.Group>();

        // 미디어-플레이리스트 매핑 데이터
        protected IEnumerable<WicsPlatform.Server.Models.wics.MapMediaGroup> mediaPlaylistMappings = new List<WicsPlatform.Server.Models.wics.MapMediaGroup>();

        // 드래그 앤 드롭 관련 필드
        protected bool isDragging = false;
        protected ulong? isDraggingOverPlaylist = null;
        protected WicsPlatform.Server.Models.wics.Medium draggingMedia = null;

        // 중복 방지를 위한 처리 중 플래그
        private bool isAddingToPlaylist = false;

        protected override async Task OnInitializedAsync()
        {
            await LoadPlaylists();
            await LoadMedia();
            await LoadMediaPlaylistMappings();
        }

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            if (firstRender)
            {
                try
                {
                    _jsModule = await JSRuntime.InvokeAsync<IJSObjectReference>("import", "./js/mediadragdrop.js");
                    _jsPlayer = await JSRuntime.InvokeAsync<IJSObjectReference>("import", "./js/mediaplayer.js");
                    _dotNetRef = DotNetObjectReference.Create(this);
                    await _jsModule.InvokeVoidAsync("initializeMediaDragDrop", _dotNetRef);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"드래그 드롭 초기화 실패: {ex.Message}");
                }
            }
            else
            {
                // 미디어 목록이 변경되었을 때 드래그 드롭 재초기화
                if (_jsModule != null && _dotNetRef != null)
                {
                    await _jsModule.InvokeVoidAsync("refreshMediaDragDrop", _dotNetRef);
                }
            }
        }

        // 파일 업로드 관련 메서드
        protected async Task OnInputFileChange(InputFileChangeEventArgs e)
        {
            var files = e.GetMultipleFiles();
            foreach (var file in files)
            {
                await UploadFile(file);
            }
        }

        protected async Task UploadFile(IBrowserFile file)
        {
            try
            {
                isUploading = true;
                uploadingFileName = file.Name;
                uploadProgress = 0;

                var allowedExtensions = new[] { ".mp3", ".wav", ".ogg", ".flac" };
                var extension = Path.GetExtension(file.Name).ToLower();

                if (!allowedExtensions.Contains(extension))
                {
                    NotificationService.Notify(new NotificationMessage
                    {
                        Severity = NotificationSeverity.Error,
                        Summary = "오류",
                        Detail = $"지원하지 않는 파일 형식입니다: {extension}",
                        Duration = 4000
                    });
                    return;
                }

                // 파일 크기 제한 (100MB)
                var maxFileSize = 100 * 1024 * 1024;
                if (file.Size > maxFileSize)
                {
                    NotificationService.Notify(new NotificationMessage
                    {
                        Severity = NotificationSeverity.Error,
                        Summary = "오류",
                        Detail = "파일 크기는 100MB를 초과할 수 없습니다.",
                        Duration = 4000
                    });
                    return;
                }

                // 파일 업로드 시뮬레이션 (실제 구현 시 서버로 업로드)
                using var stream = file.OpenReadStream(maxFileSize);
                var content = new MultipartFormDataContent();
                var streamContent = new StreamContent(stream);
                streamContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(file.ContentType);
                content.Add(streamContent, "file", file.Name);

                // 업로드 진행률 시뮬레이션
                for (int i = 0; i <= 100; i += 10)
                {
                    uploadProgress = i;
                    StateHasChanged();
                    await Task.Delay(100);
                }

                // 미디어 정보 생성
                var newMedia = new WicsPlatform.Server.Models.wics.Medium
                {
                    FileName = file.Name,
                    FullPath = $"/media/{Guid.NewGuid()}{extension}",
                    DeleteYn = "N",
                    CreatedAt = DateTime.Now,
                    UpdatedAt = DateTime.Now
                };

                await WicsService.CreateMedium(newMedia);

                NotificationService.Notify(new NotificationMessage
                {
                    Severity = NotificationSeverity.Success,
                    Summary = "업로드 완료",
                    Detail = $"{file.Name} 파일이 성공적으로 업로드되었습니다.",
                    Duration = 4000
                });

                await LoadMedia();
            }
            catch (Exception ex)
            {
                NotificationService.Notify(new NotificationMessage
                {
                    Severity = NotificationSeverity.Error,
                    Summary = "업로드 실패",
                    Detail = $"파일 업로드 중 오류가 발생했습니다: {ex.Message}",
                    Duration = 4000
                });
            }
            finally
            {
                isUploading = false;
                uploadProgress = 0;
                uploadingFileName = "";
                fileInputKey = Guid.NewGuid().ToString();
                StateHasChanged();
            }
        }

        protected void OnDragEnter(DragEventArgs e)
        {
            isDraggingOver = true;
        }

        protected void OnDragLeave(DragEventArgs e)
        {
            isDraggingOver = false;
        }

        protected async Task OnDrop(DragEventArgs e)
        {
            isDraggingOver = false;
            // 브라우저의 파일 드롭은 InputFile 컴포넌트를 통해서만 처리 가능
        }

        protected async Task ClickFileInput()
        {
            await JSRuntime.InvokeVoidAsync("eval", "document.getElementById('fileInput').click()");
        }

        // 데이터 로드 메서드
        protected async Task LoadMedia()
        {
            try
            {
                isLoading = true;

                var query = new Radzen.Query
                {
                    Expand = "MapMediaGroups($expand=Group)",
                    Filter = "DeleteYn eq 'N' or DeleteYn eq null",
                    OrderBy = "CreatedAt desc"
                };

                var result = await WicsService.GetMedia(query);
                allMedia = result.Value.AsODataEnumerable();
                media = allMedia;

                ApplyFilters();
            }
            catch (Exception ex)
            {
                NotificationService.Notify(new NotificationMessage
                {
                    Severity = NotificationSeverity.Error,
                    Summary = "오류",
                    Detail = $"미디어 목록을 불러오는 중 오류가 발생했습니다: {ex.Message}",
                    Duration = 4000
                });
            }
            finally
            {
                isLoading = false;
            }
        }

        protected async Task LoadPlaylists()
        {
            try
            {
                isLoadingPlaylists = true;

                var query = new Radzen.Query
                {
                    Filter = "Type eq 1 and (DeleteYn eq 'N' or DeleteYn eq null)",
                    OrderBy = "CreatedAt desc"
                };

                var result = await WicsService.GetGroups(query);
                playlists = result.Value.AsODataEnumerable();
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

        protected async Task LoadMediaPlaylistMappings()
        {
            try
            {
                var query = new Radzen.Query
                {
                    Expand = "Group,Media"
                };

                var result = await WicsService.GetMapMediaGroups(query);
                mediaPlaylistMappings = result.Value.AsODataEnumerable();
            }
            catch (Exception ex)
            {
                NotificationService.Notify(new NotificationMessage
                {
                    Severity = NotificationSeverity.Error,
                    Summary = "오류",
                    Detail = $"매핑 정보를 불러오는 중 오류가 발생했습니다: {ex.Message}",
                    Duration = 4000
                });
            }
        }

        // JavaScript에서 호출되는 드롭 핸들러
        [JSInvokable]
        public async Task HandleMediaDropFromJS(int playlistId)
        {
            if (isAddingToPlaylist)
            {
                Console.WriteLine("이미 추가 처리 중입니다. 중복 호출 무시.");
                return;
            }

            var playlist = playlists.FirstOrDefault(p => p.Id == (ulong)playlistId);
            if (playlist != null && selectedMedia.Any())
            {
                await AddSelectedMediaToPlaylist(playlist);
                StateHasChanged();
            }
        }

        // 선택된 미디어를 플레이리스트에 추가
        protected async Task AddSelectedMediaToPlaylist(WicsPlatform.Server.Models.wics.Group playlist)
        {
            if (!selectedMedia.Any())
            {
                NotificationService.Notify(new NotificationMessage
                {
                    Severity = NotificationSeverity.Warning,
                    Summary = "선택 필요",
                    Detail = "추가할 미디어를 선택하세요.",
                    Duration = 3000
                });
                return;
            }

            if (isAddingToPlaylist)
            {
                Console.WriteLine("이미 처리 중입니다.");
                return;
            }

            try
            {
                isAddingToPlaylist = true;

                var mediaToAdd = selectedMedia.ToList();
                int successCount = 0;
                int failCount = 0;
                int alreadyExistsCount = 0;

                await LoadMediaPlaylistMappings();

                foreach (var mediaFile in mediaToAdd)
                {
                    try
                    {
                        var existingMapping = mediaPlaylistMappings
                            .FirstOrDefault(m => m.MediaId == mediaFile.Id && m.GroupId == playlist.Id);

                        if (existingMapping != null)
                        {
                            alreadyExistsCount++;
                            continue;
                        }

                        var mapMediaGroup = new WicsPlatform.Server.Models.wics.MapMediaGroup
                        {
                            MediaId = mediaFile.Id,
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
                }

                if (successCount > 0)
                {
                    NotificationService.Notify(new NotificationMessage
                    {
                        Severity = NotificationSeverity.Success,
                        Summary = "추가 완료",
                        Detail = $"{successCount}개의 미디어가 '{playlist.Name}' 플레이리스트에 추가되었습니다." +
                                (alreadyExistsCount > 0 ? $" ({alreadyExistsCount}개는 이미 플레이리스트에 속해있음)" : "") +
                                (failCount > 0 ? $" ({failCount}개 실패)" : ""),
                        Duration = 4000
                    });

                    await LoadMediaPlaylistMappings();
                    await LoadMedia();

                    selectedMedia.Clear();
                    StateHasChanged();
                }
                else if (alreadyExistsCount > 0)
                {
                    NotificationService.Notify(new NotificationMessage
                    {
                        Severity = NotificationSeverity.Info,
                        Summary = "알림",
                        Detail = $"선택한 미디어들은 이미 '{playlist.Name}' 플레이리스트에 속해있습니다.",
                        Duration = 4000
                    });
                }
                else
                {
                    NotificationService.Notify(new NotificationMessage
                    {
                        Severity = NotificationSeverity.Warning,
                        Summary = "추가 실패",
                        Detail = "미디어를 플레이리스트에 추가하는데 실패했습니다.",
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
                    Detail = $"미디어 추가 중 오류 발생: {ex.Message}",
                    Duration = 4000
                });
            }
            finally
            {
                isAddingToPlaylist = false;
            }
        }

        // 미디어 선택 토글
        protected void ToggleMediaSelection(WicsPlatform.Server.Models.wics.Medium mediaFile)
        {
            if (IsMediaSelected(mediaFile))
            {
                selectedMedia = selectedMedia.Where(m => m.Id != mediaFile.Id).ToList();
            }
            else
            {
                selectedMedia.Add(mediaFile);
            }
        }

        // 선택 초기화
        protected void ClearSelection()
        {
            selectedMedia.Clear();
        }

        // 미디어가 속한 플레이리스트 목록 반환
        protected IEnumerable<string> GetMediaPlaylists(ulong mediaId)
        {
            return mediaPlaylistMappings
                .Where(m => m.MediaId == mediaId && m.Group != null)
                .Select(m => m.Group.Name)
                .Distinct();
        }

        // 플레이리스트에 속한 미디어 수 반환
        protected int GetMediaCountInPlaylist(ulong playlistId)
        {
            return mediaPlaylistMappings
                .Where(m => m.GroupId == playlistId)
                .Select(m => m.MediaId)
                .Distinct()
                .Count();
        }

        // 플레이리스트에 속한 미디어 목록 반환 (동기)
        protected IEnumerable<WicsPlatform.Server.Models.wics.Medium> GetMediaInPlaylistSync(ulong playlistId)
        {
            var mediaIds = mediaPlaylistMappings
                .Where(m => m.GroupId == playlistId)
                .Select(m => m.MediaId)
                .Distinct();

            return allMedia.Where(m => mediaIds.Contains(m.Id));
        }

        // 미디어 선택 상태 확인
        protected bool IsMediaSelected(WicsPlatform.Server.Models.wics.Medium mediaFile)
        {
            return selectedMedia.Any(m => m.Id == mediaFile.Id);
        }

        // 개별 미디어 선택 변경
        protected void MediaSelectionChanged(bool selected, WicsPlatform.Server.Models.wics.Medium mediaFile)
        {
            if (selected)
            {
                if (!IsMediaSelected(mediaFile))
                {
                    selectedMedia.Add(mediaFile);
                }
            }
            else
            {
                selectedMedia = selectedMedia.Where(m => m.Id != mediaFile.Id).ToList();
            }

            StateHasChanged();
        }

        // 플레이리스트 확장/축소 토글
        protected void TogglePlaylistExpansion(ulong playlistId)
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

        // 필터 적용
        protected void ApplyFilters()
        {
            var filteredMedia = allMedia;

            if (!string.IsNullOrWhiteSpace(mediaNameFilter))
            {
                filteredMedia = filteredMedia.Where(m => m.FileName.Contains(mediaNameFilter, StringComparison.OrdinalIgnoreCase));
            }

            if (playlistFilter.HasValue)
            {
                var mediaIdsInPlaylist = mediaPlaylistMappings
                    .Where(m => m.GroupId == playlistFilter.Value)
                    .Select(m => m.MediaId)
                    .Distinct();

                filteredMedia = filteredMedia.Where(m => mediaIdsInPlaylist.Contains(m.Id));
            }

            media = filteredMedia;
            mediaGrid?.Reload();
        }

        // 필터 초기화
        protected void ResetFilters()
        {
            mediaNameFilter = "";
            playlistFilter = null;
            ApplyFilters();
        }

        // 미디어 재생
        protected async Task PlayMedia(WicsPlatform.Server.Models.wics.Medium mediaFile)
        {
            if (_jsPlayer == null)
            {
                return;
            }

            var url = mediaFile.FullPath;
            if (!string.IsNullOrEmpty(url) && !url.StartsWith("/Uploads/", StringComparison.OrdinalIgnoreCase))
            {
                url = $"/Uploads/{Path.GetFileName(url)}";
            }

            await _jsPlayer.InvokeVoidAsync("playMedia", url);
        }

        // 다이얼로그 메서드들
        protected async Task OpenAddPlaylistDialog()
        {
            var result = await DialogService.OpenAsync<AddPlaylistDialog>("플레이리스트 추가",
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

        protected async Task OpenEditPlaylistDialog(WicsPlatform.Server.Models.wics.Group playlist)
        {
            // 플레이리스트 편집 다이얼로그 구현
            NotificationService.Notify(new NotificationMessage
            {
                Severity = NotificationSeverity.Info,
                Summary = "편집",
                Detail = $"{playlist.Name} 플레이리스트 편집 기능은 준비 중입니다.",
                Duration = 3000
            });
        }

        protected async Task OpenDeletePlaylistDialog(WicsPlatform.Server.Models.wics.Group playlist)
        {
            var mediaCount = GetMediaCountInPlaylist(playlist.Id);

            string message;
            if (mediaCount > 0)
            {
                message = $"'{playlist.Name}' 플레이리스트에는 {mediaCount}개의 미디어가 포함되어 있습니다.\n그래도 삭제하시겠습니까?";
            }
            else
            {
                message = $"'{playlist.Name}' 플레이리스트를 삭제하시겠습니까?";
            }

            var result = await DialogService.Confirm(message, "플레이리스트 삭제",
                new ConfirmOptions() { OkButtonText = "삭제", CancelButtonText = "취소" });

            if (result == true)
            {
                await DeletePlaylist(playlist);
            }
        }

        protected async Task DeletePlaylist(WicsPlatform.Server.Models.wics.Group playlist)
        {
            try
            {
                var updateData = new
                {
                    DeleteYn = "Y",
                    UpdatedAt = DateTime.Now
                };

                var response = await Http.PatchAsJsonAsync($"odata/wics/Groups(Id={playlist.Id})", updateData);

                if (response.IsSuccessStatusCode)
                {
                    NotificationService.Notify(new NotificationMessage
                    {
                        Severity = NotificationSeverity.Success,
                        Summary = "삭제 완료",
                        Detail = $"'{playlist.Name}' 플레이리스트가 삭제되었습니다.",
                        Duration = 4000
                    });

                    await LoadPlaylists();
                    await LoadMedia();
                }
                else
                {
                    NotificationService.Notify(new NotificationMessage
                    {
                        Severity = NotificationSeverity.Error,
                        Summary = "삭제 실패",
                        Detail = "플레이리스트 삭제 중 오류가 발생했습니다.",
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
                    Detail = $"플레이리스트 삭제 중 오류가 발생했습니다: {ex.Message}",
                    Duration = 4000
                });
            }
        }

        protected async Task OpenRemoveMediaFromPlaylistDialog(WicsPlatform.Server.Models.wics.Medium mediaFile, WicsPlatform.Server.Models.wics.Group playlist)
        {
            var result = await DialogService.Confirm(
                $"'{mediaFile.FileName}' 파일을 '{playlist.Name}' 플레이리스트에서 제거하시겠습니까?",
                "미디어 제거",
                new ConfirmOptions()
                {
                    OkButtonText = "제거",
                    CancelButtonText = "취소"
                });

            if (result == true)
            {
                await RemoveMediaFromPlaylist(mediaFile, playlist);
            }
        }

        protected async Task RemoveMediaFromPlaylist(WicsPlatform.Server.Models.wics.Medium mediaFile, WicsPlatform.Server.Models.wics.Group playlist)
        {
            try
            {
                var mapping = mediaPlaylistMappings
                    .FirstOrDefault(m => m.MediaId == mediaFile.Id && m.GroupId == playlist.Id);

                if (mapping != null)
                {
                    var response = await Http.DeleteAsync($"odata/wics/MapMediaGroups({mapping.Id})");

                    if (response.IsSuccessStatusCode)
                    {
                        NotificationService.Notify(new NotificationMessage
                        {
                            Severity = NotificationSeverity.Success,
                            Summary = "삭제 완료",
                            Detail = $"'{mediaFile.FileName}' 파일이 '{playlist.Name}' 플레이리스트에서 제거되었습니다.",
                            Duration = 3000
                        });

                        await LoadMediaPlaylistMappings();
                        await LoadMedia();

                        if (expandedPlaylists.Contains(playlist.Id))
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
                            Detail = "미디어를 플레이리스트에서 제거하는 중 오류가 발생했습니다.",
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
                    Detail = $"미디어 제거 중 오류 발생: {ex.Message}",
                    Duration = 4000
                });
            }
        }

        protected async Task OpenEditMediaDialog(WicsPlatform.Server.Models.wics.Medium mediaFile)
        {
            // 미디어 편집 다이얼로그 구현
            NotificationService.Notify(new NotificationMessage
            {
                Severity = NotificationSeverity.Info,
                Summary = "편집",
                Detail = $"{mediaFile.FileName} 파일 편집 기능은 준비 중입니다.",
                Duration = 3000
            });
        }

        protected async Task OpenDeleteMediaDialog(WicsPlatform.Server.Models.wics.Medium mediaFile)
        {
            var result = await DialogService.Confirm($"'{mediaFile.FileName}' 파일을 삭제하시겠습니까?", "미디어 삭제",
                new ConfirmOptions() { OkButtonText = "삭제", CancelButtonText = "취소" });

            if (result == true)
            {
                await DeleteMedia(mediaFile);
            }
        }

        protected async Task DeleteMedia(WicsPlatform.Server.Models.wics.Medium mediaFile)
        {
            try
            {
                var updateData = new
                {
                    DeleteYn = "Y",
                    UpdatedAt = DateTime.Now
                };

                var response = await Http.PatchAsJsonAsync($"odata/wics/Media(Id={mediaFile.Id})", updateData);

                if (response.IsSuccessStatusCode)
                {
                    NotificationService.Notify(new NotificationMessage
                    {
                        Severity = NotificationSeverity.Success,
                        Summary = "삭제 완료",
                        Detail = $"'{mediaFile.FileName}' 파일이 삭제되었습니다.",
                        Duration = 4000
                    });

                    await LoadMedia();
                }
                else
                {
                    NotificationService.Notify(new NotificationMessage
                    {
                        Severity = NotificationSeverity.Error,
                        Summary = "삭제 실패",
                        Detail = "미디어 삭제 중 오류가 발생했습니다.",
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
                    Detail = $"미디어 삭제 중 오류가 발생했습니다: {ex.Message}",
                    Duration = 4000
                });
            }
        }

        // Dispose 메서드
        public async ValueTask DisposeAsync()
        {
            if (_jsPlayer != null)
            {
                await _jsPlayer.DisposeAsync();
            }
            if (_jsModule != null)
            {
                await _jsModule.DisposeAsync();
            }
            _dotNetRef?.Dispose();
        }
    }
}