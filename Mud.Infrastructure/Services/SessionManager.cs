using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Mud.Core;
using Mud.Core.Services;

namespace Mud.Infrastructure.Services;

/// <summary>
/// Thread-safe session manager for mapping connections to characters.
/// </summary>
public class SessionManager : ISessionManager
{
    private readonly ConcurrentDictionary<string, SessionInfo> _sessionsByConnection = new();
    private readonly ConcurrentDictionary<AccountId, string> _connectionsByAccount = new();
    private readonly ConcurrentDictionary<CharacterId, string> _connectionsByCharacter = new();
    private readonly ILogger<SessionManager> _logger;

    private record SessionInfo(AccountId AccountId, CharacterId CharacterId);

    public SessionManager(ILogger<SessionManager> logger)
    {
        _logger = logger;
    }

    public string? RegisterSession(string connectionId, AccountId accountId, CharacterId characterId)
    {
        string? kickedConnectionId = null;

        // Check for existing session for this account
        if (_connectionsByAccount.TryGetValue(accountId, out var existingConnectionId))
        {
            // Kick existing session
            kickedConnectionId = existingConnectionId;
            RemoveSession(existingConnectionId);
            _logger.LogInformation("Kicked existing session {ConnectionId} for account {AccountId}",
                existingConnectionId, accountId.Value);
        }

        // Register new session
        var sessionInfo = new SessionInfo(accountId, characterId);
        _sessionsByConnection[connectionId] = sessionInfo;
        _connectionsByAccount[accountId] = connectionId;
        _connectionsByCharacter[characterId] = connectionId;

        _logger.LogInformation("Registered session {ConnectionId} for account {AccountId}, character {CharacterId}",
            connectionId, accountId.Value, characterId.Value);

        return kickedConnectionId;
    }

    public void RemoveSession(string connectionId)
    {
        if (_sessionsByConnection.TryRemove(connectionId, out var sessionInfo))
        {
            _connectionsByAccount.TryRemove(sessionInfo.AccountId, out _);
            _connectionsByCharacter.TryRemove(sessionInfo.CharacterId, out _);
            _logger.LogInformation("Removed session {ConnectionId}", connectionId);
        }
    }

    public CharacterId? GetCharacterId(string connectionId)
    {
        return _sessionsByConnection.TryGetValue(connectionId, out var session)
            ? session.CharacterId
            : null;
    }

    public AccountId? GetAccountId(string connectionId)
    {
        return _sessionsByConnection.TryGetValue(connectionId, out var session)
            ? session.AccountId
            : null;
    }

    public string? GetConnectionId(CharacterId characterId)
    {
        return _connectionsByCharacter.TryGetValue(characterId, out var connectionId)
            ? connectionId
            : null;
    }

    public IReadOnlyList<(string ConnectionId, AccountId AccountId, CharacterId CharacterId)> GetAllSessions()
    {
        return _sessionsByConnection
            .Select(kvp => (kvp.Key, kvp.Value.AccountId, kvp.Value.CharacterId))
            .ToList();
    }
}
