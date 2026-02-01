using MessagePack;
using Mud.Core.World;

namespace Mud.Core;

[MessagePackObject]
public record Point(
    [property: Key(0)] int X,
    [property: Key(1)] int Y);

public enum EntityType
{
    Player,
    Monster
}

public enum StatType
{
    Strength,
    Dexterity,
    Stamina
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
    public required string Id { get; init; }
    [Key(1)]
    public required string Name { get; init; }
    [Key(2)]
    public required Point Position { get; init; }
    [Key(3)]
    public required List<Point> QueuedPath { get; init; }
    [Key(4)]
    public required EntityType Type { get; init; }
    [Key(5)]
    public required int Health { get; init; }
    [Key(6)]
    public required int MaxHealth { get; init; }

    // Progression
    [Key(7)]
    public required int Level { get; init; }
    [Key(8)]
    public required int Experience { get; init; }

    // Attributes
    [Key(9)]
    public required int Strength { get; init; }
    [Key(10)]
    public required int Dexterity { get; init; }
    [Key(11)]
    public required int Stamina { get; init; }
    [Key(12)]
    public required int UnspentPoints { get; init; }
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

/// <summary>
/// Strongly-typed world identifier.
/// </summary>
[MessagePackObject]
public readonly record struct WorldId([property: Key(0)] string Value)
{
    public static readonly WorldId Overworld = new("overworld");

    public override string ToString() => Value;
}

/// <summary>
/// Strongly-typed character identifier (persistent, wraps database GUID).
/// </summary>
[MessagePackObject]
public readonly record struct CharacterId([property: Key(0)] Guid Value)
{
    public override string ToString() => Value.ToString();
}

/// <summary>
/// Strongly-typed account identifier (wraps ASP.NET Identity user ID).
/// </summary>
public readonly record struct AccountId(string Value)
{
    public override string ToString() => Value;
}

[MessagePackObject]
public record WorldSnapshot
{
    [Key(0)]
    public required long Tick { get; init; }

    [Key(1)]
    public required string WorldId { get; init; }

    [Key(2)]
    public required WorldType WorldType { get; init; }

    [Key(3)]
    public required List<Entity> Entities { get; init; }

    [Key(4)]
    public List<TileData>? Tiles { get; init; }  // Flat array, row-major order (y * totalWidth + x)

    [Key(5)]
    public required List<POI> POIs { get; init; }

    [Key(6)]
    public Point? ExitMarker { get; init; }

    [Key(7)]
    public required int Width { get; init; }

    [Key(8)]
    public required int Height { get; init; }

    [Key(9)]
    public required int GhostPadding { get; init; }

    [Key(10)]
    public required List<AttackEvent> AttackEvents { get; init; }

    [Key(11)]
    public required List<LevelUpEvent> LevelUpEvents { get; init; }
}
