using System.Collections.Concurrent;
using Mud.Shared;
using Mud.Shared.World;
using Mud.Server.World.Generation;

namespace Mud.Server.World;

/// <summary>
/// Represents a game world (overworld or instance)
/// </summary>
public class WorldState
{
    public string Id { get; init; } = string.Empty;
    public WorldType Type { get; init; }
    public TileMap Terrain { get; init; } = null!;
    public List<POI> POIs { get; init; } = new();
    public ConcurrentDictionary<string, Entity> Entities { get; } = new();
    public Point? ExitMarker { get; init; }
    public string? ParentPOIId { get; init; } // Links instance to overworld POI
    public BiomeType ParentBiome { get; init; } // For fractal consistency

    /// <summary>
    /// Check if a position is walkable considering terrain and entities
    /// </summary>
    public bool IsWalkable(Point position)
    {
        return Terrain.IsWalkable(position);
    }

    /// <summary>
    /// Get POI at position, if any
    /// </summary>
    public POI? GetPOIAt(Point position)
    {
        return POIs.FirstOrDefault(p => p.Position == position);
    }

    /// <summary>
    /// Check if position is the exit marker
    /// </summary>
    public bool IsExitMarker(Point position)
    {
        return ExitMarker != null && ExitMarker == position;
    }

    /// <summary>
    /// Add an entity to this world
    /// </summary>
    public void AddEntity(Entity entity)
    {
        Entities[entity.Id] = entity;
    }

    /// <summary>
    /// Remove an entity from this world
    /// </summary>
    public bool RemoveEntity(string entityId)
    {
        return Entities.TryRemove(entityId, out _);
    }

    /// <summary>
    /// Get entity by ID
    /// </summary>
    public Entity? GetEntity(string entityId)
    {
        return Entities.TryGetValue(entityId, out var entity) ? entity : null;
    }

    /// <summary>
    /// Update an entity in this world
    /// </summary>
    public void UpdateEntity(Entity entity)
    {
        Entities[entity.Id] = entity;
    }

    /// <summary>
    /// Get all player entities
    /// </summary>
    public IEnumerable<Entity> GetPlayers()
    {
        return Entities.Values.Where(e => e.Type == EntityType.Player);
    }

    /// <summary>
    /// Get all monster entities
    /// </summary>
    public IEnumerable<Entity> GetMonsters()
    {
        return Entities.Values.Where(e => e.Type == EntityType.Monster);
    }

    /// <summary>
    /// Find a random walkable position
    /// </summary>
    public Point? FindRandomWalkablePosition(Random random, int maxAttempts = 100)
    {
        for (int i = 0; i < maxAttempts; i++)
        {
            var pos = new Point(
                random.Next(0, Terrain.Width),
                random.Next(0, Terrain.Height)
            );

            if (IsWalkable(pos) && !Entities.Values.Any(e => e.Position == pos))
            {
                return pos;
            }
        }
        return null;
    }

    /// <summary>
    /// Convert to WorldSnapshot for network transmission
    /// </summary>
    /// <param name="tick">Current game tick</param>
    /// <param name="includeTiles">Whether to include tile data (only needed on world change)</param>
    /// <param name="attackEvents">Attack events that occurred this tick</param>
    public WorldSnapshot ToSnapshot(long tick, bool includeTiles = true, List<AttackEvent>? attackEvents = null)
    {
        return new WorldSnapshot
        {
            Tick = tick,
            WorldId = Id,
            WorldType = Type,
            Entities = Entities.Values.ToList(),
            Tiles = includeTiles ? Terrain.ToTileDataArray() : null,
            POIs = POIs,
            ExitMarker = ExitMarker,
            Width = Terrain.Width,
            Height = Terrain.Height,
            GhostPadding = Terrain.GhostPadding,
            AttackEvents = attackEvents ?? new()
        };
    }
}
