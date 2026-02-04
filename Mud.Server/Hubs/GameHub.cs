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
    private readonly IPersistenceService _persistenceService;
    private readonly ICharacterCache _characterCache;
    private readonly ILogger<GameHub> _logger;

    private string ConnectionId => Context.ConnectionId;

    public GameHub(
        GameLoopService gameLoop,
        IPersistenceService persistenceService,
        ICharacterCache characterCache,
        ILogger<GameHub> logger)
    {
        _gameLoop = gameLoop;
        _persistenceService = persistenceService;
        _characterCache = characterCache;
        _logger = logger;
    }

    public async Task Join(string name)
    {
        // [Authorize] on the hub should guarantee this claim exists
        var accountIdClaim = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? throw new InvalidOperationException("Authenticated user missing NameIdentifier claim");

        var accountId = new AccountId(accountIdClaim);

        // Load or create character
        var characterData = await _persistenceService.LoadCharacterAsync(accountId);
        if (characterData == null)
        {
            // Character should have been created during registration
            _logger.LogWarning("No character found for account {AccountId}, creating one", accountId.Value);
            characterData = await _persistenceService.CreateCharacterAsync(accountId, name);
        }

        // Pre-populate character cache for fast stat lookups
        _characterCache.Set(characterData.Id, new CharacterProgression
        {
            Level = characterData.Level,
            Experience = characterData.Experience,
            Strength = characterData.Strength,
            Dexterity = characterData.Dexterity,
            Stamina = characterData.Stamina,
            UnspentPoints = characterData.UnspentPoints
        });

        // Add player to game (kicks existing session if concurrent login)
        var kickedConnectionId = _gameLoop.AddPlayerFromPersistence(ConnectionId, accountId, characterData);
        if (kickedConnectionId != null)
        {
            _logger.LogInformation("Kicked existing session {KickedId} for account {AccountId}",
                kickedConnectionId, accountId.Value);
        }
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        await _gameLoop.RemovePlayerAsync(ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }

    public Task Move(Direction direction)
    {
        _gameLoop.EnqueueInput(ConnectionId, direction);
        return Task.CompletedTask;
    }

    public Task RangedAttack(string targetId)
    {
        _gameLoop.ProcessRangedAttack(ConnectionId, targetId);
        return Task.CompletedTask;
    }

    public Task Interact()
    {
        _gameLoop.Interact(ConnectionId);
        return Task.CompletedTask;
    }

    public Task AllocateStat(StatType stat)
    {
        _gameLoop.AllocateStat(ConnectionId, stat);
        return Task.CompletedTask;
    }
}
