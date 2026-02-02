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

    // Derived from Stamina - not persisted
    public int MaxHealth => ProgressionFormulas.MaxHealth(Stamina);
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
    /// Stage progression data changes (XP, Level, Stats, UnspentPoints).
    /// Call FlushAsync to commit.
    /// </summary>
    Task UpdateProgressionAsync(CharacterId characterId, int experience, int level,
        int strength, int dexterity, int stamina, int unspentPoints);

    /// <summary>
    /// Stage volatile state changes (Position, Health, CurrentWorldId, LastOverworldPosition).
    /// Call FlushAsync to commit.
    /// </summary>
    Task UpdateVolatileStateAsync(CharacterId characterId, int health, int positionX, int positionY,
        string currentWorldId, int lastOverworldX, int lastOverworldY);

    /// <summary>
    /// Stage all state changes for a character.
    /// Call FlushAsync to commit.
    /// </summary>
    Task UpdateAllAsync(CharacterId characterId, CharacterData data);

    /// <summary>
    /// Commit all staged changes to the database.
    /// </summary>
    Task FlushAsync();
}
