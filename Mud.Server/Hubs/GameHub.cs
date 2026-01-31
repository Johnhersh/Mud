using Microsoft.AspNetCore.SignalR;
using Mud.Shared;
using Mud.Server.Services;

namespace Mud.Server.Hubs;

public class GameHub : Hub<IGameClient>, IGameHub
{
    private readonly GameLoopService _gameLoop;

    private PlayerId PlayerId => new(Context.ConnectionId);

    public GameHub(GameLoopService gameLoop)
    {
        _gameLoop = gameLoop;
    }

    public Task Join(string name)
    {
        _gameLoop.AddPlayer(PlayerId, name);
        return Task.CompletedTask;
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        _gameLoop.RemovePlayer(PlayerId);
        return base.OnDisconnectedAsync(exception);
    }

    public Task Move(Direction direction)
    {
        _gameLoop.EnqueueInput(PlayerId, direction);
        return Task.CompletedTask;
    }

    public Task RangedAttack(string targetId)
    {
        _gameLoop.ProcessRangedAttack(PlayerId, targetId);
        return Task.CompletedTask;
    }

    public Task Interact()
    {
        _gameLoop.Interact(PlayerId);
        return Task.CompletedTask;
    }

    public Task AllocateStat(StatType stat)
    {
        _gameLoop.AllocateStat(PlayerId, stat);
        return Task.CompletedTask;
    }
}
