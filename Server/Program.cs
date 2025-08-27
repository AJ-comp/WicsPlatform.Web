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

    // appsettings.json에서 연결 문자열 가져오기
    /*
        var connectionString = builder.Configuration.GetConnectionString("wicsConnection");

        // DbContext를 종속성 주입을 통해 등록
        builder.Services.AddDbContext<wicsContext>(options => options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString)));
        builder.Services.AddDbContext<ApplicationIdentityDbContext>(options =>
        {
            options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString));
        });
        builder.Services.AddIdentity<ApplicationUser, ApplicationRole>().AddEntityFrameworkStores<ApplicationIdentityDbContext>().AddDefaultTokenProviders();

        builder.Services.AddDbContext<WicsPlatform.Server.Data.wicsContext>(options =>
        {
            options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString));
        });
        builder.Services.AddScoped<WicsPlatform.Client.Services.BroadcastWebSocketService>();
    */
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
builder.Services.AddHttpClient();
builder.Services.AddScoped<WicsPlatform.Server.wicsService>();
builder.Services.AddSingleton<IAudioMixingService, AudioMixingService>();
builder.Services.AddSingleton<IUdpBroadcastService, UdpBroadcastService>();
builder.Services.AddSingleton<ITtsBroadcastService, TtsBroadcastService>();
builder.Services.AddSingleton<IMediaBroadcastService, MediaBroadcastService>();
builder.Services.AddSingleton<OpusCodec>(provider => new OpusCodec(48000, 1, 32000));
RegisterDBContext(builder);
builder.Services.AddControllers().AddOData(opt =>
{
    var oDataBuilderwics = new ODataConventionModelBuilder();
    oDataBuilderwics.EntitySet<WicsPlatform.Server.Models.wics.Broadcast>("Broadcasts");
    oDataBuilderwics.EntitySet<WicsPlatform.Server.Models.wics.Channel>("Channels");
    oDataBuilderwics.EntitySet<WicsPlatform.Server.Models.wics.Group>("Groups");
    oDataBuilderwics.EntitySet<WicsPlatform.Server.Models.wics.MapChannelMedium>("MapChannelMedia");
    oDataBuilderwics.EntitySet<WicsPlatform.Server.Models.wics.MapChannelTt>("MapChannelTts");
    oDataBuilderwics.EntitySet<WicsPlatform.Server.Models.wics.MapMediaGroup>("MapMediaGroups");
    oDataBuilderwics.EntitySet<WicsPlatform.Server.Models.wics.MapSpeakerGroup>("MapSpeakerGroups");
    oDataBuilderwics.EntitySet<WicsPlatform.Server.Models.wics.Medium>("Media");
    oDataBuilderwics.EntitySet<WicsPlatform.Server.Models.wics.Mic>("Mics");
    oDataBuilderwics.EntitySet<WicsPlatform.Server.Models.wics.Speaker>("Speakers");
    oDataBuilderwics.EntitySet<WicsPlatform.Server.Models.wics.Tt>("Tts");
    opt.AddRouteComponents("odata/wics", oDataBuilderwics.GetEdmModel()).Count().Filter().OrderBy().Expand().Select().SetMaxTop(null).TimeZone = TimeZoneInfo.Utc;
});
builder.Services.AddScoped<WicsPlatform.Client.wicsService>();
builder.Services.AddHttpClient("WicsPlatform.Server").ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { UseCookies = false }).AddHeaderPropagation(o => o.Headers.Add("Cookie"));
builder.Services.AddHeaderPropagation(o => o.Headers.Add("Cookie"));
builder.Services.AddAuthentication();
builder.Services.AddAuthorization();
builder.Services.AddScoped<WicsPlatform.Client.SecurityService>();
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
builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.SameSite = SameSiteMode.Lax; // None -> Lax 변경
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest; // Always -> SameAsRequest 변경
});
builder.Services.AddDbContext<WicsPlatform.Server.Data.wicsContext>(options =>
{
    options.UseMySql(builder.Configuration.GetConnectionString("wicsConnection"), ServerVersion.AutoDetect(builder.Configuration.GetConnectionString("wicsConnection")));
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    // app.UseHsts();  // 주석 처리
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

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(
       Path.Combine(builder.Environment.WebRootPath, "Uploads")),
    RequestPath = "/Uploads",
    ContentTypeProvider = provider,
    ServeUnknownFileTypes = true,
    DefaultContentType = "audio/mpeg",
    OnPrepareResponse = ctx =>
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
    }
});

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