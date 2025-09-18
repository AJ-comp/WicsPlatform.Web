using Microsoft.AspNetCore.Identity;
using System.Text.Json.Serialization;

namespace WicsPlatform.Server.Models;

public partial class ApplicationRole : IdentityRole
{
    [JsonIgnore]
    public ICollection<ApplicationUser> Users { get; set; }

}
