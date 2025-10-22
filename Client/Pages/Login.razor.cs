using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.JSInterop;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Radzen;
using Radzen.Blazor;

namespace WicsPlatform.Client.Pages
{
    public partial class Login
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

        [Inject]
        protected SecurityService Security { get; set; }

        public class AdminExistsResponse
        {
            public bool Exists { get; set; }
        }

        protected LoginModel loginModel = new LoginModel();
        protected bool isLoading;
        protected string error;
        protected string info;
        protected bool errorVisible;
        protected bool infoVisible;

        protected override async Task OnInitializedAsync()
        {
            // Check if any query parameters exist for error or redirectUrl
            var uri = new Uri(NavigationManager.Uri);
            var query = System.Web.HttpUtility.ParseQueryString(uri.Query);

            if (query.Get("error") != null)
            {
                errorVisible = true;
                error = query.Get("error");
            }

            if (query.Get("redirectUrl") != null)
            {
                loginModel.redirectUrl = query.Get("redirectUrl");
            }

            // Check if admin user exists
            try
            {
                var response = await HttpClient.GetFromJsonAsync<AdminExistsResponse>("api/SuperUser/exists");
                if (!response.Exists)
                {
                    // No admin user exists, redirect to admin setup page
                    NavigationManager.NavigateTo("admin-setup");
                    return;
                }
            }
            catch (Exception ex)
            {
                // If there's an error checking, we'll still show the login page
                // but also show a message
                infoVisible = true;
                info = $"시스템 설정을 확인할 수 없습니다: {ex.Message}. 첫 실행이라면 관리자 계정 설정이 필요할 수 있습니다.";
            }
        }

        private record LoginApiRequest(string userName, string password, string redirectUrl);
        private record LoginApiResponse(bool success, string redirectUrl, string error);

        protected async Task HandleLogin()
        {
            try
            {
                errorVisible = false;
                infoVisible = false;
                isLoading = true;

                var body = new LoginApiRequest(loginModel.UserName, loginModel.Password, loginModel.redirectUrl);
                var resp = await HttpClient.PostAsJsonAsync("Account/LoginApi", body);
                var data = await resp.Content.ReadFromJsonAsync<LoginApiResponse>();

                if (resp.IsSuccessStatusCode && data?.success == true)
                {
                    // 쿠키가 커밋되었음을 보장하기 위해 짧은 지연 후 이동
                    await Task.Delay(50);
                    NavigationManager.NavigateTo(string.IsNullOrWhiteSpace(data.redirectUrl) ? "/" : data.redirectUrl, forceLoad: true);
                    return;
                }

                errorVisible = true;
                error = data?.error ?? ($"Login failed: {resp.StatusCode}");
            }
            catch (Exception ex)
            {
                errorVisible = true;
                error = ex.Message;
            }
            finally
            {
                isLoading = false;
            }
        }
    }

    public class LoginModel
    {
        public string UserName { get; set; }
        public string Password { get; set; }
        public string redirectUrl { get; set; }
    }
}