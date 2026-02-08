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

/// <summary>
/// Combat stats for monsters (players use CharacterCache instead).
/// </summary>
public static class MonsterStats
{
    public const int GoblinStrength = 3;
    public const int GoblinDexterity = 3;

    public static int GetStrength(string monsterName) => monsterName switch
    {
        "Goblin" => GoblinStrength,
        _ => 5
    };

    public static int GetDexterity(string monsterName) => monsterName switch
    {
        "Goblin" => GoblinDexterity,
        _ => 5
    };
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

/// <summary>
/// Sent per-player when their progression changes (XP gain, level up, stat allocation).
/// </summary>
[MessagePackObject]
public record ProgressionUpdate(
    [property: Key(0)] int Level,
    [property: Key(1)] int Experience,
    [property: Key(2)] int Strength,
    [property: Key(3)] int Dexterity,
    [property: Key(4)] int Stamina,
    [property: Key(5)] int UnspentPoints,
    [property: Key(6)] int MaxHealth
);

/// <summary>
/// Data transfer object sent to clients in WorldSnapshot for UI rendering.
/// Contains only what clients need to draw the game world - position, health bars, levels, etc.
/// Represents both players and monsters.
///
/// This is purely for rendering. Other concerns live elsewhere:
/// - Session/connection state: PlayerSession (server-side only)
/// - Persistent character data: CharacterCache/CharacterEntity (sent via OnProgressionUpdate)
/// </summary>
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
    [Key(7)]
    public required int Level { get; init; }
}

public enum Direction
{
    Up,
    Down,
    Left,
    Right
}

public static class PointExtensions
{
    extension(Point position)
    {
        public Point Adjacent(Direction direction) => direction switch
        {
            Direction.Up => position with { Y = position.Y - 1 },
            Direction.Down => position with { Y = position.Y + 1 },
            Direction.Left => position with { X = position.X - 1 },
            Direction.Right => position with { X = position.X + 1 },
            _ => position
        };
    }
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
