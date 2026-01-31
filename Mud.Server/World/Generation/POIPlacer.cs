using Mud.Shared;
using Mud.Shared.World;

namespace Mud.Server.World.Generation;

/// <summary>
/// Places POIs on the terrain
/// </summary>
public static class POIPlacer
{
    /// <summary>
    /// Place POIs including spawn town at center
    /// </summary>
    public static POIMap PlacePOIs(this BiomeMap map)
    {
        var random = new Random(map.Seed + 1);
        var pois = new List<POI>();
        var poiPositions = new HashSet<Point>();

        // Create spawn town at center
        var spawnTown = new POI
        {
            Id = "spawn_town",
            Position = new Point(map.Width / 2, map.Height / 2),
            Type = POIType.Town,
            InfluenceRadius = WorldConfig.TownInfluenceRadius
        };
        pois.Add(spawnTown);
        poiPositions.Add(spawnTown.Position);

        // Calculate target POI count based on walkable area
        int walkableCount = CountWalkableTiles(map);
        int targetPOICount = (int)(walkableCount * WorldConfig.POIDensity);
        targetPOICount = Math.Clamp(targetPOICount, 5, 50);

        int attempts = 0;
        int maxAttempts = targetPOICount * 10;

        while (pois.Count < targetPOICount && attempts < maxAttempts)
        {
            attempts++;

            var pos = new Point(
                random.Next(0, map.Width),
                random.Next(0, map.Height)
            );

            if (!IsWalkable(map, pos)) continue;

            // Check minimum distance from other POIs
            bool tooClose = poiPositions.Any(existingPos =>
                Math.Abs(pos.X - existingPos.X) + Math.Abs(pos.Y - existingPos.Y) < WorldConfig.MinPOIDistance);

            if (tooClose) continue;

            var poiType = random.NextDouble() < 0.3 ? POIType.Dungeon : POIType.Camp;
            var poi = new POI
            {
                Id = $"poi_{pois.Count}",
                Position = pos,
                Type = poiType,
                InfluenceRadius = WorldConfig.CampInfluenceRadius
            };

            pois.Add(poi);
            poiPositions.Add(pos);
        }

        return new POIMap(map.Biomes, map.Noise, pois, map.Width, map.Height, map.Seed, map.GhostPadding);
    }

    /// <summary>
    /// Place exit marker for instances (at bottom center)
    /// </summary>
    public static POIMap PlaceExitMarker(this BiomeMap map, string parentPoiId)
    {
        var intendedPosition = new Point(map.Width / 2, map.Height - 2);
        var exitPosition = FindNearestWalkablePosition(map, intendedPosition);

        var exitPOI = new POI
        {
            Id = $"exit_{parentPoiId}",
            Position = exitPosition,
            Type = POIType.Camp,
            InfluenceRadius = WorldConfig.ExitInfluenceRadius,
            ParentPOIId = parentPoiId
        };

        return new POIMap(map.Biomes, map.Noise, new List<POI> { exitPOI }, map.Width, map.Height, map.Seed, map.GhostPadding);
    }

    /// <summary>
    /// Find the nearest walkable position using spiral search.
    /// Returns the starting position if already walkable, otherwise expands outward.
    /// </summary>
    private static Point FindNearestWalkablePosition(BiomeMap map, Point start)
    {
        if (IsWalkable(map, start))
            return start;

        // Spiral search: expand outward in rings
        int maxRadius = Math.Max(map.Width, map.Height);

        for (int distance = 1; distance <= maxRadius; distance++)
        {
            // Check all positions at this Manhattan distance in a square spiral
            // Top edge: y = start.Y - distance, x goes from start.X - distance to start.X + distance
            for (int x = start.X - distance; x <= start.X + distance; x++)
            {
                var pos = new Point(x, start.Y - distance);
                if (IsWalkable(map, pos)) return pos;
            }

            // Right edge: x = start.X + distance, y goes from start.Y - distance + 1 to start.Y + distance
            for (int y = start.Y - distance + 1; y <= start.Y + distance; y++)
            {
                var pos = new Point(start.X + distance, y);
                if (IsWalkable(map, pos)) return pos;
            }

            // Bottom edge: y = start.Y + distance, x goes from start.X + distance - 1 to start.X - distance
            for (int x = start.X + distance - 1; x >= start.X - distance; x--)
            {
                var pos = new Point(x, start.Y + distance);
                if (IsWalkable(map, pos)) return pos;
            }

            // Left edge: x = start.X - distance, y goes from start.Y + distance - 1 to start.Y - distance + 1
            for (int y = start.Y + distance - 1; y >= start.Y - distance + 1; y--)
            {
                var pos = new Point(start.X - distance, y);
                if (IsWalkable(map, pos)) return pos;
            }
        }

        // Fallback: return start position (should never happen with valid terrain generation)
        return start;
    }

    /// <summary>
    /// Check if a position is walkable (Plains biome only)
    /// </summary>
    private static bool IsWalkable(BiomeMap map, Point pos)
    {
        int x = pos.X + map.GhostPadding;
        int y = pos.Y + map.GhostPadding;

        if (x < 0 || x >= map.Biomes.GetLength(0) ||
            y < 0 || y >= map.Biomes.GetLength(1))
            return false;

        return map.Biomes[x, y] == BiomeType.Plains;
    }

    /// <summary>
    /// Count walkable tiles in biome map
    /// </summary>
    private static int CountWalkableTiles(BiomeMap map)
    {
        int count = 0;

        for (int x = 0; x < map.Width; x++)
        {
            for (int y = 0; y < map.Height; y++)
            {
                int ax = x + map.GhostPadding;
                int ay = y + map.GhostPadding;

                if (map.Biomes[ax, ay] == BiomeType.Plains)
                    count++;
            }
        }

        return count;
    }
}
