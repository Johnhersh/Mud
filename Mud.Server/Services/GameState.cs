using System.Collections.Concurrent;
using Mud.Core;
using Mud.Server.World;

namespace Mud.Server.Services;

/// <summary>
/// Encapsulates all mutable game state independent of infrastructure (SignalR, persistence, tick timing).
/// Tests create their own GameState, populate it, call game logic, and assert outcomes.
/// </summary>
public class GameState
{
    // World management
    public ConcurrentDictionary<WorldId, WorldState> Worlds { get; } = new();
    public WorldState Overworld { get; set; } = null!;

    // Player tracking (keyed by SignalR ConnectionId)
    public ConcurrentDictionary<string, PlayerSession> Sessions { get; } = new();
    public ConcurrentDictionary<string, ConcurrentQueue<Direction>> PlayerInputQueues { get; } = new();

    // Attack events per world (cleared after each broadcast)
    public ConcurrentDictionary<WorldId, ConcurrentBag<AttackEvent>> AttackEvents { get; } = new();

    // XP events: keyed by world, then by connection ID (for per-player sends)
    public ConcurrentDictionary<WorldId, ConcurrentDictionary<string, List<XpGainEvent>>> XpEvents { get; } = new();

    // Level-up events: keyed by world only (broadcast to all in world)
    public ConcurrentDictionary<WorldId, ConcurrentBag<LevelUpEvent>> LevelUpEvents { get; } = new();

    // Progression updates: keyed by connection ID (sent per-player when their progression changes)
    public ConcurrentDictionary<string, ProgressionUpdate> ProgressionUpdates { get; } = new();
}
