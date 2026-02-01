using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Mud.Core;
using Mud.Core.Services;
using Mud.Infrastructure.Data;

namespace Mud.Infrastructure.Services;

/// <summary>
/// EF Core implementation of character persistence.
/// </summary>
public class PersistenceService : IPersistenceService
{
    private readonly MudDbContext _context;
    private readonly ILogger<PersistenceService> _logger;

    public PersistenceService(MudDbContext context, ILogger<PersistenceService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<CharacterData?> LoadCharacterAsync(AccountId accountId)
    {
        var entity = await _context.Characters
            .FirstOrDefaultAsync(c => c.AccountId == accountId.Value);

        if (entity == null)
            return null;

        return ToCharacterData(entity);
    }

    public async Task<CharacterData> CreateCharacterAsync(AccountId accountId, string name)
    {
        var entity = new CharacterEntity
        {
            Id = Guid.NewGuid(),
            AccountId = accountId.Value,
            Name = name,
            Level = 1,
            Experience = 0,
            Strength = ProgressionFormulas.BaseStrength,
            Dexterity = ProgressionFormulas.BaseDexterity,
            Stamina = ProgressionFormulas.BaseStamina,
            UnspentPoints = 0,
            Health = ProgressionFormulas.MaxHealth(ProgressionFormulas.BaseStamina),
            MaxHealth = ProgressionFormulas.MaxHealth(ProgressionFormulas.BaseStamina),
            PositionX = 0,
            PositionY = 0,
            CurrentWorldId = null,
            LastOverworldX = 0,
            LastOverworldY = 0,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _context.Characters.Add(entity);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Created character {Name} for account {AccountId}", name, accountId.Value);
        return ToCharacterData(entity);
    }

    public async Task SaveProgressionAsync(CharacterId characterId, int experience, int level,
        int strength, int dexterity, int stamina, int unspentPoints)
    {
        var entity = await _context.Characters.FindAsync(characterId.Value);
        if (entity == null)
        {
            _logger.LogWarning("Character {CharacterId} not found for progression save", characterId.Value);
            return;
        }

        entity.Experience = experience;
        entity.Level = level;
        entity.Strength = strength;
        entity.Dexterity = dexterity;
        entity.Stamina = stamina;
        entity.UnspentPoints = unspentPoints;
        entity.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        _logger.LogDebug("Saved progression for character {CharacterId}: Level={Level}, XP={XP}",
            characterId.Value, level, experience);
    }

    public async Task SaveVolatileStateAsync(CharacterId characterId, int health, int positionX, int positionY,
        string? currentWorldId, int lastOverworldX, int lastOverworldY)
    {
        var entity = await _context.Characters.FindAsync(characterId.Value);
        if (entity == null)
        {
            _logger.LogWarning("Character {CharacterId} not found for volatile state save", characterId.Value);
            return;
        }

        entity.Health = health;
        entity.PositionX = positionX;
        entity.PositionY = positionY;
        entity.CurrentWorldId = currentWorldId;
        entity.LastOverworldX = lastOverworldX;
        entity.LastOverworldY = lastOverworldY;
        entity.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        _logger.LogDebug("Saved volatile state for character {CharacterId}: Position=({X},{Y})",
            characterId.Value, positionX, positionY);
    }

    public async Task SaveAllAsync(CharacterId characterId, CharacterData data)
    {
        var entity = await _context.Characters.FindAsync(characterId.Value);
        if (entity == null)
        {
            _logger.LogWarning("Character {CharacterId} not found for full save", characterId.Value);
            return;
        }

        entity.Experience = data.Experience;
        entity.Level = data.Level;
        entity.Strength = data.Strength;
        entity.Dexterity = data.Dexterity;
        entity.Stamina = data.Stamina;
        entity.UnspentPoints = data.UnspentPoints;
        entity.Health = data.Health;
        entity.MaxHealth = data.MaxHealth;
        entity.PositionX = data.PositionX;
        entity.PositionY = data.PositionY;
        entity.CurrentWorldId = data.CurrentWorldId;
        entity.LastOverworldX = data.LastOverworldX;
        entity.LastOverworldY = data.LastOverworldY;
        entity.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        _logger.LogDebug("Saved all state for character {CharacterId}", characterId.Value);
    }

    private static CharacterData ToCharacterData(CharacterEntity entity)
    {
        return new CharacterData
        {
            Id = new CharacterId(entity.Id),
            Name = entity.Name,
            Level = entity.Level,
            Experience = entity.Experience,
            Strength = entity.Strength,
            Dexterity = entity.Dexterity,
            Stamina = entity.Stamina,
            UnspentPoints = entity.UnspentPoints,
            Health = entity.Health,
            MaxHealth = entity.MaxHealth,
            PositionX = entity.PositionX,
            PositionY = entity.PositionY,
            CurrentWorldId = entity.CurrentWorldId,
            LastOverworldX = entity.LastOverworldX,
            LastOverworldY = entity.LastOverworldY
        };
    }
}
