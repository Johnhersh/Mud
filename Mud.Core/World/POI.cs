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
    public required string Id { get; init; }

    [Key(1)]
    public required Point Position { get; init; }

    [Key(2)]
    public required POIType Type { get; init; }

    [Key(3)]
    public required float InfluenceRadius { get; init; }

    /// <summary>
    /// For instances: links back to the parent overworld POI
    /// </summary>
    [Key(4)]
    public string? ParentPOIId { get; init; }
}
