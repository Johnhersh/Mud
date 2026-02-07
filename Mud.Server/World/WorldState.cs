using System.Collections.Concurrent;
using Mud.Core;
using Mud.Core.World;
using Mud.Server.World.Generation;

namespace Mud.Server.World;

/// <summary>
/// Data container for a game world (overworld or instance).
/// Pure data + computed accessors. Behavior lives in extensions.
/// </summary>
public class WorldState
{
    public WorldId Id { get; init; }
    public WorldType Type { get; init; }
    public TileMap Terrain { get; init; } = null!;
    public List<POI> POIs { get; init; } = new();
    public ConcurrentDictionary<string, Entity> Entities { get; } = new();
    public Point? ExitMarker { get; init; }
    public string? ParentPOIId { get; init; }
    public BiomeType ParentBiome { get; init; }

    // Computed accessors — pure queries over owned data

    public bool IsWalkable(Point position) => Terrain.IsWalkable(position);
    public POI? GetPOIAt(Point position) => POIs.FirstOrDefault(p => p.Position == position);
    public bool IsExitMarker(Point position) => ExitMarker != null && ExitMarker == position;
    public Entity? GetEntity(string entityId) => Entities.TryGetValue(entityId, out var e) ? e : null;
    public IEnumerable<Entity> GetPlayers() => Entities.Values.Where(e => e.Type == EntityType.Player);
    public IEnumerable<Entity> GetMonsters() => Entities.Values.Where(e => e.Type == EntityType.Monster);

    public void AddEntity(Entity entity) => Entities[entity.Id] = entity;
    public bool RemoveEntity(string entityId) => Entities.TryRemove(entityId, out _);
    public void UpdateEntity(Entity entity) => Entities[entity.Id] = entity;
    public void UpdateEntity(string entityId, Func<Entity, Entity> transform)
    {
        if (Entities.TryGetValue(entityId, out var current))
            Entities[entityId] = transform(current);
    }
}
