using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using Mud.Server.Hubs;
using Mud.Shared;

namespace Mud.Server.Services;

public class GameLoopService : BackgroundService
{
    private readonly IHubContext<GameHub> _hubContext;
    private readonly ILogger<GameLoopService> _logger;
    
    private readonly ConcurrentDictionary<string, Player> _players = new();
    private readonly ConcurrentDictionary<string, ConcurrentQueue<Direction>> _playerInputQueues = new();
    private readonly HashSet<Point> _walls = new();
    private long _tick = 0;

    public GameLoopService(IHubContext<GameHub> hubContext, ILogger<GameLoopService> logger)
    {
        _hubContext = hubContext;
        _logger = logger;
        GenerateInitialWalls();
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
        _players.TryAdd(id, new Player { Id = id, Name = name, Position = new Point(0, 0) });
    }

    public void RemovePlayer(string id)
    {
        _players.TryRemove(id, out _);
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
        foreach (var playerId in _players.Keys)
        {
            if (!_playerInputQueues.TryGetValue(playerId, out var queue) || !queue.TryDequeue(out var direction))
            {
                continue;
            }

            if (_players.TryGetValue(playerId, out var player))
            {
                var newPos = direction switch
                {
                    Direction.Up => player.Position with { Y = player.Position.Y - 1 },
                    Direction.Down => player.Position with { Y = player.Position.Y + 1 },
                    Direction.Left => player.Position with { X = player.Position.X - 1 },
                    Direction.Right => player.Position with { X = player.Position.X + 1 },
                    _ => player.Position
                };

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
                        queuedPath.Add(currentPos);
                    }

                    _players[playerId] = player with 
                    { 
                        Position = newPos,
                        QueuedPath = queuedPath
                    };
                }
                else
                {
                    // Blocked: clear the queue
                    while (queue.TryDequeue(out _)) ;
                    _players[playerId] = player with { QueuedPath = new List<Point>() };
                }
            }
        }
    }

    private async Task Broadcast()
    {
        var snapshot = new WorldSnapshot
        {
            Tick = _tick,
            Players = _players.Values.ToList(),
            Walls = _walls.ToList()
        };

        await _hubContext.Clients.All.SendAsync("OnWorldUpdate", snapshot);
    }
}
