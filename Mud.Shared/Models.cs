using MessagePack;

namespace Mud.Shared;

[MessagePackObject]
public record Point(
    [property: Key(0)] int X,
    [property: Key(1)] int Y);

[MessagePackObject]
public record Player
{
    [Key(0)]
    public string Id { get; init; } = string.Empty;
    [Key(1)]
    public string Name { get; init; } = string.Empty;
    [Key(2)]
    public Point Position { get; init; } = new(0, 0);
    [Key(3)]
    public List<Point> QueuedPath { get; init; } = new();
}

public enum Direction
{
    Up,
    Down,
    Left,
    Right
}

[MessagePackObject]
public record WorldSnapshot
{
    [Key(0)]
    public long Tick { get; init; }
    [Key(1)]
    public List<Player> Players { get; init; } = new();
    [Key(2)]
    public List<Point> Walls { get; init; } = new();
}
