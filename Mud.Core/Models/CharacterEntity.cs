using System.ComponentModel.DataAnnotations;

namespace Mud.Core.Models;

/// <summary>
/// Character database entity.
/// </summary>
public class CharacterEntity
{
    public Guid Id { get; set; }

    [Required]
    public required string AccountId { get; set; }

    [Required]
    [MaxLength(50)]
    public required string Name { get; set; }

    // Progression (persisted immediately)
    public int Level { get; set; } = 1;
    public int Experience { get; set; } = 0;
    public int Strength { get; set; } = 5;
    public int Dexterity { get; set; } = 5;
    public int Stamina { get; set; } = 5;
    public int UnspentPoints { get; set; } = 0;

    // Volatile state (persisted on disconnect)
    public int Health { get; set; } = 100;
    public int PositionX { get; set; } = 0;
    public int PositionY { get; set; } = 0;
    public string CurrentWorldId { get; set; } = WorldId.Overworld.Value;
    public int LastOverworldX { get; set; } = 0;
    public int LastOverworldY { get; set; } = 0;

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Navigation
    public ApplicationUser? Account { get; set; }
}
