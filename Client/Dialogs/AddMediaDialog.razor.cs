using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Radzen;
using System.Net.Http;
using System.Net.Http.Json;
using System.IO;

namespace WicsPlatform.Client.Dialogs
{
    public partial class AddMediaDialog
    {
        [Inject] protected DialogService DialogService { get; set; }
        [Inject] protected NotificationService NotificationService { get; set; }
        [Inject] protected wicsService WicsService { get; set; }
        [Inject] protected HttpClient Http { get; set; }
        [Inject] protected IJSRuntime JSRuntime { get; set; }

        // 파일 업로드 관련 필드
        protected bool isDraggingOver = false;
        protected bool isUploading = false;
        protected int uploadProgress = 0;
        protected string uploadingFileName = "";
        protected string fileInputKey = Guid.NewGuid().ToString();
        protected List<string> uploadedFiles = new List<string>();

        // 파일 선택 버튼 클릭
        protected async Task ClickFileInput()
        {
            await JSRuntime.InvokeVoidAsync("eval", "document.getElementById('mediaFileInput').click()");
        }

        // 파일 선택 시
        protected async Task OnInputFileChange(InputFileChangeEventArgs e)
        {
            var files = e.GetMultipleFiles();
            foreach (var file in files)
            {
                await UploadFile(file);
            }
            
            // 모든 파일 업로드 완료 후 InputFile 리셋
            fileInputKey = Guid.NewGuid().ToString();
            StateHasChanged();
        }

        // 파일 업로드 처리
        protected async Task UploadFile(IBrowserFile file)
        {
            try
            {
                isUploading = true;
                uploadingFileName = file.Name;
                uploadProgress = 0;
                StateHasChanged();

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

                // 파일 업로드 처리
                using var stream = file.OpenReadStream(maxFileSize);
                var content = new MultipartFormDataContent();
                var streamContent = new StreamContent(stream);
                streamContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(file.ContentType);
                content.Add(streamContent, "file", file.Name);

                // 진행률 시작
                uploadProgress = 30;
                StateHasChanged();

                // 실제 서버에 파일 업로드
                var uploadResponse = await Http.PostAsync("upload/single", content);
                
                if (!uploadResponse.IsSuccessStatusCode)
                {
                    throw new Exception($"파일 업로드 실패: {uploadResponse.StatusCode}");
                }

                uploadProgress = 60;
                StateHasChanged();

                var uploadResult = await uploadResponse.Content.ReadFromJsonAsync<UploadResult>();
                
                if (uploadResult == null || !uploadResult.Success)
                {
                    throw new Exception("파일 업로드 응답이 올바르지 않습니다.");
                }

                uploadProgress = 80;
                StateHasChanged();

                // 업로드된 파일 경로 추출 (예: /Uploads/xxx.mp3)
                var serverFilePath = new Uri(uploadResult.FileUrl).AbsolutePath;

                // 미디어 정보 생성 (실제 업로드된 파일 경로 사용)
                var newMedia = new WicsPlatform.Server.Models.wics.Medium
                {
                    FileName = file.Name,
                    FullPath = serverFilePath,
                    DeleteYn = "N",
                    CreatedAt = DateTime.Now,
                    UpdatedAt = DateTime.Now
                };

                await WicsService.CreateMedium(newMedia);
                
                uploadProgress = 100;
                StateHasChanged();

                // 업로드된 파일 목록에 추가
                uploadedFiles.Add(file.Name);

                NotificationService.Notify(new NotificationMessage
                {
                    Severity = NotificationSeverity.Success,
                    Summary = "업로드 완료",
                    Detail = $"{file.Name} 파일이 성공적으로 업로드되었습니다.",
                    Duration = 3000
                });

                StateHasChanged();
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
                StateHasChanged();
            }
        }

        // 드래그 이벤트 핸들러
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

        // 다이얼로그 닫기
        protected void CloseDialog()
        {
            DialogService.Close(uploadedFiles.Any());
        }
    }

    // 업로드 응답 모델
    public class UploadResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public string FileUrl { get; set; }
    }
}
