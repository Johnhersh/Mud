using Mud.Core;

namespace Mud.Server.World;

/// <summary>
/// Tracks session data for a connected player (transient, lost on disconnect).
/// </summary>
public class PlayerSession
{
    public required string ConnectionId { get; init; }
    public required AccountId AccountId { get; init; }
    public required CharacterId CharacterId { get; init; }
    public required string Name { get; init; }
    public WorldId CurrentWorldId { get; set; }
    public Point Position { get; set; } = new(0, 0);
    public Point LastOverworldPosition { get; set; } = new(0, 0);

    /// <summary>
    /// Track which worlds have had tiles sent to avoid resending static data
    /// </summary>
    public HashSet<WorldId> SentTilesWorldIds { get; } = new();

    /// <summary>
    /// Move player to a new world
    /// </summary>
    public void TransferToWorld(WorldId worldId, Point position)
    {
        CurrentWorldId = worldId;
        Position = position;
    }

    /// <summary>
    /// Save overworld position before entering instance
    /// </summary>
    public void SaveOverworldPosition()
    {
        LastOverworldPosition = Position;
    }
}
