using Microsoft.AspNetCore.Identity;

namespace Mud.Infrastructure.Data;

/// <summary>
/// Application user extending ASP.NET Identity.
/// </summary>
public class ApplicationUser : IdentityUser
{
    // Navigation property to characters
    public ICollection<CharacterEntity> Characters { get; set; } = new List<CharacterEntity>();
}
