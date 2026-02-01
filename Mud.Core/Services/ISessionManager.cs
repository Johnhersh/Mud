namespace Mud.Core.Services;

/// <summary>
/// Manages the mapping between SignalR connections and game characters.
/// </summary>
public interface ISessionManager
{
    /// <summary>
    /// Register a new session, mapping a connection to an account and character.
    /// Returns the kicked connection ID if another session was active for this account.
    /// </summary>
    string? RegisterSession(string connectionId, AccountId accountId, CharacterId characterId);

    /// <summary>
    /// Remove a session by connection ID.
    /// </summary>
    void RemoveSession(string connectionId);

    /// <summary>
    /// Get the character ID for a connection.
    /// </summary>
    CharacterId? GetCharacterId(string connectionId);

    /// <summary>
    /// Get the account ID for a connection.
    /// </summary>
    AccountId? GetAccountId(string connectionId);

    /// <summary>
    /// Get the connection ID for a character.
    /// </summary>
    string? GetConnectionId(CharacterId characterId);

    /// <summary>
    /// Get all active sessions for graceful shutdown.
    /// </summary>
    IReadOnlyList<(string ConnectionId, AccountId AccountId, CharacterId CharacterId)> GetAllSessions();
}
