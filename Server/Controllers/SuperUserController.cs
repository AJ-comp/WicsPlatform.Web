using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using WicsPlatform.Server.Models;
using WicsPlatform.Server.Data;

namespace WicsPlatform.Server.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class SuperUserController : ControllerBase
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly RoleManager<ApplicationRole> _roleManager;

        public SuperUserController(
            UserManager<ApplicationUser> userManager,
            RoleManager<ApplicationRole> roleManager)
        {
            _userManager = userManager;
            _roleManager = roleManager;
        }

        [HttpGet("exists")]
        public async Task<IActionResult> AdminExists()
        {
            // Check if the 'Administrators' role exists
            bool adminRoleExists = await _roleManager.RoleExistsAsync("Administrators");

            if (!adminRoleExists)
            {
                // No Administrators role exists yet
                return Ok(new { exists = false });
            }

            // Check if any user is in the Administrators role
            var usersInRole = await _userManager.GetUsersInRoleAsync("Administrators");
            return Ok(new { exists = usersInRole.Any() });
        }

        [HttpPost("setup")]
        public async Task<IActionResult> SetupAdmin([FromBody] AdminSetupModel model)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            // Check if the Administrators role exists, create it if not
            if (!await _roleManager.RoleExistsAsync("Administrators"))
            {
                await _roleManager.CreateAsync(new ApplicationRole { Name = "Administrators" });
            }

            // Check if any Administrator already exists
            var existingAdmins = await _userManager.GetUsersInRoleAsync("Administrators");
            if (existingAdmins.Any())
            {
                return BadRequest(new { error = "Administrator user already exists" });
            }

            // Create the admin user
            var user = new ApplicationUser
            {
                UserName = model.Email,
                Email = model.Email,
                EmailConfirmed = true
            };

            var result = await _userManager.CreateAsync(user, model.Password);

            if (result.Succeeded)
            {
                // Add the user to Administrators role
                await _userManager.AddToRoleAsync(user, "Administrators");

                return Ok(new { success = true });
            }

            return BadRequest(new { error = string.Join(", ", result.Errors.Select(e => e.Description)) });
        }
    }

    public class AdminSetupModel
    {
        public string Email { get; set; }
        public string Password { get; set; }
        public string ConfirmPassword { get; set; }
    }
}
