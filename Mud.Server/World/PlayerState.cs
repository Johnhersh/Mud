using Mud.Shared;

namespace Mud.Server.World;

/// <summary>
/// Tracks player state across worlds
/// </summary>
public class PlayerState
{
    public PlayerId Id { get; init; }
    public string Name { get; set; } = string.Empty;
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
