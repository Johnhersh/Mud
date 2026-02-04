using Microsoft.AspNetCore.Identity;

namespace Mud.Core.Models;

/// <summary>
/// Application user extending ASP.NET Identity.
/// </summary>
public class ApplicationUser : IdentityUser
{
    public ICollection<CharacterEntity> Characters { get; set; } = new List<CharacterEntity>();
}
