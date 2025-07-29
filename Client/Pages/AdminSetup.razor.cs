using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Radzen;

namespace WicsPlatform.Client.Pages
{
    public partial class AdminSetup
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
        protected HttpClient HttpClient { get; set; }

        protected AdminSetupModel model = new AdminSetupModel();
        protected string error;
        protected string info;
        protected bool errorVisible;
        protected bool infoVisible;
        protected bool isLoading;

        protected override async Task OnInitializedAsync()
        {
            // Check if admin user already exists and redirect if it does
            try
            {
                var response = await HttpClient.GetFromJsonAsync<AdminExistsResponse>("api/SuperUser/exists");
                if (response.Exists)
                {
                    // Admin user already exists, redirect to login
                    NavigationManager.NavigateTo("login");
                }
            }
            catch (Exception ex)
            {
                // If there's an error, we'll still show the setup page
                errorVisible = true;
                error = $"관리자 확인 중 오류가 발생했습니다: {ex.Message}. 계속 진행하거나 나중에 다시 시도해주세요.";
            }
        }

        protected async Task HandleSetup()
        {
            try
            {
                errorVisible = false;
                infoVisible = false;
                isLoading = true;

                var response = await HttpClient.PostAsJsonAsync("api/SuperUser/setup", model);

                if (response.IsSuccessStatusCode)
                {
                    var successResponse = await response.Content.ReadFromJsonAsync<SuccessResponse>();

                    infoVisible = true;
                    info = "관리자 계정이 성공적으로 생성되었습니다. 로그인 페이지로 이동합니다...";

                    await Task.Delay(2000); // Wait 2 seconds before redirecting
                    NavigationManager.NavigateTo("login");
                }
                else
                {
                    var errorResponse = await response.Content.ReadFromJsonAsync<ErrorResponse>();
                    errorVisible = true;
                    error = errorResponse?.Error ?? "관리자 계정 생성 중 오류가 발생했습니다.";
                }
            }
            catch (Exception ex)
            {
                errorVisible = true;
                error = $"예기치 않은 오류가 발생했습니다: {ex.Message}";
            }
            finally
            {
                isLoading = false;
            }
        }
    }

    public class AdminSetupModel
    {
        public string Email { get; set; }
        public string Password { get; set; }
        public string ConfirmPassword { get; set; }
    }

    public class AdminExistsResponse
    {
        public bool Exists { get; set; }
    }

    public class SuccessResponse
    {
        public bool Success { get; set; }
    }

    public class ErrorResponse
    {
        public string Error { get; set; }
    }
}
