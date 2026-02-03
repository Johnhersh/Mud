using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Mud.Core;
using Mud.Core.Models;
using Mud.Core.Services;
using Mud.Infrastructure.Data;

namespace Mud.Infrastructure.Services;

/// <summary>
/// IMemoryCache-backed character progression cache.
/// DB is the single source of truth; cache is invalidated on writes.
/// </summary>
public class CharacterCache : ICharacterCache
{
    private readonly IMemoryCache _cache;
    private readonly MudDbContext _context;
    private readonly ILogger<CharacterCache> _logger;

    private static readonly TimeSpan CacheExpiration = TimeSpan.FromMinutes(30);

    public CharacterCache(
        IMemoryCache cache,
        MudDbContext context,
        ILogger<CharacterCache> logger)
    {
        _cache = cache;
        _context = context;
        _logger = logger;
    }

    public async Task<CharacterProgression?> GetProgressionAsync(CharacterId characterId)
    {
        if (_cache.TryGetValue<CharacterProgression>(characterId, out var cached))
        {
            return cached;
        }

        // Cache miss - load from DB
        var entity = await _context.Characters
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == characterId.Value);

        if (entity is null)
        {
            _logger.LogWarning("Character {CharacterId} not found in database", characterId.Value);
            return null;
        }

        var progression = new CharacterProgression
        {
            Level = entity.Level,
            Experience = entity.Experience,
            Strength = entity.Strength,
            Dexterity = entity.Dexterity,
            Stamina = entity.Stamina,
            UnspentPoints = entity.UnspentPoints
        };

        _cache.Set(characterId, progression, CacheExpiration);
        _logger.LogDebug("Cached progression for character {CharacterId}", characterId.Value);

        return progression;
    }

    public void Invalidate(CharacterId characterId)
    {
        _cache.Remove(characterId);
        _logger.LogDebug("Invalidated cache for character {CharacterId}", characterId.Value);
    }

    public void Set(CharacterId characterId, CharacterProgression progression)
    {
        _cache.Set(characterId, progression, CacheExpiration);
    }
}
