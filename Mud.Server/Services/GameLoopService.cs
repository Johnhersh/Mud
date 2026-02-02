using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using Mud.Server.Hubs;
using Mud.Server.World;
using Mud.Server.World.Generation;
using Mud.Core;
using Mud.Core.World;
using Mud.Core.Services;

namespace Mud.Server.Services;

public class GameLoopService : BackgroundService
{
    private readonly IHubContext<GameHub, IGameClient> _hubContext;
    private readonly ILogger<GameLoopService> _logger;
    private readonly IServiceScopeFactory _scopeFactory;

    // World management
    private readonly ConcurrentDictionary<WorldId, WorldState> _worlds = new();
    private WorldState _overworld = null!;

    // Player tracking (keyed by SignalR ConnectionId)
    private readonly ConcurrentDictionary<string, PlayerSession> _sessions = new();
    private readonly ConcurrentDictionary<string, ConcurrentQueue<Direction>> _playerInputQueues = new();

    // Attack events per world (cleared after each broadcast)
    private readonly ConcurrentDictionary<WorldId, ConcurrentBag<AttackEvent>> _worldAttackEvents = new();

    // XP events: keyed by world, then by connection ID (for per-player sends)
    private readonly ConcurrentDictionary<WorldId, ConcurrentDictionary<string, List<XpGainEvent>>> _worldPlayerXpEvents = new();

    // Level-up events: keyed by world only (broadcast to all in world)
    private readonly ConcurrentDictionary<WorldId, ConcurrentBag<LevelUpEvent>> _worldLevelUpEvents = new();

    // Progression updates: keyed by connection ID (sent per-player when their progression changes)
    private readonly ConcurrentDictionary<string, ProgressionUpdate> _pendingProgressionUpdates = new();

    // Pending persistence operations (processed async to not block game loop)
    private readonly ConcurrentQueue<PersistenceOp> _pendingPersistenceOps = new();

    private record PersistenceOp(CharacterId CharacterId, Func<IPersistenceService, Task> Operation);

    private long _tick = 0;

    public GameLoopService(
        IHubContext<GameHub, IGameClient> hubContext,
        ILogger<GameLoopService> logger,
        IServiceScopeFactory scopeFactory)
    {
        _hubContext = hubContext;
        _logger = logger;
        _scopeFactory = scopeFactory;

        InitializeWorld();
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Server shutting down, saving all player states...");
        await SaveAllPlayersAsync();
        _logger.LogInformation("All player states saved");

        await base.StopAsync(cancellationToken);
    }

    private async Task SaveAllPlayersAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var persistenceService = scope.ServiceProvider.GetRequiredService<IPersistenceService>();
        var charCache = scope.ServiceProvider.GetRequiredService<ICharacterCache>();

        foreach (var session in _sessions.Values)
        {
            var (world, entity) = FindPlayerInternal(session.ConnectionId);
            if (entity == null) continue;

            // Determine overworld position for resume
            var (overworldX, overworldY) = session.CurrentWorldId == _overworld.Id
                ? (entity.Position.X, entity.Position.Y)
                : (session.LastOverworldPosition.X, session.LastOverworldPosition.Y);

            var progression = await charCache.GetProgressionAsync(session.CharacterId);

            var data = new CharacterData
            {
                Id = session.CharacterId,
                Name = entity.Name,
                Level = progression?.Level ?? 1,
                Experience = progression?.Experience ?? 0,
                Strength = progression?.Strength ?? ProgressionFormulas.BaseStrength,
                Dexterity = progression?.Dexterity ?? ProgressionFormulas.BaseDexterity,
                Stamina = progression?.Stamina ?? ProgressionFormulas.BaseStamina,
                UnspentPoints = progression?.UnspentPoints ?? 0,
                Health = entity.Health,
                PositionX = overworldX,
                PositionY = overworldY,
                CurrentWorldId = WorldId.Overworld.Value,
                LastOverworldX = overworldX,
                LastOverworldY = overworldY
            };

            await persistenceService.UpdateAllAsync(session.CharacterId, data);
        }

        await persistenceService.FlushAsync();
    }

    private void InitializeWorld()
    {
        _logger.LogInformation("Generating overworld with seed {Seed}...", WorldConfig.WorldSeed);
        _overworld = WorldGenerator.GenerateOverworld(WorldConfig.WorldSeed);
        _worlds[_overworld.Id] = _overworld;
        _logger.LogInformation("Overworld generated: {Width}x{Height} with {POICount} POIs",
            _overworld.Terrain.Width, _overworld.Terrain.Height, _overworld.POIs.Count);
    }

    private void SpawnMonsters(WorldState world, int count)
    {
        var random = new Random(WorldConfig.WorldSeed);
        for (int i = 0; i < count; i++)
        {
            var pos = world.FindRandomWalkablePosition(random);
            if (pos == null) continue;

            var monster = new Entity
            {
                Id = $"monster_{world.Id}_{i}",
                Name = "Goblin",
                Position = pos,
                QueuedPath = new List<Point>(),
                Type = EntityType.Monster,
                Health = 50,
                MaxHealth = 50,
                Level = 1
            };
            world.AddEntity(monster);
        }
    }

    /// <summary>
    /// Add a player from persisted character data.
    /// </summary>
    /// <summary>
    /// Add a player from persisted data. Returns kicked connection ID if concurrent login detected.
    /// </summary>
    public string? AddPlayerFromPersistence(string connectionId, AccountId accountId, CharacterData characterData)
    {
        // Check for existing session with same account (concurrent login)
        string? kickedConnectionId = null;
        var existingSession = _sessions.Values.FirstOrDefault(s => s.AccountId == accountId);
        if (existingSession != null)
        {
            kickedConnectionId = existingSession.ConnectionId;
            RemovePlayer(existingSession.ConnectionId);
            _logger.LogInformation("Kicked existing session {ConnectionId} for account {AccountId}",
                existingSession.ConnectionId, accountId.Value);
        }

        // Determine spawn position
        Point spawnPos;
        WorldId spawnWorld;

        // Check if saved world still exists
        var savedWorldId = characterData.CurrentWorldId != null
            ? new WorldId(characterData.CurrentWorldId)
            : _overworld.Id;

        if (_worlds.ContainsKey(savedWorldId) && savedWorldId != _overworld.Id)
        {
            // Instance still exists, spawn there
            spawnWorld = savedWorldId;
            spawnPos = new Point(characterData.PositionX, characterData.PositionY);
        }
        else
        {
            // Spawn at last overworld position (or town if invalid)
            spawnWorld = _overworld.Id;
            if (characterData.LastOverworldX > 0 || characterData.LastOverworldY > 0)
            {
                spawnPos = new Point(characterData.LastOverworldX, characterData.LastOverworldY);
            }
            else
            {
                var spawnTown = _overworld.POIs.FirstOrDefault(p => p.Type == POIType.Town);
                spawnPos = spawnTown?.Position ?? new Point(
                    WorldConfig.OverworldWidth / 2,
                    WorldConfig.OverworldHeight / 2
                );
            }
        }

        // Create player session
        var session = new PlayerSession
        {
            ConnectionId = connectionId,
            AccountId = accountId,
            CharacterId = characterData.Id,
            Name = characterData.Name,
            CurrentWorldId = spawnWorld,
            Position = spawnPos,
            LastOverworldPosition = new Point(characterData.LastOverworldX, characterData.LastOverworldY)
        };
        _sessions[connectionId] = session;

        // Create player entity (volatile state only - progression lives in cache)
        var entity = new Entity
        {
            Id = connectionId,
            Name = characterData.Name,
            Position = spawnPos,
            QueuedPath = new List<Point>(),
            Type = EntityType.Player,
            Health = characterData.Health,
            MaxHealth = characterData.MaxHealth,
            Level = characterData.Level
        };

        if (_worlds.TryGetValue(spawnWorld, out var world))
        {
            world.AddEntity(entity);
        }

        _logger.LogInformation("Player {Name} (Character {CharacterId}) joined at {Position} in {World}",
            characterData.Name, characterData.Id.Value, spawnPos, spawnWorld);

        return kickedConnectionId;
    }

    public void RemovePlayer(string connectionId)
    {
        if (_sessions.TryRemove(connectionId, out var session))
        {
            // Remove from current world
            if (_worlds.TryGetValue(session.CurrentWorldId, out var world))
            {
                world.RemoveEntity(connectionId);

                // Clean up instance if empty
                if (world.Type == WorldType.Instance && !world.GetPlayers().Any())
                {
                    _worlds.TryRemove(world.Id, out _);
                    _logger.LogInformation("Instance {WorldId} destroyed (empty)", world.Id);
                }
            }
        }
        _playerInputQueues.TryRemove(connectionId, out _);
    }

    /// <summary>
    /// Remove player and save their state to the database.
    /// </summary>
    public async Task RemovePlayerAsync(string connectionId)
    {
        if (_sessions.TryGetValue(connectionId, out var session))
        {
            // Get entity data before removal
            var (world, entity) = FindPlayerInternal(connectionId);

            // Save state if we have entity
            if (entity != null)
            {
                // Determine overworld position
                var (overworldX, overworldY) = session.CurrentWorldId == _overworld.Id
                    ? (entity.Position.X, entity.Position.Y)
                    : (session.LastOverworldPosition.X, session.LastOverworldPosition.Y);

                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var persistenceService = scope.ServiceProvider.GetRequiredService<IPersistenceService>();

                    await persistenceService.UpdateVolatileStateAsync(
                        session.CharacterId,
                        entity.Health,
                        overworldX,
                        overworldY,
                        WorldId.Overworld.Value,
                        overworldX,
                        overworldY
                    );
                    await persistenceService.FlushAsync();

                    _logger.LogInformation("Saved volatile state for character {CharacterId}", session.CharacterId.Value);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to save volatile state for character {CharacterId}", session.CharacterId.Value);
                }
            }
        }

        // Now remove from game
        RemovePlayer(connectionId);
    }

    public PlayerSession? GetSession(string connectionId)
    {
        return _sessions.TryGetValue(connectionId, out var session) ? session : null;
    }

    public WorldState? GetWorld(WorldId worldId)
    {
        return _worlds.TryGetValue(worldId, out var world) ? world : null;
    }

    public void EnqueueInput(string connectionId, Direction direction)
    {
        var queue = _playerInputQueues.GetOrAdd(connectionId, _ => new ConcurrentQueue<Direction>());
        if (queue.Count < 5)
        {
            queue.Enqueue(direction);
        }
    }

    public void Interact(string connectionId)
    {
        if (!_sessions.TryGetValue(connectionId, out var session))
            return;

        if (!_worlds.TryGetValue(session.CurrentWorldId, out var world))
            return;

        var entity = world.GetEntity(connectionId);
        if (entity == null) return;

        if (world.Type == WorldType.Overworld)
        {
            // Check for POI
            var poi = world.GetPOIAt(entity.Position);
            if (poi is not null) EnterInstance(session, poi, world, entity);
        }
        else // Instance
        {
            // Check for exit
            if (world.IsExitMarker(entity.Position)) ExitInstance(session, world, entity);
        }
    }

    private void EnterInstance(PlayerSession session, POI poi, WorldState overworld, Entity entity)
    {
        var instanceId = new WorldId($"instance_{poi.Id}");

        // Get or create instance
        if (!_worlds.TryGetValue(instanceId, out var instance))
        {
            instance = WorldGenerator.GenerateInstance(poi, overworld.Terrain, WorldConfig.WorldSeed);
            _worlds[instanceId] = instance;

            SpawnMonsters(instance, 3);
            _logger.LogInformation("Instance {InstanceId} created for POI {POIId}", instanceId, poi.Id);
        }

        // Find spawn position in instance
        var random = new Random();
        var spawnPos = instance.FindRandomWalkablePosition(random) ?? new Point(
            WorldConfig.InstanceWidth / 2,
            WorldConfig.InstanceHeight / 2
        );

        // Save overworld position
        session.SaveOverworldPosition();

        // Remove from overworld
        overworld.RemoveEntity(session.ConnectionId);

        // Add to instance
        var newEntity = entity with { Position = spawnPos, QueuedPath = new List<Point>() };
        instance.AddEntity(newEntity);

        // Update session
        session.TransferToWorld(instanceId, spawnPos);
        _logger.LogInformation("Player {ConnectionId} entered instance {InstanceId}", session.ConnectionId, instanceId);
    }

    private void ExitInstance(PlayerSession session, WorldState instance, Entity entity)
    {
        // Remove from instance
        instance.RemoveEntity(session.ConnectionId);

        // Add back to overworld
        var newEntity = entity with
        {
            Position = session.LastOverworldPosition,
            QueuedPath = new List<Point>()
        };
        _overworld.AddEntity(newEntity);

        // Update session
        session.TransferToWorld(_overworld.Id, session.LastOverworldPosition);
        _logger.LogInformation("Player {ConnectionId} exited to overworld", session.ConnectionId);

        // Clean up instance if empty
        if (!instance.GetPlayers().Any())
        {
            _worlds.TryRemove(instance.Id, out _);
            _logger.LogInformation("Instance {InstanceId} destroyed (empty)", instance.Id);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var startTime = DateTime.UtcNow;

            Update();
            await Broadcast();

            // Process pending persistence operations (non-blocking)
            await ProcessPendingPersistenceOps();

            _tick++;

            var elapsed = DateTime.UtcNow - startTime;
            var delay = TimeSpan.FromMilliseconds(300) - elapsed;
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, stoppingToken);
            }
        }
    }

    private async Task ProcessPendingPersistenceOps()
    {
        if (_pendingPersistenceOps.IsEmpty) return;

        using var scope = _scopeFactory.CreateScope();
        var persistenceService = scope.ServiceProvider.GetRequiredService<IPersistenceService>();
        var characterCache = scope.ServiceProvider.GetRequiredService<ICharacterCache>();

        var affectedCharacters = new HashSet<CharacterId>();

        while (_pendingPersistenceOps.TryDequeue(out var op))
        {
            try
            {
                await op.Operation(persistenceService);
                affectedCharacters.Add(op.CharacterId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error executing persistence operation for character {CharacterId}", op.CharacterId);
            }
        }

        await persistenceService.FlushAsync();

        // Invalidate cache for all affected characters
        foreach (var charId in affectedCharacters)
        {
            characterCache.Invalidate(charId);
        }
    }

    private void Update()
    {
        // Process each world
        foreach (var world in _worlds.Values)
        {
            UpdateWorld(world);
        }
    }

    private void UpdateWorld(WorldState world)
    {
        var players = world.GetPlayers().ToList();
        foreach (var playerEntity in players)
        {
            var connectionId = playerEntity.Id;
            if (!_playerInputQueues.TryGetValue(connectionId, out var queue) || !queue.TryDequeue(out var direction))
            {
                continue;
            }

            var player = world.GetEntity(playerEntity.Id);
            if (player == null) continue;

            var newPos = direction switch
            {
                Direction.Up => player.Position with { Y = player.Position.Y - 1 },
                Direction.Down => player.Position with { Y = player.Position.Y + 1 },
                Direction.Left => player.Position with { X = player.Position.X - 1 },
                Direction.Right => player.Position with { X = player.Position.X + 1 },
                _ => player.Position
            };

            // Check for monster at destination
            var target = world.Entities.Values.FirstOrDefault(e => e.Position == newPos && e.Type == EntityType.Monster);
            if (target != null)
            {
                ProcessAttack(world, playerEntity.Id, target.Id, isMelee: true);
                // Clear queue on attack
                while (queue.TryDequeue(out _)) ;
                // Re-fetch player after ProcessAttack since it may have been updated with XP
                var updatedPlayer = world.GetEntity(playerEntity.Id);
                if (updatedPlayer != null)
                {
                    world.UpdateEntity(updatedPlayer with { QueuedPath = new List<Point>() });
                }
                continue;
            }

            if (world.IsWalkable(newPos))
            {
                // Update position and recalculate queued path for the client
                var currentPos = newPos;
                var queuedPath = new List<Point>();
                foreach (var queuedDir in queue)
                {
                    currentPos = queuedDir switch
                    {
                        Direction.Up => currentPos with { Y = currentPos.Y - 1 },
                        Direction.Down => currentPos with { Y = currentPos.Y + 1 },
                        Direction.Left => currentPos with { X = currentPos.X - 1 },
                        Direction.Right => currentPos with { X = currentPos.X + 1 },
                        _ => currentPos
                    };

                    if (!world.IsWalkable(currentPos)) break;
                    // Also break if there's a monster
                    if (world.Entities.Values.Any(e => e.Position == currentPos && e.Type == EntityType.Monster)) break;

                    queuedPath.Add(currentPos);
                }

                world.UpdateEntity(player with
                {
                    Position = newPos,
                    QueuedPath = queuedPath
                });

                // Update session position
                if (_sessions.TryGetValue(connectionId, out var session))
                {
                    session.Position = newPos;
                }
            }
            else
            {
                // Blocked: clear the queue
                while (queue.TryDequeue(out _)) ;
                world.UpdateEntity(player with { QueuedPath = new List<Point>() });
            }
        }
    }

    public void ProcessAttack(WorldState world, string attackerId, string targetId, bool isMelee)
    {
        var attacker = world.GetEntity(attackerId);
        var target = world.GetEntity(targetId);
        if (attacker is null || target is null) return;

        // Get attack stats: from cache for players, from MonsterStats for monsters
        var (strength, dexterity) = GetAttackStats(attacker);

        // Calculate damage from attributes
        int damage = isMelee
            ? ProgressionFormulas.MeleeDamage(strength)
            : ProgressionFormulas.RangedDamage(dexterity);

        var targetPosition = target.Position;
        var newHealth = target.Health - damage;

        if (newHealth <= 0)
        {
            world.RemoveEntity(targetId);
            _logger.LogInformation("Entity {TargetId} died", targetId);

            // Award XP to all players in this instance if a monster was killed
            if (target.Type == EntityType.Monster && attacker.Type == EntityType.Player)
            {
                AwardXpToInstance(world, targetPosition);
            }
        }
        else
        {
            world.UpdateEntity(target with { Health = newHealth });
            _logger.LogInformation("Entity {AttackerId} attacked {TargetId} for {Damage} damage. Health: {Health}",
                attackerId, targetId, damage, newHealth);
        }

        // Record attack event for client animation
        var attackEvent = new AttackEvent(attackerId, targetId, damage, isMelee, targetPosition);
        var attackBag = _worldAttackEvents.GetOrAdd(world.Id, _ => []);
        attackBag.Add(attackEvent);
    }

    private (int Strength, int Dexterity) GetAttackStats(Entity attacker)
    {
        if (attacker.Type == EntityType.Monster)
        {
            return (MonsterStats.GetStrength(attacker.Name), MonsterStats.GetDexterity(attacker.Name));
        }

        // For players, get from cache
        if (_sessions.TryGetValue(attacker.Id, out var session))
        {
            using var scope = _scopeFactory.CreateScope();
            var cache = scope.ServiceProvider.GetRequiredService<ICharacterCache>();
            var progression = cache.GetProgressionAsync(session.CharacterId).GetAwaiter().GetResult();
            if (progression != null)
            {
                return (progression.Strength, progression.Dexterity);
            }
        }

        // Fallback to base stats
        return (ProgressionFormulas.BaseStrength, ProgressionFormulas.BaseDexterity);
    }

    public void ProcessRangedAttack(string connectionId, string targetId)
    {
        // Find player's current world
        if (!_sessions.TryGetValue(connectionId, out var session))
            return;

        if (!_worlds.TryGetValue(session.CurrentWorldId, out var world))
            return;

        ProcessAttack(world, connectionId, targetId, isMelee: false);
    }

    private void AwardXpToInstance(WorldState world, Point killedPosition)
    {
        var players = world.GetPlayers().ToList();

        foreach (var player in players)
        {
            // Need session to get character ID for cache lookup
            if (!_sessions.TryGetValue(player.Id, out var session)) continue;

            // Get current progression from cache
            using var scope = _scopeFactory.CreateScope();
            var cache = scope.ServiceProvider.GetRequiredService<ICharacterCache>();
            var progression = cache.GetProgressionAsync(session.CharacterId).GetAwaiter().GetResult();
            if (progression == null) continue;

            // Skip players at max level
            if (progression.Level >= ProgressionFormulas.MaxLevel) continue;

            var newXp = progression.Experience + ProgressionFormulas.XpPerKill;
            var newLevel = progression.Level;
            var newUnspent = progression.UnspentPoints;
            var newHealth = player.Health;
            var leveledUp = false;

            // Check for level up(s) - loop handles multiple level-ups from large XP gains
            while (newLevel < ProgressionFormulas.MaxLevel &&
                   newXp >= ProgressionFormulas.ExperienceForLevel(newLevel + 1))
            {
                newLevel++;
                newUnspent += ProgressionFormulas.PointsPerLevel;
                leveledUp = true;
            }

            // Cap XP at max level threshold
            if (newLevel >= ProgressionFormulas.MaxLevel)
            {
                newXp = ProgressionFormulas.ExperienceForLevel(ProgressionFormulas.MaxLevel);
            }

            // Full heal on level up
            var newMaxHealth = ProgressionFormulas.MaxHealth(progression.Stamina);
            if (leveledUp)
            {
                newHealth = newMaxHealth;
                RecordLevelUpEvent(world.Id, player.Id, newLevel, player.Position);
                _logger.LogInformation("Player {ConnectionId} leveled up to {Level}", player.Id, newLevel);
            }

            // Record XP gain event for this player
            RecordXpGainEvent(world.Id, player.Id, ProgressionFormulas.XpPerKill, killedPosition);

            // Update entity (only volatile fields: Level, Health, MaxHealth)
            var updatedEntity = player with
            {
                Level = newLevel,
                Health = newHealth,
                MaxHealth = newMaxHealth
            };
            world.UpdateEntity(updatedEntity);

            // Record progression update for this player
            var progressionUpdate = new ProgressionUpdate(
                newLevel,
                newXp,
                progression.Strength,
                progression.Dexterity,
                progression.Stamina,
                newUnspent,
                newMaxHealth
            );
            _pendingProgressionUpdates[player.Id] = progressionUpdate;

            // Queue persistence operation for progression data
            var charId = session.CharacterId;
            var xp = newXp;
            var level = newLevel;
            var str = progression.Strength;
            var dex = progression.Dexterity;
            var sta = progression.Stamina;
            var unspent = newUnspent;

            _pendingPersistenceOps.Enqueue(new PersistenceOp(charId, async svc =>
                await svc.UpdateProgressionAsync(charId, xp, level, str, dex, sta, unspent)));
        }
    }

    private void RecordXpGainEvent(WorldId worldId, string connectionId, int amount, Point position)
    {
        var playerXpEvents = _worldPlayerXpEvents.GetOrAdd(worldId, _ => new ConcurrentDictionary<string, List<XpGainEvent>>());
        var xpList = playerXpEvents.GetOrAdd(connectionId, _ => []);
        lock (xpList)
        {
            xpList.Add(new XpGainEvent(amount, position));
        }
    }

    private void RecordLevelUpEvent(WorldId worldId, string playerId, int newLevel, Point position)
    {
        var levelUpBag = _worldLevelUpEvents.GetOrAdd(worldId, _ => []);
        levelUpBag.Add(new LevelUpEvent(playerId, newLevel, position));
    }

    public void AllocateStat(string connectionId, StatType stat)
    {
        var (world, player) = FindPlayer(connectionId);
        if (world == null || player == null) return;

        // Need session to get character ID for cache lookup
        if (!_sessions.TryGetValue(connectionId, out var session)) return;

        // Get current progression from cache
        using var scope = _scopeFactory.CreateScope();
        var cache = scope.ServiceProvider.GetRequiredService<ICharacterCache>();
        var progression = cache.GetProgressionAsync(session.CharacterId).GetAwaiter().GetResult();
        if (progression == null || progression.UnspentPoints <= 0) return;

        // Calculate new stats
        var newStr = progression.Strength;
        var newDex = progression.Dexterity;
        var newSta = progression.Stamina;
        var newUnspent = progression.UnspentPoints - 1;

        switch (stat)
        {
            case StatType.Strength:
                newStr++;
                break;
            case StatType.Dexterity:
                newDex++;
                break;
            case StatType.Stamina:
                newSta++;
                break;
            default:
                return;
        }

        var newMaxHealth = ProgressionFormulas.MaxHealth(newSta);
        var newHealth = stat == StatType.Stamina
            ? player.Health + ProgressionFormulas.HealthPerStamina
            : player.Health;

        // Update entity (only Health/MaxHealth change for Stamina allocation)
        if (stat == StatType.Stamina)
        {
            world.UpdateEntity(player with { Health = newHealth, MaxHealth = newMaxHealth });
        }

        _logger.LogInformation("Player {ConnectionId} allocated point to {Stat}. New value: {Value}",
            connectionId, stat, stat switch
            {
                StatType.Strength => newStr,
                StatType.Dexterity => newDex,
                StatType.Stamina => newSta,
                _ => 0
            });

        // Record progression update for this player
        var progressionUpdate = new ProgressionUpdate(
            progression.Level,
            progression.Experience,
            newStr,
            newDex,
            newSta,
            newUnspent,
            newMaxHealth
        );
        _pendingProgressionUpdates[connectionId] = progressionUpdate;

        // Queue persistence operation for stats
        var charId = session.CharacterId;
        var xp = progression.Experience;
        var level = progression.Level;

        _pendingPersistenceOps.Enqueue(new PersistenceOp(charId, async svc =>
            await svc.UpdateProgressionAsync(charId, xp, level, newStr, newDex, newSta, newUnspent)));
    }

    private (WorldState? world, Entity? player) FindPlayer(string connectionId)
    {
        return FindPlayerInternal(connectionId);
    }

    private (WorldState? world, Entity? player) FindPlayerInternal(string connectionId)
    {
        if (!_sessions.TryGetValue(connectionId, out var session))
            return (null, null);

        if (!_worlds.TryGetValue(session.CurrentWorldId, out var world))
            return (null, null);

        var entity = world.GetEntity(connectionId);
        return (world, entity);
    }

    private async Task Broadcast()
    {
        // Group sessions by world and send world-specific snapshots
        var sessionsByWorld = _sessions.Values
            .GroupBy(s => s.CurrentWorldId)
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var (worldId, sessions) in sessionsByWorld)
        {
            if (!_worlds.TryGetValue(worldId, out var world)) continue;

            // Get and clear attack events for this world
            var attackEvents = _worldAttackEvents.TryRemove(worldId, out var attackBag)
                ? attackBag.ToList()
                : [];

            // Get and clear level-up events for this world
            var levelUpEvents = _worldLevelUpEvents.TryRemove(worldId, out var levelUpBag)
                ? levelUpBag.ToList()
                : [];

            // Create snapshot without tiles (for players who already have them)
            var snapshotWithoutTiles = world.ToSnapshot(_tick, includeTiles: false, attackEvents, levelUpEvents);
            // Create snapshot with tiles (for players who need them)
            WorldSnapshot? snapshotWithTiles = null;

            foreach (var session in sessions)
            {
                // Only include tiles if we haven't sent this world's tiles before
                bool needsTiles = !session.SentTilesWorldIds.Contains(worldId);

                if (needsTiles)
                {
                    // Lazily create the full snapshot only if needed
                    snapshotWithTiles ??= world.ToSnapshot(_tick, includeTiles: true, attackEvents, levelUpEvents);
                    _logger.LogInformation("Sending tiles to {ConnectionId}, count: {Count}",
                        session.ConnectionId, snapshotWithTiles.Tiles?.Count ?? 0);
                    session.SentTilesWorldIds.Add(worldId);
                    await _hubContext.Clients.Client(session.ConnectionId).OnWorldUpdate(snapshotWithTiles);
                }
                else
                {
                    await _hubContext.Clients.Client(session.ConnectionId).OnWorldUpdate(snapshotWithoutTiles);
                }

                // Send XP events individually to this player
                await SendXpEventsToPlayer(worldId, session.ConnectionId);

                // Send progression update if one is pending
                await SendProgressionUpdateToPlayer(session.ConnectionId);
            }

            // Clear XP events for this world after sending
            _worldPlayerXpEvents.TryRemove(worldId, out _);
        }
    }

    private async Task SendProgressionUpdateToPlayer(string connectionId)
    {
        if (_pendingProgressionUpdates.TryRemove(connectionId, out var update))
        {
            await _hubContext.Clients.Client(connectionId).OnProgressionUpdate(update);
        }
    }

    private async Task SendXpEventsToPlayer(WorldId worldId, string connectionId)
    {
        if (!_worldPlayerXpEvents.TryGetValue(worldId, out var playerXpEvents)) return;
        if (!playerXpEvents.TryGetValue(connectionId, out var xpEvents)) return;

        List<XpGainEvent> eventsToSend;
        lock (xpEvents)
        {
            if (xpEvents.Count == 0) return;
            eventsToSend = [.. xpEvents];
            xpEvents.Clear();
        }

        await _hubContext.Clients.Client(connectionId).OnXpGain(eventsToSend);
    }
}
