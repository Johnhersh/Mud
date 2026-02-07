using Mud.Core;
using Mud.Core.Services;

namespace Mud.Tests;

/// <summary>
/// In-memory ICharacterCache for tests. No database, no expiry.
/// </summary>
public class TestCharacterCache : ICharacterCache
{
    private readonly Dictionary<CharacterId, CharacterProgression> _cache = new();

    public Task<CharacterProgression?> GetProgressionAsync(CharacterId characterId)
    {
        _cache.TryGetValue(characterId, out var progression);
        return Task.FromResult(progression);
    }

    public void Invalidate(CharacterId characterId)
    {
        _cache.Remove(characterId);
    }

    public void Set(CharacterId characterId, CharacterProgression progression)
    {
        _cache[characterId] = progression;
    }
}
