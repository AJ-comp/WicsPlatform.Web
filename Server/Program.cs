using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.OData;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.OData.ModelBuilder;
using Radzen;
using WicsPlatform.Audio;
using WicsPlatform.Server.Components;
using WicsPlatform.Server.Contracts;
using WicsPlatform.Server.Data;
using WicsPlatform.Server.Models;
using WicsPlatform.Server.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http; // Added for CookiePolicyOptions
using Microsoft.AspNetCore.HttpOverrides; // add forwarded headers
using Microsoft.AspNetCore.Http.Features; // for FormOptions

static void RegisterDBContext(WebApplicationBuilder builder)
{
    // Radzen Blazor Studio에서 스캐폴딩 시 이걸로 해야 함
    builder.Services.AddDbContext<wicsContext>(options =>
    {
        options.UseMySql(builder.Configuration.GetConnectionString("wicsConnection"), ServerVersion.AutoDetect(builder.Configuration.GetConnectionString("wicsConnection")));
    });
    builder.Services.AddDbContext<ApplicationIdentityDbContext>(options =>
    {
        options.UseMySql(builder.Configuration.GetConnectionString("wicsConnection"), ServerVersion.AutoDetect(builder.Configuration.GetConnectionString("wicsConnection")));
    });
    builder.Services.AddIdentity<ApplicationUser, ApplicationRole>().AddEntityFrameworkStores<ApplicationIdentityDbContext>().AddDefaultTokenProviders();
}

var builder = WebApplication.CreateBuilder(args);
// Add services to the container.
builder.Services.AddRazorComponents().AddInteractiveWebAssemblyComponents();
builder.Services.AddControllers();
builder.Services.AddRadzenComponents();
builder.Services.AddRadzenCookieThemeService(options =>
{
    options.Name = "WicsPlatformTheme";
    options.Duration = TimeSpan.FromDays(365);
});

// Allow large multipart uploads (e.g., media files)
builder.Services.Configure<FormOptions>(o =>
{
    o.MultipartBodyLengthLimit = 200_000_000; // 200 MB
});

// 데이터 보호 키 저장 경로를 설정 파일에서 우선 읽고(IIS 권한 문제 대비)
string? dataProtectionKeysPath = null;
var configuredKeysPath = builder.Configuration["DataProtection:KeysPath"]; // ex) "keys" -> ContentRoot/keys
if (!string.IsNullOrWhiteSpace(configuredKeysPath))
{
    try
    {
        var resolved = Path.IsPathRooted(configuredKeysPath)
            ? configuredKeysPath
            : Path.Combine(builder.Environment.ContentRootPath, configuredKeysPath);
        Directory.CreateDirectory(resolved);
        dataProtectionKeysPath = resolved;
    }
    catch
    {
        // 설정된 경로가 잘못됐거나 권한이 없으면 아래 자동 선택 로직으로 폴백
        dataProtectionKeysPath = null;
    }
}

if (string.IsNullOrEmpty(dataProtectionKeysPath))
{
    try
    {
        // 1순위: 애플리케이션 폴더 내부 (배포 경로) - 권한이 있는 경우에만
        var candidate1 = Path.Combine(builder.Environment.ContentRootPath, "keys");
        Directory.CreateDirectory(candidate1);
        dataProtectionKeysPath = candidate1;
    }
    catch
    {
        try
        {
            // 2순위: 사용자 프로필(LocalApplicationData) - AppPool에서 Load User Profile이 켜져 있어야 함
            var candidate2 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WicsPlatform", "keys");
            Directory.CreateDirectory(candidate2);
            dataProtectionKeysPath = candidate2;
        }
        catch
        {
            try
            {
                // 3순위: 공용 ProgramData - 서버 정책에 따라 권한 필요
                var candidate3 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "WicsPlatform", "keys");
                Directory.CreateDirectory(candidate3);
                dataProtectionKeysPath = candidate3;
            }
            catch
            {
                // 모든 경로 실패 시 영속화 없이 진행(로그인 루프 재발 가능) -> IIS 권한 설정 필요
                dataProtectionKeysPath = null;
            }
        }
    }
}

var dataProtectionAppName = builder.Configuration["DataProtection:ApplicationName"] ?? "WicsPlatform";
var dp = builder.Services.AddDataProtection().SetApplicationName(dataProtectionAppName);
if (!string.IsNullOrEmpty(dataProtectionKeysPath))
{
    dp.PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeysPath));
}

builder.Services.AddHttpClient();
builder.Services.AddScoped<WicsPlatform.Server.wicsService>();
builder.Services.AddSingleton<IAudioMixingService, AudioMixingService>();
builder.Services.AddSingleton<IUdpBroadcastService, UdpBroadcastService>();
builder.Services.AddSingleton<ITtsBroadcastService, TtsBroadcastService>();
builder.Services.AddSingleton<IMediaBroadcastService, MediaBroadcastService>();
builder.Services.AddSingleton<IBroadcastPreparationService, BroadcastPreparationService>();
builder.Services.AddSingleton<IScheduleExecutionService, ScheduleExecutionService>();
builder.Services.AddHostedService<ScheduleScannerService>();
RegisterDBContext(builder);
builder.Services.AddControllers().AddOData(opt =>
{
    var oDataBuilderwics = new ODataConventionModelBuilder();
    oDataBuilderwics.EntitySet<WicsPlatform.Server.Models.wics.Broadcast>("Broadcasts");
    oDataBuilderwics.EntitySet<WicsPlatform.Server.Models.wics.Channel>("Channels");
    oDataBuilderwics.EntitySet<WicsPlatform.Server.Models.wics.Group>("Groups");
    oDataBuilderwics.EntitySet<WicsPlatform.Server.Models.wics.MapChannelGroup>("MapChannelGroups");
    oDataBuilderwics.EntitySet<WicsPlatform.Server.Models.wics.MapChannelMedium>("MapChannelMedia");
    oDataBuilderwics.EntitySet<WicsPlatform.Server.Models.wics.MapChannelSpeaker>("MapChannelSpeakers");
    oDataBuilderwics.EntitySet<WicsPlatform.Server.Models.wics.MapChannelTt>("MapChannelTts");
    oDataBuilderwics.EntitySet<WicsPlatform.Server.Models.wics.MapMediaGroup>("MapMediaGroups");
    oDataBuilderwics.EntitySet<WicsPlatform.Server.Models.wics.MapSpeakerGroup>("MapSpeakerGroups");
    oDataBuilderwics.EntitySet<WicsPlatform.Server.Models.wics.Medium>("Media");
    oDataBuilderwics.EntitySet<WicsPlatform.Server.Models.wics.Mic>("Mics");
    oDataBuilderwics.EntitySet<WicsPlatform.Server.Models.wics.Schedule>("Schedules");
    oDataBuilderwics.EntitySet<WicsPlatform.Server.Models.wics.SchedulePlay>("SchedulePlays");
    oDataBuilderwics.EntitySet<WicsPlatform.Server.Models.wics.Speaker>("Speakers");
    oDataBuilderwics.EntitySet<WicsPlatform.Server.Models.wics.SpeakerOwnershipState>("SpeakerOwnershipStates").EntityType.HasKey(entity => new { entity.SpeakerId, entity.ChannelId });
    oDataBuilderwics.EntitySet<WicsPlatform.Server.Models.wics.Tt>("Tts");
    opt.AddRouteComponents("odata/wics", oDataBuilderwics.GetEdmModel()).Count().Filter().OrderBy().Expand().Select().SetMaxTop(null).TimeZone = TimeZoneInfo.Utc;
});
builder.Services.AddScoped<WicsPlatform.Client.wicsService>();
builder.Services.AddHttpClient("WicsPlatform.Server").ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { UseCookies = false }).AddHeaderPropagation(o => o.Headers.Add("Cookie"));
builder.Services.AddHeaderPropagation(o => o.Headers.Add("Cookie"));
builder.Services.AddAuthorization();
builder.Services.AddScoped<WicsPlatform.Client.SecurityService>();

// Client 서비스 등록 (Server-Side Rendering을 위해 필요)
builder.Services.AddScoped<WicsPlatform.Client.Services.BroadcastWebSocketService>();
builder.Services.AddScoped<WicsPlatform.Client.Services.BroadcastRecordingService>();
builder.Services.AddScoped<WicsPlatform.Client.Services.BroadcastLoggingService>();
builder.Services.AddSingleton<OpusCodec>(provider => new OpusCodec(48000, 1, 32000));
builder.Services.AddScoped<WicsPlatform.Client.Services.Interfaces.IBroadcastDataService, WicsPlatform.Client.Services.BroadcastDataService>();
builder.Services.AddControllers().AddOData(o =>
{
    var oDataBuilder = new ODataConventionModelBuilder();
    oDataBuilder.EntitySet<ApplicationUser>("ApplicationUsers");
    var usersType = oDataBuilder.StructuralTypes.First(x => x.ClrType == typeof(ApplicationUser));
    usersType.AddProperty(typeof(ApplicationUser).GetProperty(nameof(ApplicationUser.Password)));
    usersType.AddProperty(typeof(ApplicationUser).GetProperty(nameof(ApplicationUser.ConfirmPassword)));
    oDataBuilder.EntitySet<ApplicationRole>("ApplicationRoles");
    o.AddRouteComponents("odata/Identity", oDataBuilder.GetEdmModel()).Count().Filter().OrderBy().Expand().Select().SetMaxTop(null).TimeZone = TimeZoneInfo.Utc;
});
builder.Services.AddScoped<AuthenticationStateProvider, WicsPlatform.Client.ApplicationAuthenticationStateProvider>();
// Add CORS policy to allow the client to access the server API from browser
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyMethod().AllowAnyHeader().SetIsOriginAllowed(_ => true) // 또는 .WithOrigins("https://localhost:50553")
        .AllowCredentials(); // 이 부분이 중요!
    });
});
// Identity 쿠키 명시적 구성 (경로 및 이름 등)
var relaxCookies = builder.Configuration.GetValue<bool>("AuthCookies:RelaxForIpClients");
builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login"; // 챌린지 시 이동 경로
    options.LogoutPath = "/Account/Logout";
    options.AccessDeniedPath = "/Login";
    options.Cookie.Name = ".WicsPlatform.Identity";
    // HTTPS 사이트에서 브라우저 정책과 호환되도록 설정
    // 기본(보안) 정책
    options.Cookie.SameSite = SameSiteMode.None;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    // 필요시(내부망 IP/인증서 미신뢰 테스트 환경) 완화 스위치
    if (relaxCookies)
    {
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.None; // allow over HTTP if needed
    }
    options.SlidingExpiration = true;
});

// 전역 쿠키 정책 (IIS/프록시 환경에서 HTTPS 시 Secure 강제)
builder.Services.Configure<CookiePolicyOptions>(o =>
{
    if (relaxCookies)
    {
        o.MinimumSameSitePolicy = SameSiteMode.Lax;
        o.Secure = CookieSecurePolicy.None;
    }
    else
    {
        o.MinimumSameSitePolicy = SameSiteMode.None;
        o.Secure = CookieSecurePolicy.Always;
    }
});

builder.Services.AddDbContext<WicsPlatform.Server.Data.wicsContext>(options =>
{
    options.UseMySql(builder.Configuration.GetConnectionString("wicsConnection"), ServerVersion.AutoDetect(builder.Configuration.GetConnectionString("wicsConnection")));
});
var app = builder.Build();

// Ensure essential directories exist on startup (deploy-time creation)
static void EnsureDir(string? path, string contentRoot)
{
    if (string.IsNullOrWhiteSpace(path)) return;
    var full = Path.IsPathRooted(path) ? path : Path.Combine(contentRoot, path);
    try { Directory.CreateDirectory(full); } catch { }
}
try
{
    // wwwroot/Uploads
    EnsureDir(Path.Combine(app.Environment.WebRootPath, "Uploads"), app.Environment.ContentRootPath);
    // DataProtection keys (already created above, but ensure again)
    EnsureDir(builder.Configuration["DataProtection:KeysPath"], app.Environment.ContentRootPath);
    // Auth logging folder
    EnsureDir(builder.Configuration["AuthLogging:LogPath"], app.Environment.ContentRootPath);
    // Upload diagnostics folder
    EnsureDir(builder.Configuration["UploadLogging:LogPath"], app.Environment.ContentRootPath);
}
catch { }

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // app.UseHsts();
}

// Add forwarded headers support (IIS/프록시 환경에서 HTTPS 스킴 보존)
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedFor,
});

// Debug cookie tracer (optional)
var traceCookies = builder.Configuration.GetValue<bool>("AuthLogging:TraceCookies");
if (traceCookies)
{
    var logPath = builder.Configuration["AuthLogging:LogPath"];
    if (!string.IsNullOrWhiteSpace(logPath))
    {
        if (!Path.IsPathRooted(logPath)) logPath = Path.Combine(builder.Environment.ContentRootPath, logPath);
        Directory.CreateDirectory(logPath);
    }
    string LogFile() => Path.Combine(string.IsNullOrWhiteSpace(logPath) ? builder.Environment.ContentRootPath : logPath, $"cookie-trace-{DateTime.Now:yyyyMMdd}.log");

    app.Use(async (ctx, next) =>
    {
        try
        {
            var p = ctx.Request.Path.Value ?? string.Empty;
            if (p.StartsWith("/Account", StringComparison.OrdinalIgnoreCase) || p.StartsWith("/manage-broad-cast", StringComparison.OrdinalIgnoreCase) || p.Equals("/", StringComparison.Ordinal))
            {
                var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "-";
                var ua = ctx.Request.Headers["User-Agent"].ToString();
                var cookie = ctx.Request.Headers["Cookie"].ToString();
                await File.AppendAllTextAsync(LogFile(), $"[{DateTime.Now:HH:mm:ss.fff}] -> PATH={p} IP={ip} COOKIE={cookie}\n");
                ctx.Response.OnStarting(async () =>
                {
                    try
                    {
                        var setCookie = ctx.Response.Headers["Set-Cookie"].ToString();
                        if (!string.IsNullOrEmpty(setCookie))
                        {
                            await File.AppendAllTextAsync(LogFile(), $"[{DateTime.Now:HH:mm:ss.fff}] <- PATH={p} SET-COOKIE={setCookie}\n");
                        }
                    }
                    catch { }
                });
            }
        }
        catch { }
        await next();
    });
}

// app.UseHttpsRedirection();  // 주석 처리
// 정적 파일 제공 설정 - 순서 중요!
// 1. 기본 wwwroot 폴더
app.UseStaticFiles();
// 2. Uploads 폴더를 위한 추가 설정
var provider = new FileExtensionContentTypeProvider();
provider.Mappings[".mp3"] = "audio/mpeg";
provider.Mappings[".wav"] = "audio/wav";
provider.Mappings[".ogg"] = "audio/ogg";
provider.Mappings[".webm"] = "audio/webm";
provider.Mappings[".m4a"] = "audio/mp4";
provider.Mappings[".flac"] = "audio/flac";
app.UseStaticFiles(new StaticFileOptions { FileProvider = new PhysicalFileProvider(Path.Combine(builder.Environment.WebRootPath, "Uploads")), RequestPath = "/Uploads", ContentTypeProvider = provider, ServeUnknownFileTypes = true, DefaultContentType = "audio/mpeg", OnPrepareResponse = ctx =>
{
    // CORS 헤더 추가
    ctx.Context.Response.Headers.Append("Access-Control-Allow-Origin", "*");
    ctx.Context.Response.Headers.Append("Access-Control-Allow-Methods", "GET, HEAD, OPTIONS");
    ctx.Context.Response.Headers.Append("Access-Control-Allow-Headers", "*");
    // 캐싱 설정 (개발 중에는 no-cache)
    if (app.Environment.IsDevelopment())
    {
        ctx.Context.Response.Headers.Append("Cache-Control", "no-cache, no-store");
        ctx.Context.Response.Headers.Append("Pragma", "no-cache");
    }
} });

// 쿠키 정책 미들웨어는 인증 전에 위치
app.UseCookiePolicy();

app.UseCors("AllowAll"); // Apply CORS policy
app.MapControllers();
app.UseHeaderPropagation();
app.UseAuthentication();
app.UseAuthorization();
app.UseWebSockets();
app.UseMiddleware<WicsPlatform.Server.Middleware.WebSocketMiddleware>();
app.UseAntiforgery();
app.MapRazorComponents<App>().AddInteractiveWebAssemblyRenderMode().AddAdditionalAssemblies(typeof(WicsPlatform.Client._Imports).Assembly);
app.Services.CreateScope().ServiceProvider.GetRequiredService<ApplicationIdentityDbContext>().Database.Migrate();
app.Run();