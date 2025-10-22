using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using WicsPlatform.Server.Models;
using System.IO; // added for file logging

namespace WicsPlatform.Server.Controllers
{
    [Route("Account/[action]")]
    public partial class AccountController : Controller
    {
        private readonly SignInManager<ApplicationUser> signInManager;
        private readonly UserManager<ApplicationUser> userManager;
        private readonly RoleManager<ApplicationRole> roleManager;
        private readonly IWebHostEnvironment env;
        private readonly IConfiguration configuration;
        private readonly ILogger<AccountController> logger;

        private readonly string loginLogDirectory; // configured login log directory

        public AccountController(
            IWebHostEnvironment env,
            SignInManager<ApplicationUser> signInManager,
            UserManager<ApplicationUser> userManager,
            RoleManager<ApplicationRole> roleManager,
            IConfiguration configuration,
            ILogger<AccountController> logger)
        {
            this.signInManager = signInManager;
            this.userManager = userManager;
            this.roleManager = roleManager;
            this.env = env;
            this.configuration = configuration;
            this.logger = logger;

            // Read login log directory from configuration: AuthLogging:LoginLogPath
            var cfgPath = configuration["AuthLogging:LoginLogPath"];
            if (!string.IsNullOrWhiteSpace(cfgPath))
            {
                try
                {
                    // If relative path, resolve against content root
                    loginLogDirectory = Path.IsPathRooted(cfgPath)
                        ? cfgPath
                        : Path.Combine(env.ContentRootPath, cfgPath);

                    Directory.CreateDirectory(loginLogDirectory);
                }
                catch
                {
                    loginLogDirectory = null; // if fails, disable file logging
                }
            }
        }

        private void WriteLoginLog(string message)
        {
            try
            {
                if (string.IsNullOrEmpty(loginLogDirectory))
                {
                    return; // not configured or not available
                }

                var ip = HttpContext?.Connection?.RemoteIpAddress?.ToString() ?? "-";
                var ua = HttpContext?.Request?.Headers["User-Agent"].ToString() ?? "-";
                var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] IP={ip} UA={ua} :: {message}";

                var file = Path.Combine(loginLogDirectory, $"login-{DateTime.Now:yyyyMMdd}.log");
                System.IO.File.AppendAllText(file, line + Environment.NewLine);
            }
            catch
            {
                // ignore file logging errors
            }
        }

        private IActionResult RedirectWithError(string error, string redirectUrl = null)
        {
            if (!string.IsNullOrEmpty(redirectUrl))
            {
                return Redirect($"~/Login?error={error}&redirectUrl={Uri.EscapeDataString(redirectUrl.Replace("~", ""))}");
            }
            else
            {
                return Redirect($"~/Login?error={error}");
            }
        }

        [HttpGet]
        public async Task<IActionResult> Login(string returnUrl)
        {
            logger.LogInformation($"GET Login called with returnUrl: {returnUrl}");
            WriteLoginLog($"GET /Account/Login returnUrl='{returnUrl}'");

            if (returnUrl != "/" && !string.IsNullOrEmpty(returnUrl))
            {
                return Redirect($"~/Login?redirectUrl={Uri.EscapeDataString(returnUrl)}");
            }

            return Redirect("~/Login");
        }

        [HttpPost]
        public async Task<IActionResult> Login([FromForm] LoginModel model)
        {
            // Never log password!
            logger.LogInformation($"POST Login called with userName: {model.userName}, redirectUrl: {model.redirectUrl}");
            WriteLoginLog($"POST /Account/Login userName='{model.userName}' redirectUrl='{model.redirectUrl}'");

            string redirectUrl = string.IsNullOrEmpty(model.redirectUrl) ? "/" : model.redirectUrl.StartsWith("/") ? model.redirectUrl : $"/{model.redirectUrl}";

            try
            {
                if (env.IsDevelopment() && model.userName == "admin" && model.password == "admin")
                {
                    var claims = new List<Claim>()
                    {
                        new Claim(ClaimTypes.Name, "admin"),
                        new Claim(ClaimTypes.Email, "admin")
                    };

                    foreach (var role in roleManager.Roles)
                    {
                        claims.Add(new Claim(ClaimTypes.Role, role.Name));
                    }

                    await signInManager.SignInWithClaimsAsync(new ApplicationUser { UserName = model.userName, Email = model.userName }, isPersistent: false, claims);

                    logger.LogInformation("Development admin login successful");
                    WriteLoginLog($"SUCCESS user='{model.userName}' (dev admin) -> redirect='{redirectUrl}'");
                    return Redirect(redirectUrl);
                }

                if (!string.IsNullOrEmpty(model.userName) && !string.IsNullOrEmpty(model.password))
                {
                    var user = await userManager.FindByNameAsync(model.userName);
                    if (user == null)
                    {
                        user = await userManager.FindByEmailAsync(model.userName);
                    }

                    if (user != null)
                    {
                        var result = await signInManager.PasswordSignInAsync(user.UserName, model.password, false, false);

                        if (result.Succeeded)
                        {
                            logger.LogInformation($"User {model.userName} logged in successfully");
                            WriteLoginLog($"SUCCESS user='{model.userName}' -> redirect='{redirectUrl}'");
                            return Redirect(redirectUrl);
                        }
                        else
                        {
                            logger.LogWarning($"Failed login attempt for {model.userName}: {result.ToString()}");
                            WriteLoginLog($"FAIL user='{model.userName}' result='{result}'");
                        }
                    }
                    else
                    {
                        logger.LogWarning($"User not found: {model.userName}");
                        WriteLoginLog($"FAIL user not found: '{model.userName}'");
                    }
                }

                return RedirectWithError("Invalid user or password", model.redirectUrl);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Login error");
                WriteLoginLog($"ERROR user='{model.userName}' ex='{ex.Message}'");
                return RedirectWithError($"Login error: {ex.Message}", model.redirectUrl);
            }
        }

        [HttpPost]
        [Authorize]
        public async Task<IActionResult> ChangePassword(string oldPassword, string newPassword)
        {
            if (string.IsNullOrEmpty(oldPassword) || string.IsNullOrEmpty(newPassword))
            {
                return BadRequest("Invalid password");
            }

            var id = User.FindFirstValue(ClaimTypes.NameIdentifier);

            var user = await userManager.FindByIdAsync(id);
            var result = await userManager.ChangePasswordAsync(user, oldPassword, newPassword);

            if (result.Succeeded)
            {
                return Ok();
            }

            var message = string.Join(", ", result.Errors.Select(error => error.Description));

            return BadRequest(message);
        }

        [HttpPost]
        public ApplicationAuthenticationState CurrentUser()
        {
            return new ApplicationAuthenticationState
            {
                IsAuthenticated = User.Identity.IsAuthenticated,
                Name = User.Identity.Name,
                Claims = User.Claims.Select(c => new ApplicationClaim { Type = c.Type, Value = c.Value })
            };
        }

        public async Task<IActionResult> Logout()
        {
            await signInManager.SignOutAsync();
            WriteLoginLog($"LOGOUT user='{User?.Identity?.Name ?? "(anonymous)"}'");

            return Redirect("~/");
        }

        public class LoginModel
        {
            public string userName { get; set; }
            public string password { get; set; }
            public string redirectUrl { get; set; }
        }
    }
}
