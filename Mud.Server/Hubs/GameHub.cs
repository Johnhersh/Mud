using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Mud.Core;
using Mud.Core.Services;
using Mud.Server.Services;

namespace Mud.Server.Hubs;

[Authorize]
public class GameHub : Hub<IGameClient>, IGameHub
{
    private readonly GameLoopService _gameLoop;
    private readonly ISessionManager _sessionManager;
    private readonly IPersistenceService _persistenceService;
    private readonly ILogger<GameHub> _logger;

    // For backward compatibility during transition
    private PlayerId PlayerId => new(Context.ConnectionId);

    public GameHub(
        GameLoopService gameLoop,
        ISessionManager sessionManager,
        IPersistenceService persistenceService,
        ILogger<GameHub> logger)
    {
        _gameLoop = gameLoop;
        _sessionManager = sessionManager;
        _persistenceService = persistenceService;
        _logger = logger;
    }

    public async Task Join(string name)
    {
        var accountIdClaim = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (string.IsNullOrEmpty(accountIdClaim))
        {
            // Fallback for unauthenticated (shouldn't happen with [Authorize])
            _gameLoop.AddPlayer(PlayerId, name);
            return;
        }

        var accountId = new AccountId(accountIdClaim);

        // Load or create character
        var characterData = await _persistenceService.LoadCharacterAsync(accountId);
        if (characterData == null)
        {
            // Character should have been created during registration
            _logger.LogWarning("No character found for account {AccountId}, creating one", accountId.Value);
            characterData = await _persistenceService.CreateCharacterAsync(accountId, name);
        }

        // Register session (kicks existing if any)
        var kickedConnectionId = _sessionManager.RegisterSession(Context.ConnectionId, accountId, characterData.Id);
        if (kickedConnectionId != null)
        {
            _logger.LogInformation("Kicked existing session {KickedId} for account {AccountId}",
                kickedConnectionId, accountId.Value);
            // Could notify the kicked connection here
        }

        // Add player to game with persisted data
        _gameLoop.AddPlayerFromPersistence(PlayerId, characterData);
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        // Get character ID before removing session
        var characterId = _sessionManager.GetCharacterId(Context.ConnectionId);

        // Remove session
        _sessionManager.RemoveSession(Context.ConnectionId);

        // Save and remove player from game loop
        await _gameLoop.RemovePlayerAsync(PlayerId, characterId);

        await base.OnDisconnectedAsync(exception);
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
