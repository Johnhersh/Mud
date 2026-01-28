using Microsoft.AspNetCore.SignalR;
using Mud.Shared;
using Mud.Server.Services;

namespace Mud.Server.Hubs;

public class GameHub : Hub
{
    private readonly GameLoopService _gameLoop;

    private PlayerId PlayerId => new(Context.ConnectionId);

    public GameHub(GameLoopService gameLoop)
    {
        _gameLoop = gameLoop;
    }

    public void Join(string name) =>
        _gameLoop.AddPlayer(PlayerId, name);

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        _gameLoop.RemovePlayer(PlayerId);
        return base.OnDisconnectedAsync(exception);
    }

    public void Move(Direction direction) =>
        _gameLoop.EnqueueInput(PlayerId, direction);

    public void RangedAttack(string targetId) =>
        _gameLoop.ProcessRangedAttack(PlayerId, targetId);

    public void Interact() =>
        _gameLoop.Interact(PlayerId);
}
