using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using Mud.Server.Hubs;
using Mud.Server.World;
using Mud.Server.World.Generation;
using Mud.Shared;
using Mud.Shared.World;

namespace Mud.Server.Services;

public class GameLoopService : BackgroundService
{
    private readonly IHubContext<GameHub> _hubContext;
    private readonly ILogger<GameLoopService> _logger;

    // World management
    private readonly ConcurrentDictionary<string, WorldState> _worlds = new();
    private WorldState _overworld = null!;

    // Player tracking
    private readonly ConcurrentDictionary<PlayerId, PlayerState> _players = new();
    private readonly ConcurrentDictionary<PlayerId, ConcurrentQueue<Direction>> _playerInputQueues = new();

    // Attack events per world (cleared after each broadcast)
    private readonly ConcurrentDictionary<string, ConcurrentBag<AttackEvent>> _worldAttackEvents = new();

    private long _tick = 0;

    public GameLoopService(IHubContext<GameHub> hubContext, ILogger<GameLoopService> logger)
    {
        _hubContext = hubContext;
        _logger = logger;
        InitializeWorld();
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
                Type = EntityType.Monster,
                Health = 50,
                MaxHealth = 50
            };
            world.AddEntity(monster);
        }
    }

    public void AddPlayer(PlayerId playerId, string name)
    {
        // Spawn at town, or center of map if no town exists
        var spawnTown = _overworld.POIs.FirstOrDefault(p => p.Type == POIType.Town);
        var spawnPos = spawnTown?.Position ?? new Point(
            WorldConfig.OverworldWidth / 2,
            WorldConfig.OverworldHeight / 2
        );

        // Create player state
        var playerState = new PlayerState
        {
            Id = playerId,
            Name = name,
            CurrentWorldId = _overworld.Id,
            Position = spawnPos,
            LastOverworldPosition = spawnPos
        };
        _players[playerId] = playerState;

        // Create player entity in overworld
        var entity = new Entity
        {
            Id = playerId.Value,
            Name = name,
            Position = spawnPos,
            Type = EntityType.Player,
            Health = 100,
            MaxHealth = 100
        };
        _overworld.AddEntity(entity);
        _logger.LogInformation("Player {Name} joined at {Position}", name, spawnPos);
    }

    public void RemovePlayer(PlayerId playerId)
    {
        if (_players.TryRemove(playerId, out var playerState))
        {
            // Remove from current world
            if (_worlds.TryGetValue(playerState.CurrentWorldId, out var world))
            {
                world.RemoveEntity(playerId.Value);

                // Clean up instance if empty
                if (world.Type == WorldType.Instance && !world.GetPlayers().Any())
                {
                    _worlds.TryRemove(world.Id, out _);
                    _logger.LogInformation("Instance {WorldId} destroyed (empty)", world.Id);
                }
            }
        }
        _playerInputQueues.TryRemove(playerId, out _);
    }

    public PlayerState? GetPlayer(PlayerId playerId)
    {
        return _players.TryGetValue(playerId, out var player) ? player : null;
    }

    public WorldState? GetWorld(string worldId)
    {
        return _worlds.TryGetValue(worldId, out var world) ? world : null;
    }

    public void EnqueueInput(PlayerId playerId, Direction direction)
    {
        var queue = _playerInputQueues.GetOrAdd(playerId, _ => new ConcurrentQueue<Direction>());
        if (queue.Count < 5)
        {
            queue.Enqueue(direction);
        }
    }

    public void Interact(PlayerId playerId)
    {
        if (!_players.TryGetValue(playerId, out var playerState))
            return;

        if (!_worlds.TryGetValue(playerState.CurrentWorldId, out var world))
            return;

        var entity = world.GetEntity(playerId.Value);
        if (entity == null) return;

        if (world.Type == WorldType.Overworld)
        {
            // Check for POI
            var poi = world.GetPOIAt(entity.Position);
            if (poi != null)
            {
                EnterInstance(playerState, poi, world, entity);
            }
        }
        else // Instance
        {
            // Check for exit
            if (world.IsExitMarker(entity.Position))
            {
                ExitInstance(playerState, world, entity);
            }
        }
    }

    private void EnterInstance(PlayerState playerState, POI poi, WorldState overworld, Entity entity)
    {
        string instanceId = $"instance_{poi.Id}";

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
        playerState.SaveOverworldPosition();

        // Remove from overworld
        overworld.RemoveEntity(playerState.Id.Value);

        // Add to instance
        var newEntity = entity with { Position = spawnPos, QueuedPath = new List<Point>() };
        instance.AddEntity(newEntity);

        // Update player state
        playerState.TransferToWorld(instanceId, spawnPos);
        _logger.LogInformation("Player {PlayerId} entered instance {InstanceId}", playerState.Id, instanceId);
    }

    private void ExitInstance(PlayerState playerState, WorldState instance, Entity entity)
    {
        // Remove from instance
        instance.RemoveEntity(playerState.Id.Value);

        // Add back to overworld
        var newEntity = entity with
        {
            Position = playerState.LastOverworldPosition,
            QueuedPath = new List<Point>()
        };
        _overworld.AddEntity(newEntity);

        // Update player state
        playerState.TransferToWorld(_overworld.Id, playerState.LastOverworldPosition);
        _logger.LogInformation("Player {PlayerId} exited to overworld", playerState.Id);

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

            _tick++;

            var elapsed = DateTime.UtcNow - startTime;
            var delay = TimeSpan.FromMilliseconds(300) - elapsed;
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, stoppingToken);
            }
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
            var playerId = new PlayerId(playerEntity.Id);
            if (!_playerInputQueues.TryGetValue(playerId, out var queue) || !queue.TryDequeue(out var direction))
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
                world.UpdateEntity(player with { QueuedPath = new List<Point>() });
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

                // Update player state position
                if (_players.TryGetValue(playerId, out var pState))
                {
                    pState.Position = newPos;
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
        if (attacker == null || target == null) return;

        int damage = 10;
        var targetPosition = target.Position; // Capture before potential removal

        var newTarget = target with { Health = target.Health - damage };

        if (newTarget.Health <= 0)
        {
            world.RemoveEntity(targetId);
            _logger.LogInformation("Entity {TargetId} died", targetId);
        }
        else
        {
            world.UpdateEntity(newTarget);
            _logger.LogInformation("Entity {AttackerId} attacked {TargetId} for {Damage} damage. Health: {Health}",
                attackerId, targetId, damage, newTarget.Health);
        }

        // Record attack event for client animation
        var attackEvent = new AttackEvent(attackerId, targetId, damage, isMelee, targetPosition);
        var attackBag = _worldAttackEvents.GetOrAdd(world.Id, _ => new ConcurrentBag<AttackEvent>());
        attackBag.Add(attackEvent);
    }

    public void ProcessRangedAttack(PlayerId attackerId, string targetId)
    {
        // Find player's current world
        if (!_players.TryGetValue(attackerId, out var playerState))
            return;

        if (!_worlds.TryGetValue(playerState.CurrentWorldId, out var world))
            return;

        ProcessAttack(world, attackerId.Value, targetId, isMelee: false);
    }

    private async Task Broadcast()
    {
        // Group players by world and send world-specific snapshots
        var playersByWorld = _players.Values
            .GroupBy(p => p.CurrentWorldId)
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var (worldId, players) in playersByWorld)
        {
            if (!_worlds.TryGetValue(worldId, out var world)) continue;

            // Get and clear attack events for this world
            List<AttackEvent>? attackEvents = null;
            if (_worldAttackEvents.TryRemove(worldId, out var attackBag))
            {
                attackEvents = attackBag.ToList();
            }

            // Create snapshot without tiles (for players who already have them)
            var snapshotWithoutTiles = world.ToSnapshot(_tick, includeTiles: false, attackEvents);
            // Create snapshot with tiles (for players who need them)
            WorldSnapshot? snapshotWithTiles = null;

            foreach (var playerState in players)
            {
                // Only include tiles if we haven't sent this world's tiles before
                bool needsTiles = !playerState.SentTilesWorldIds.Contains(worldId);

                if (needsTiles)
                {
                    // Lazily create the full snapshot only if needed
                    snapshotWithTiles ??= world.ToSnapshot(_tick, includeTiles: true, attackEvents);
                    _logger.LogInformation("Sending tiles to {PlayerId}, count: {Count}",
                        playerState.Id, snapshotWithTiles.Tiles?.Count ?? 0);
                    playerState.SentTilesWorldIds.Add(worldId);
                    await _hubContext.Clients.Client(playerState.Id.Value).SendAsync("OnWorldUpdate", snapshotWithTiles);
                }
                else
                {
                    await _hubContext.Clients.Client(playerState.Id.Value).SendAsync("OnWorldUpdate", snapshotWithoutTiles);
                }
            }
        }
    }
}
