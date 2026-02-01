using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Mud.Infrastructure.Data;

/// <summary>
/// Character database entity.
/// </summary>
[Table("Characters")]
public class CharacterEntity
{
    [Key]
    public Guid Id { get; set; }

    [Required]
    public string AccountId { get; set; } = string.Empty;

    [Required]
    [MaxLength(50)]
    public string Name { get; set; } = string.Empty;

    // Progression (persisted immediately)
    public int Level { get; set; } = 1;
    public int Experience { get; set; } = 0;
    public int Strength { get; set; } = 5;
    public int Dexterity { get; set; } = 5;
    public int Stamina { get; set; } = 5;
    public int UnspentPoints { get; set; } = 0;

    // Volatile state (persisted on disconnect)
    public int Health { get; set; } = 100;
    public int MaxHealth { get; set; } = 100;
    public int PositionX { get; set; } = 0;
    public int PositionY { get; set; } = 0;
    public string? CurrentWorldId { get; set; }
    public int LastOverworldX { get; set; } = 0;
    public int LastOverworldY { get; set; } = 0;

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Navigation
    [ForeignKey(nameof(AccountId))]
    public ApplicationUser? Account { get; set; }
}
