using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Radzen;
using System.ComponentModel.DataAnnotations;
using System.Net.Http.Json;

namespace WicsPlatform.Client.Dialogs;

public partial class AddPlaylistDialog
{
    [Inject] protected IJSRuntime JSRuntime { get; set; }
    [Inject] protected DialogService DialogService { get; set; }
    [Inject] protected NotificationService NotificationService { get; set; }
    [Inject] protected HttpClient Http { get; set; }

    protected GroupFormModel model = new GroupFormModel();
    protected string error;
    protected bool errorVisible;
    protected bool isProcessing = false;

    protected async Task FormSubmit()
    {
        try
        {
            isProcessing = true;
            errorVisible = false;

            if (string.IsNullOrWhiteSpace(model.Name) || string.IsNullOrWhiteSpace(model.Description))
            {
                errorVisible = true;
                error = "모든 필수 항목을 입력해주세요.";
                isProcessing = false;
                return;
            }

            var group = new
            {
                Type = (byte)1,
                Name = model.Name,
                Description = model.Description,
                DeleteYn = "N",
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now
            };

            // 그룹 생성 API 호출
            var response = await Http.PostAsJsonAsync("odata/wics/Groups", group);

            if (response.IsSuccessStatusCode)
            {
                NotificationService.Notify(new NotificationMessage
                {
                    Severity = NotificationSeverity.Success,
                    Summary = "그룹 생성 성공",
                    Detail = $"'{model.Name}' 그룹이 성공적으로 생성되었습니다.",
                    Duration = 4000
                });

                DialogService.Close(true);
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                errorVisible = true;
                error = $"그룹 생성 중 오류가 발생했습니다: {errorContent}";
            }
        }
        catch (Exception ex)
        {
            errorVisible = true;
            error = $"그룹 생성 중 오류가 발생했습니다: {ex.Message}";
        }
        finally
        {
            isProcessing = false;
        }
    }

    protected async Task CancelClick()
    {
        await Task.Delay(100);
        DialogService.Close(null);
    }
}

// 그룹 생성 요청 모델
public class CreateGroupRequest
{
    public byte Type { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

// 그룹 생성/수정 폼 모델
public class GroupFormModel
{
    [Required(ErrorMessage = "그룹 이름은 필수입니다.")]
    [StringLength(100, ErrorMessage = "그룹 이름은 100자 이내로 입력해주세요.")]
    public string Name { get; set; }

    [Required(ErrorMessage = "설명은 필수입니다.")]
    [StringLength(500, ErrorMessage = "설명은 500자 이내로 입력해주세요.")]
    public string Description { get; set; }
}
