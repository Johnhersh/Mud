using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using Mud.Server.Hubs;
using Mud.Shared;

namespace Mud.Server.Services;

public class GameLoopService : BackgroundService
{
    private readonly IHubContext<GameHub> _hubContext;
    private readonly ILogger<GameLoopService> _logger;
    
    private readonly ConcurrentDictionary<string, Entity> _entities = new();
    private readonly ConcurrentDictionary<string, ConcurrentQueue<Direction>> _playerInputQueues = new();
    private readonly HashSet<Point> _walls = new();
    private long _tick = 0;
    private const string MonsterId = "monster_1";

    public GameLoopService(IHubContext<GameHub> hubContext, ILogger<GameLoopService> logger)
    {
        _hubContext = hubContext;
        _logger = logger;
        GenerateInitialWalls();
        SpawnMonster();
    }

    private void SpawnMonster()
    {
        var random = new Random();
        Point pos;
        do
        {
            pos = new Point(random.Next(-20, 20), random.Next(-20, 20));
        } while (_walls.Contains(pos));

        var monster = new Entity
        {
            Id = MonsterId,
            Name = "Goblin",
            Position = pos,
            Type = EntityType.Monster,
            Health = 50,
            MaxHealth = 50
        };
        _entities.TryAdd(MonsterId, monster);
    }

    private void GenerateInitialWalls()
    {
        var random = new Random();
        for (int i = 0; i < 50; i++)
        {
            _walls.Add(new Point(random.Next(-20, 20), random.Next(-20, 20)));
        }
    }

    public void AddPlayer(string id, string name)
    {
        _entities.TryAdd(id, new Entity 
        { 
            Id = id, 
            Name = name, 
            Position = new Point(0, 0),
            Type = EntityType.Player,
            Health = 100,
            MaxHealth = 100
        });
    }

    public void RemovePlayer(string id)
    {
        _entities.TryRemove(id, out _);
    }

    public void EnqueueInput(string playerId, Direction direction)
    {
        var queue = _playerInputQueues.GetOrAdd(playerId, _ => new ConcurrentQueue<Direction>());
        if (queue.Count < 5)
        {
            queue.Enqueue(direction);
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
            var delay = TimeSpan.FromMilliseconds(500) - elapsed;
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, stoppingToken);
            }
        }
    }

    private void Update()
    {
        var players = _entities.Values.Where(e => e.Type == EntityType.Player).ToList();
        foreach (var playerEntity in players)
        {
            var playerId = playerEntity.Id;
            if (!_playerInputQueues.TryGetValue(playerId, out var queue) || !queue.TryDequeue(out var direction))
            {
                continue;
            }

            if (_entities.TryGetValue(playerId, out var player))
            {
                var newPos = direction switch
                {
                    Direction.Up => player.Position with { Y = player.Position.Y - 1 },
                    Direction.Down => player.Position with { Y = player.Position.Y + 1 },
                    Direction.Left => player.Position with { X = player.Position.X - 1 },
                    Direction.Right => player.Position with { X = player.Position.X + 1 },
                    _ => player.Position
                };

                // Check for monster at destination
                var target = _entities.Values.FirstOrDefault(e => e.Position == newPos && e.Type == EntityType.Monster);
                if (target != null)
                {
                    ProcessAttack(playerId, target.Id);
                    // Clear queue on attack
                    while (queue.TryDequeue(out _)) ;
                    _entities[playerId] = player with { QueuedPath = new List<Point>() };
                    continue;
                }

                if (!_walls.Contains(newPos))
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
                        
                        if (_walls.Contains(currentPos)) break;
                        // Also break if there's a monster
                        if (_entities.Values.Any(e => e.Position == currentPos && e.Type == EntityType.Monster)) break;

                        queuedPath.Add(currentPos);
                    }

                    _entities[playerId] = player with 
                    { 
                        Position = newPos,
                        QueuedPath = queuedPath
                    };
                }
                else
                {
                    // Blocked: clear the queue
                    while (queue.TryDequeue(out _)) ;
                    _entities[playerId] = player with { QueuedPath = new List<Point>() };
                }
            }
        }
    }

    public void ProcessAttack(string attackerId, string targetId)
    {
        if (!_entities.TryGetValue(attackerId, out var attacker) || !_entities.TryGetValue(targetId, out var target))
            return;

        int damage = 10;
        var newTarget = target with { Health = target.Health - damage };

        if (newTarget.Health <= 0)
        {
            // Respawn monster
            var random = new Random();
            Point pos;
            do
            {
                pos = new Point(random.Next(-20, 20), random.Next(-20, 20));
            } while (_walls.Contains(pos) || _entities.Values.Any(e => e.Position == pos));

            _entities[targetId] = newTarget with { Health = target.MaxHealth, Position = pos };
            _logger.LogInformation("Entity {TargetId} died and respawned at {Pos}", targetId, pos);
        }
        else
        {
            _entities[targetId] = newTarget;
            _logger.LogInformation("Entity {AttackerId} attacked {TargetId} for {Damage} damage. Health: {Health}", attackerId, targetId, damage, newTarget.Health);
        }
    }

    private async Task Broadcast()
    {
        var snapshot = new WorldSnapshot
        {
            Tick = _tick,
            Entities = _entities.Values.ToList(),
            Walls = _walls.ToList()
        };

        await _hubContext.Clients.All.SendAsync("OnWorldUpdate", snapshot);
    }
}
