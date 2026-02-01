using MessagePack;
using Mud.Core;

namespace Mud.Core.World;

/// <summary>
/// Types of Points of Interest
/// </summary>
public enum POIType
{
    Camp,
    Town,
    Dungeon
}

/// <summary>
/// Point of Interest marker on the overworld
/// </summary>
[MessagePackObject]
public record POI
{
    [Key(0)]
    public string Id { get; init; } = string.Empty;

    [Key(1)]
    public Point Position { get; init; } = new(0, 0);

    [Key(2)]
    public POIType Type { get; init; }

    [Key(3)]
    public float InfluenceRadius { get; init; }

    /// <summary>
    /// For instances: links back to the parent overworld POI
    /// </summary>
    [Key(4)]
    public string? ParentPOIId { get; init; }
}
