using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Mud.Shared;

namespace Mud.Client.Services;

public class GameClient : IAsyncDisposable
{
    private readonly HubConnection _hubConnection;
    public event Action<WorldSnapshot>? OnWorldUpdate;

    public GameClient(string baseUri)
    {
        _hubConnection = new HubConnectionBuilder()
            .WithUrl(new Uri(new Uri(baseUri), "/gamehub"))
            .AddMessagePackProtocol()
            .WithAutomaticReconnect()
            .Build();

        _hubConnection.On<WorldSnapshot>("OnWorldUpdate", snapshot =>
        {
            OnWorldUpdate?.Invoke(snapshot);
        });
    }

    public string? ConnectionId => _hubConnection.ConnectionId;

    public async Task StartAsync()
    {
        await _hubConnection.StartAsync();
    }

    public async Task JoinAsync(string name)
    {
        await _hubConnection.SendAsync("Join", name);
    }

    public async Task MoveAsync(Direction direction)
    {
        await _hubConnection.SendAsync("Move", direction);
    }

    public async Task RangedAttackAsync(string targetId)
    {
        await _hubConnection.SendAsync("RangedAttack", targetId);
    }

    public async ValueTask DisposeAsync()
    {
        await _hubConnection.DisposeAsync();
    }
}
