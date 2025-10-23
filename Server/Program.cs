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
    // Radzen Blazor Studio���� ��ĳ���� �� �̰ɷ� �ؾ� ��
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

// ������ ��ȣ Ű ���� ��θ� ���� ���Ͽ��� �켱 �а�(IIS ���� ���� ���)
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
        // ������ ��ΰ� �߸��ưų� ������ ������ �Ʒ� �ڵ� ���� �������� ����
        dataProtectionKeysPath = null;
    }
}

if (string.IsNullOrEmpty(dataProtectionKeysPath))
{
    try
    {
        // 1����: ���ø����̼� ���� ���� (���� ���) - ������ �ִ� ��쿡��
        var candidate1 = Path.Combine(builder.Environment.ContentRootPath, "keys");
        Directory.CreateDirectory(candidate1);
        dataProtectionKeysPath = candidate1;
    }
    catch
    {
        try
        {
            // 2����: ����� ������(LocalApplicationData) - AppPool���� Load User Profile�� ���� �־�� ��
            var candidate2 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WicsPlatform", "keys");
            Directory.CreateDirectory(candidate2);
            dataProtectionKeysPath = candidate2;
        }
        catch
        {
            try
            {
                // 3����: ���� ProgramData - ���� ��å�� ���� ���� �ʿ�
                var candidate3 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "WicsPlatform", "keys");
                Directory.CreateDirectory(candidate3);
                dataProtectionKeysPath = candidate3;
            }
            catch
            {
                // ��� ��� ���� �� ����ȭ ���� ����(�α��� ���� ��� ����) -> IIS ���� ���� �ʿ�
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

// Client ���� ��� (Server-Side Rendering�� ���� �ʿ�)
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
        policy.AllowAnyMethod().AllowAnyHeader().SetIsOriginAllowed(_ => true) // �Ǵ� .WithOrigins("https://localhost:50553")
        .AllowCredentials(); // �� �κ��� �߿�!
    });
});
// Identity ��Ű ����� ���� (��� �� �̸� ��)
var relaxCookies = builder.Configuration.GetValue<bool>("AuthCookies:RelaxForIpClients");
builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login"; // ç���� �� �̵� ���
    options.LogoutPath = "/Account/Logout";
    options.AccessDeniedPath = "/Login";
    options.Cookie.Name = ".WicsPlatform.Identity";
    // HTTPS ����Ʈ���� ������ ��å�� ȣȯ�ǵ��� ����
    // �⺻(����) ��å
    options.Cookie.SameSite = SameSiteMode.None;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    // �ʿ��(���θ� IP/������ �̽ŷ� �׽�Ʈ ȯ��) ��ȭ ����ġ
    if (relaxCookies)
    {
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.None; // allow over HTTP if needed
    }
    options.SlidingExpiration = true;
});

// ���� ��Ű ��å (IIS/���Ͻ� ȯ�濡�� HTTPS �� Secure ����)
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

// Add forwarded headers support (IIS/���Ͻ� ȯ�濡�� HTTPS ��Ŵ ����)
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

// app.UseHttpsRedirection();  // �ּ� ó��
// ���� ���� ���� ���� - ���� �߿�!
// 1. �⺻ wwwroot ����
app.UseStaticFiles();
// 2. Uploads ������ ���� �߰� ����
var provider = new FileExtensionContentTypeProvider();
provider.Mappings[".mp3"] = "audio/mpeg";
provider.Mappings[".wav"] = "audio/wav";
provider.Mappings[".ogg"] = "audio/ogg";
provider.Mappings[".webm"] = "audio/webm";
provider.Mappings[".m4a"] = "audio/mp4";
provider.Mappings[".flac"] = "audio/flac";
app.UseStaticFiles(new StaticFileOptions { FileProvider = new PhysicalFileProvider(Path.Combine(builder.Environment.WebRootPath, "Uploads")), RequestPath = "/Uploads", ContentTypeProvider = provider, ServeUnknownFileTypes = true, DefaultContentType = "audio/mpeg", OnPrepareResponse = ctx =>
{
    // CORS ��� �߰�
    ctx.Context.Response.Headers.Append("Access-Control-Allow-Origin", "*");
    ctx.Context.Response.Headers.Append("Access-Control-Allow-Methods", "GET, HEAD, OPTIONS");
    ctx.Context.Response.Headers.Append("Access-Control-Allow-Headers", "*");
    // ĳ�� ���� (���� �߿��� no-cache)
    if (app.Environment.IsDevelopment())
    {
        ctx.Context.Response.Headers.Append("Cache-Control", "no-cache, no-store");
        ctx.Context.Response.Headers.Append("Pragma", "no-cache");
    }
} });

// ��Ű ��å �̵����� ���� ���� ��ġ
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