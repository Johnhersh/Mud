using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Mud.Shared;
using TypedSignalR.Client;

namespace Mud.Client.Services;

public class GameClient : IAsyncDisposable, IGameClient
{
    private readonly HubConnection _hubConnection;
    private readonly IGameHub _hub;
    private readonly IDisposable _subscription;

    public event Action<WorldSnapshot>? OnWorldUpdate;
    public event Action<List<XpGainEvent>>? OnXpGain;

    public GameClient(string baseUri)
    {
        _hubConnection = new HubConnectionBuilder()
            .WithUrl(new Uri(new Uri(baseUri), "/gamehub"))
            .AddMessagePackProtocol()
            .WithAutomaticReconnect()
            .Build();

        _hub = _hubConnection.CreateHubProxy<IGameHub>();
        _subscription = _hubConnection.Register<IGameClient>(this);
    }

    public string? ConnectionId => _hubConnection.ConnectionId;

    // IGameClient implementation - called by server
    Task IGameClient.OnWorldUpdate(WorldSnapshot snapshot)
    {
        OnWorldUpdate?.Invoke(snapshot);
        return Task.CompletedTask;
    }

    Task IGameClient.OnXpGain(List<XpGainEvent> xpEvents)
    {
        OnXpGain?.Invoke(xpEvents);
        return Task.CompletedTask;
    }

    public async Task StartAsync()
    {
        await _hubConnection.StartAsync();
    }

    public Task JoinAsync(string name) => _hub.Join(name);

    public Task MoveAsync(Direction direction) => _hub.Move(direction);

    public Task RangedAttackAsync(string targetId) => _hub.RangedAttack(targetId);

    public Task InteractAsync() => _hub.Interact();

    public Task AllocateStatAsync(StatType stat) => _hub.AllocateStat(stat);

    public async ValueTask DisposeAsync()
    {
        _subscription.Dispose();
        await _hubConnection.DisposeAsync();
    }
}
