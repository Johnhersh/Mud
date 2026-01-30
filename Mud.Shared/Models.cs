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
public record AttackEvent(
    [property: Key(0)] string AttackerId,
    [property: Key(1)] string TargetId,
    [property: Key(2)] int Damage,
    [property: Key(3)] bool IsMelee,
    [property: Key(4)] Point TargetPosition
);

/// <summary>
/// Sent per-player (separate from snapshot) - no PlayerId needed since recipient knows it's theirs.
/// </summary>
[MessagePackObject]
public record XpGainEvent(
    [property: Key(0)] int Amount,
    [property: Key(1)] Point Position  // Where to show floating text (e.g., monster death location)
);

/// <summary>
/// Sent in grouped snapshot - everyone sees level-ups (social feature).
/// </summary>
[MessagePackObject]
public record LevelUpEvent(
    [property: Key(0)] string PlayerId,  // Needed so clients know which entity leveled
    [property: Key(1)] int NewLevel,
    [property: Key(2)] Point Position
);

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

    // Progression
    [Key(7)]
    public int Level { get; init; } = 1;
    [Key(8)]
    public int Experience { get; init; } = 0;

    // Attributes
    [Key(9)]
    public int Strength { get; init; } = 5;
    [Key(10)]
    public int Dexterity { get; init; } = 5;
    [Key(11)]
    public int Stamina { get; init; } = 5;
    [Key(12)]
    public int UnspentPoints { get; init; } = 0;
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

    [Key(10)]
    public List<AttackEvent> AttackEvents { get; init; } = new();

    [Key(11)]
    public List<LevelUpEvent> LevelUpEvents { get; init; } = new();
}
