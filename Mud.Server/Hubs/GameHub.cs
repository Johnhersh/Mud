using Microsoft.AspNetCore.SignalR;
using Mud.Shared;
using Mud.Server.Services;

namespace Mud.Server.Hubs;

public class GameHub : Hub
{
    private readonly GameLoopService _gameLoop;

    public GameHub(GameLoopService gameLoop)
    {
        _gameLoop = gameLoop;
    }

    public async Task Join(string name)
    {
        _gameLoop.AddPlayer(Context.ConnectionId, name);
        await Task.CompletedTask;
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _gameLoop.RemovePlayer(Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }

    public async Task Move(Direction direction)
    {
        _gameLoop.EnqueueInput(Context.ConnectionId, direction);
        await Task.CompletedTask;
    }

    public async Task RangedAttack(string targetId)
    {
        _gameLoop.ProcessAttack(Context.ConnectionId, targetId);
        await Task.CompletedTask;
    }
}
