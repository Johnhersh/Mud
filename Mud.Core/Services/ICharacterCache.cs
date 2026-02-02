namespace Mud.Core.Services;

/// <summary>
/// Cached character progression data (read from DB, invalidated on write).
/// </summary>
public record CharacterProgression
{
    public required int Level { get; init; }
    public required int Experience { get; init; }
    public required int Strength { get; init; }
    public required int Dexterity { get; init; }
    public required int Stamina { get; init; }
    public required int UnspentPoints { get; init; }
}

/// <summary>
/// Caches character progression data with DB as single source of truth.
/// </summary>
public interface ICharacterCache
{
    /// <summary>
    /// Get character progression data. Loads from DB on cache miss.
    /// </summary>
    Task<CharacterProgression?> GetProgressionAsync(CharacterId characterId);

    /// <summary>
    /// Invalidate cached data for a character. Call after DB writes.
    /// </summary>
    void Invalidate(CharacterId characterId);

    /// <summary>
    /// Pre-populate cache with character data (used on login).
    /// </summary>
    void Set(CharacterId characterId, CharacterProgression progression);
}
