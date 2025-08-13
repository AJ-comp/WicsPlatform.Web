using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Radzen;
using WicsPlatform.Client.Services;
using WicsPlatform.Client.Services.Interfaces;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.Services.AddRadzenComponents();
builder.Services.AddRadzenCookieThemeService(options =>
{
    options.Name = "WicsPlatformTheme";
    options.Duration = TimeSpan.FromDays(365);
});
builder.Services.AddTransient(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddScoped<WicsPlatform.Client.wicsService>();
builder.Services.AddScoped<BroadcastWebSocketService>();
builder.Services.AddScoped<MediaStreamingService>();
builder.Services.AddScoped<BroadcastRecordingService>();
builder.Services.AddScoped<BroadcastLoggingService>(); // 추가
builder.Services.AddScoped<IBroadcastDataService, BroadcastDataService>();

builder.Services.AddAuthorizationCore();
builder.Services.AddHttpClient("WicsPlatform.Server", client => client.BaseAddress = new Uri(builder.HostEnvironment.BaseAddress));
builder.Services.AddTransient(sp => sp.GetRequiredService<IHttpClientFactory>().CreateClient("WicsPlatform.Server"));
builder.Services.AddScoped<WicsPlatform.Client.SecurityService>();
builder.Services.AddScoped<AuthenticationStateProvider, WicsPlatform.Client.ApplicationAuthenticationStateProvider>();
var host = builder.Build();
await host.RunAsync();