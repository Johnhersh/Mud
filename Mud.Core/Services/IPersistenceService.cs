namespace Mud.Core.Services;

/// <summary>
/// Character data for persistence operations.
/// </summary>
public record CharacterData
{
    public required CharacterId Id { get; init; }
    public required string Name { get; init; }

    // Progression (persisted immediately on change)
    public int Level { get; init; } = 1;
    public int Experience { get; init; } = 0;
    public int Strength { get; init; } = 5;
    public int Dexterity { get; init; } = 5;
    public int Stamina { get; init; } = 5;
    public int UnspentPoints { get; init; } = 0;

    // Volatile state (persisted on disconnect)
    public int Health { get; init; } = 100;
    public int MaxHealth { get; init; } = 100;
    public int PositionX { get; init; } = 0;
    public int PositionY { get; init; } = 0;
    public string CurrentWorldId { get; init; } = WorldId.Overworld.Value;
    public int LastOverworldX { get; init; } = 0;
    public int LastOverworldY { get; init; } = 0;
}

/// <summary>
/// Persistence operations for character data.
/// </summary>
public interface IPersistenceService
{
    /// <summary>
    /// Load character data for an account. Returns null if no character exists.
    /// </summary>
    Task<CharacterData?> LoadCharacterAsync(AccountId accountId);

    /// <summary>
    /// Create a new character for an account.
    /// </summary>
    Task<CharacterData> CreateCharacterAsync(AccountId accountId, string name);

    /// <summary>
    /// Save progression data (XP, Level, Stats, UnspentPoints).
    /// Called immediately when these values change.
    /// </summary>
    Task SaveProgressionAsync(CharacterId characterId, int experience, int level,
        int strength, int dexterity, int stamina, int unspentPoints);

    /// <summary>
    /// Save volatile state (Position, Health, CurrentWorldId, LastOverworldPosition).
    /// Called on player disconnect.
    /// </summary>
    Task SaveVolatileStateAsync(CharacterId characterId, int health, int positionX, int positionY,
        string currentWorldId, int lastOverworldX, int lastOverworldY);

    /// <summary>
    /// Save all state for a character (used during graceful shutdown).
    /// </summary>
    Task SaveAllAsync(CharacterId characterId, CharacterData data);
}
