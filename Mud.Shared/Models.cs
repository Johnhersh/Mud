using MessagePack;
using Mud.Shared.World;

namespace Mud.Shared;

[MessagePackObject]
public record Point(
    [property: Key(0)] int X,
    [property: Key(1)] int Y);

public enum EntityType
{
    Player,
    Monster
}

[MessagePackObject]
public record Entity
{
    [Key(0)]
    public string Id { get; init; } = string.Empty;
    [Key(1)]
    public string Name { get; init; } = string.Empty;
    [Key(2)]
    public Point Position { get; init; } = new(0, 0);
    [Key(3)]
    public List<Point> QueuedPath { get; init; } = new();
    [Key(4)]
    public EntityType Type { get; init; }
    [Key(5)]
    public int Health { get; init; }
    [Key(6)]
    public int MaxHealth { get; init; }
}

public enum Direction
{
    Up,
    Down,
    Left,
    Right
}

/// <summary>
/// Strongly-typed player identifier (wraps SignalR connection ID).
/// </summary>
[MessagePackObject]
public readonly record struct PlayerId([property: Key(0)] string Value)
{
    public override string ToString() => Value;
}

[MessagePackObject]
public record WorldSnapshot
{
    [Key(0)]
    public long Tick { get; init; }

    [Key(1)]
    public string WorldId { get; init; } = string.Empty;

    [Key(2)]
    public WorldType WorldType { get; init; }

    [Key(3)]
    public List<Entity> Entities { get; init; } = new();

    [Key(4)]
    public List<TileData>? Tiles { get; init; }  // Flat array, row-major order (y * totalWidth + x)

    [Key(5)]
    public List<POI> POIs { get; init; } = new();

    [Key(6)]
    public Point? ExitMarker { get; init; }

    [Key(7)]
    public int Width { get; init; }

    [Key(8)]
    public int Height { get; init; }

    [Key(9)]
    public int GhostPadding { get; init; }
}
