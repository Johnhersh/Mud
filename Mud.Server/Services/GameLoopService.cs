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

    private readonly GameState _state = new();

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

        foreach (var session in _state.Sessions.Values)
        {
            var (world, entity) = FindPlayerInternal(session.ConnectionId);
            if (entity == null) continue;

            // Determine overworld position for resume
            var (overworldX, overworldY) = session.CurrentWorldId == _state.Overworld.Id
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
        _state.Overworld = WorldGenerator.GenerateOverworld(WorldConfig.WorldSeed);
        _state.Worlds[_state.Overworld.Id] = _state.Overworld;
        _logger.LogInformation("Overworld generated: {Width}x{Height} with {POICount} POIs",
            _state.Overworld.Terrain.Width, _state.Overworld.Terrain.Height, _state.Overworld.POIs.Count);
    }

    private static void SpawnMonsters(WorldState world, int count)
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
    /// Add a player from persisted data. Returns kicked connection ID if concurrent login detected.
    /// </summary>
    public string? AddPlayerFromPersistence(string connectionId, AccountId accountId, CharacterData characterData)
    {
        // Check for existing session with same account (concurrent login)
        string? kickedConnectionId = null;
        var existingSession = _state.Sessions.Values.FirstOrDefault(s => s.AccountId == accountId);
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
            : _state.Overworld.Id;

        if (_state.Worlds.ContainsKey(savedWorldId) && savedWorldId != _state.Overworld.Id)
        {
            // Instance still exists, spawn there
            spawnWorld = savedWorldId;
            spawnPos = new Point(characterData.PositionX, characterData.PositionY);
        }
        else
        {
            // Spawn at last overworld position (or town if invalid)
            spawnWorld = _state.Overworld.Id;
            if (characterData.LastOverworldX > 0 || characterData.LastOverworldY > 0)
            {
                spawnPos = new Point(characterData.LastOverworldX, characterData.LastOverworldY);
            }
            else
            {
                var spawnTown = _state.Overworld.POIs.FirstOrDefault(p => p.Type == POIType.Town);
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
        _state.Sessions[connectionId] = session;

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

        if (_state.Worlds.TryGetValue(spawnWorld, out var world))
        {
            world.AddEntity(entity);
        }

        _logger.LogInformation("Player {Name} (Character {CharacterId}) joined at {Position} in {World}",
            characterData.Name, characterData.Id.Value, spawnPos, spawnWorld);

        return kickedConnectionId;
    }

    public void RemovePlayer(string connectionId)
    {
        _state.RemovePlayer(connectionId);
    }

    /// <summary>
    /// Remove player and save their state to the database.
    /// </summary>
    public async Task RemovePlayerAsync(string connectionId)
    {
        if (_state.Sessions.TryGetValue(connectionId, out var session))
        {
            // Get entity data before removal
            var (world, entity) = FindPlayerInternal(connectionId);

            // Save state if we have entity
            if (entity != null)
            {
                // Determine overworld position
                var (overworldX, overworldY) = session.CurrentWorldId == _state.Overworld.Id
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
        return _state.Sessions.TryGetValue(connectionId, out var session) ? session : null;
    }

    public WorldState? GetWorld(WorldId worldId)
    {
        return _state.Worlds.TryGetValue(worldId, out var world) ? world : null;
    }

    public void EnqueueInput(string connectionId, Direction direction)
    {
        var queue = _state.PlayerInputQueues.GetOrAdd(connectionId, _ => new ConcurrentQueue<Direction>());
        if (queue.Count < 5)
        {
            queue.Enqueue(direction);
        }
    }

    public void Interact(string connectionId)
    {
        if (!_state.Sessions.TryGetValue(connectionId, out var session))
            return;

        if (!_state.Worlds.TryGetValue(session.CurrentWorldId, out var world))
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
        if (!_state.Worlds.TryGetValue(instanceId, out var instance))
        {
            instance = WorldGenerator.GenerateInstance(poi, overworld.Terrain, WorldConfig.WorldSeed);
            _state.Worlds[instanceId] = instance;

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
        _state.Overworld.AddEntity(newEntity);

        // Update session
        session.TransferToWorld(_state.Overworld.Id, session.LastOverworldPosition);
        _logger.LogInformation("Player {ConnectionId} exited to overworld", session.ConnectionId);

        // Clean up instance if empty
        if (!instance.GetPlayers().Any())
        {
            _state.Worlds.TryRemove(instance.Id, out _);
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
        using var scope = _scopeFactory.CreateScope();
        var cache = scope.ServiceProvider.GetRequiredService<ICharacterCache>();

        foreach (var world in _state.Worlds.Values)
        {
            world.UpdateWorld(_state, cache);
        }
    }

    public void ProcessRangedAttack(string connectionId, string targetId)
    {
        if (!_state.Sessions.TryGetValue(connectionId, out var session))
            return;

        if (!_state.Worlds.TryGetValue(session.CurrentWorldId, out var world))
            return;

        using var scope = _scopeFactory.CreateScope();
        var cache = scope.ServiceProvider.GetRequiredService<ICharacterCache>();
        world.ProcessAttack(_state, connectionId, targetId, isMelee: false, cache);
    }

    public void AllocateStat(string connectionId, StatType stat)
    {
        var (world, player) = FindPlayer(connectionId);
        if (world == null || player == null) return;

        if (!_state.Sessions.TryGetValue(connectionId, out var session)) return;

        using var scope = _scopeFactory.CreateScope();
        var cache = scope.ServiceProvider.GetRequiredService<ICharacterCache>();
        var progression = cache.GetProgressionAsync(session.CharacterId).GetAwaiter().GetResult();
        if (progression == null || progression.UnspentPoints <= 0) return;

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

        var progressionUpdate = new ProgressionUpdate(
            progression.Level,
            progression.Experience,
            newStr,
            newDex,
            newSta,
            newUnspent,
            newMaxHealth
        );
        _state.ProgressionUpdates[connectionId] = progressionUpdate;

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
        if (!_state.Sessions.TryGetValue(connectionId, out var session))
            return (null, null);

        if (!_state.Worlds.TryGetValue(session.CurrentWorldId, out var world))
            return (null, null);

        var entity = world.GetEntity(connectionId);
        return (world, entity);
    }

    private async Task Broadcast()
    {
        // Group sessions by world and send world-specific snapshots
        var sessionsByWorld = _state.Sessions.Values
            .GroupBy(s => s.CurrentWorldId)
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var (worldId, sessions) in sessionsByWorld)
        {
            if (!_state.Worlds.TryGetValue(worldId, out var world)) continue;

            // Get and clear attack events for this world
            var attackEvents = _state.AttackEvents.TryRemove(worldId, out var attackBag)
                ? attackBag.ToList()
                : [];

            // Get and clear level-up events for this world
            var levelUpEvents = _state.LevelUpEvents.TryRemove(worldId, out var levelUpBag)
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
            _state.XpEvents.TryRemove(worldId, out _);
        }
    }

    private async Task SendProgressionUpdateToPlayer(string connectionId)
    {
        if (_state.ProgressionUpdates.TryRemove(connectionId, out var update))
        {
            await _hubContext.Clients.Client(connectionId).OnProgressionUpdate(update);
        }
    }

    private async Task SendXpEventsToPlayer(WorldId worldId, string connectionId)
    {
        if (!_state.XpEvents.TryGetValue(worldId, out var playerXpEvents)) return;
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
