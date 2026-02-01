using Mud.Server.World;
using Mud.Core;
using Mud.Core.World;

namespace Mud.Server.World.Generation;

/// <summary>
/// Final stage of terrain pipeline - converts to tile map
/// </summary>
public static class TerrainPipeline
{
    /// <summary>
    /// Convert POI map to tile map (preserves POIs for later pipeline steps)
    /// </summary>
    public static TileMapWithPOIs ToTileMap(this POIMap map)
    {
        int totalWidth = map.Biomes.GetLength(0);
        int totalHeight = map.Biomes.GetLength(1);
        var tiles = new Tile[totalWidth, totalHeight];

        for (int x = 0; x < totalWidth; x++)
        {
            for (int y = 0; y < totalHeight; y++)
            {
                tiles[x, y] = BiomeToTile(map.Biomes[x, y], map.Noise[x, y]);
            }
        }

        // Add POI markers
        foreach (var poi in map.POIs)
        {
            int x = poi.Position.X + map.GhostPadding;
            int y = poi.Position.Y + map.GhostPadding;

            if (x >= 0 && x < tiles.GetLength(0) && y >= 0 && y < tiles.GetLength(1))
            {
                var tileType = poi.Type switch
                {
                    POIType.Town => TileType.TownCenter,
                    _ => TileType.POIMarker
                };
                tiles[x, y] = new Tile(tileType, true);
            }
        }

        return new TileMapWithPOIs(tiles, map.POIs, map.Width, map.Height, map.GhostPadding);
    }

    /// <summary>
    /// Convert to a complete WorldState (for overworld)
    /// </summary>
    public static WorldState ToWorldState(this TileMapWithPOIs map, WorldId id, WorldType type)
    {
        return new WorldState
        {
            Id = id,
            Type = type,
            Terrain = new TileMap(map.Tiles, map.Width, map.Height, map.GhostPadding),
            POIs = map.POIs
        };
    }

    /// <summary>
    /// Convert to an instance WorldState (with exit marker)
    /// </summary>
    public static WorldState ToInstanceState(this TileMapWithPOIs map, string parentPoiId, BiomeType parentBiome)
    {
        // First POI is the exit marker
        var exitPoi = map.POIs.FirstOrDefault();
        var exitPosition = exitPoi?.Position;

        var tileMap = new TileMap(map.Tiles, map.Width, map.Height, map.GhostPadding);
        if (exitPosition != null)
        {
            tileMap = tileMap.WithExitMarker(exitPosition);
        }

        return new WorldState
        {
            Id = new WorldId($"instance_{parentPoiId}"),
            Type = WorldType.Instance,
            Terrain = tileMap,
            POIs = new List<POI>(),
            ExitMarker = exitPosition,
            ParentPOIId = parentPoiId,
            ParentBiome = parentBiome
        };
    }

    /// <summary>
    /// Add exit marker to a tile map (for instances)
    /// </summary>
    public static TileMap WithExitMarker(this TileMap map, Point exitPosition)
    {
        var tiles = (Tile[,])map.Tiles.Clone();

        int x = exitPosition.X + map.GhostPadding;
        int y = exitPosition.Y + map.GhostPadding;

        if (x >= 0 && x < tiles.GetLength(0) && y >= 0 && y < tiles.GetLength(1))
        {
            tiles[x, y] = new Tile(TileType.ExitMarker, true);
        }

        return new TileMap(tiles, map.Width, map.Height, map.GhostPadding);
    }

    /// <summary>
    /// Convert biome type to tile with density based on noise value
    /// </summary>
    private static Tile BiomeToTile(BiomeType biome, float noise)
    {
        return biome switch
        {
            BiomeType.Water => new Tile(TileType.Water, false),
            BiomeType.Plains => new Tile(GetGrassDensity(noise), true),
            BiomeType.Forest => new Tile(GetTreeDensity(noise), false),
            _ => new Tile(TileType.GrassSparse, true)
        };
    }

    /// <summary>
    /// Get grass density based on noise value (higher noise = denser grass)
    /// </summary>
    private static TileType GetGrassDensity(float noise)
    {
        // Plains is between WaterThreshold and PlainsThreshold
        // Normalize within that range
        float range = WorldConfig.PlainsThreshold - WorldConfig.WaterThreshold;
        float normalized = (noise - WorldConfig.WaterThreshold) / range;

        return normalized switch
        {
            < 0.33f => TileType.GrassSparse,
            < 0.66f => TileType.GrassMedium,
            _ => TileType.GrassDense
        };
    }

    /// <summary>
    /// Get tree density based on noise value (higher noise = denser forest)
    /// </summary>
    private static TileType GetTreeDensity(float noise)
    {
        // Forest is above PlainsThreshold (up to 1.0)
        // Normalize within that range
        float range = 1.0f - WorldConfig.PlainsThreshold;
        float normalized = (noise - WorldConfig.PlainsThreshold) / range;

        return normalized switch
        {
            < 0.15f => TileType.TreeSparse,
            < 0.40f => TileType.TreeMedium,
            _ => TileType.TreeDense
        };
    }

    /// <summary>
    /// Check if a position is walkable in the tile map
    /// </summary>
    public static bool IsWalkable(this TileMap map, Point position)
    {
        // Check world boundaries (not ghost area)
        if (position.X < 0 || position.X >= map.Width ||
            position.Y < 0 || position.Y >= map.Height)
            return false;

        int x = position.X + map.GhostPadding;
        int y = position.Y + map.GhostPadding;

        return map.Tiles[x, y].Walkable;
    }

    /// <summary>
    /// Get tile at position
    /// </summary>
    public static Tile? GetTile(this TileMap map, Point position)
    {
        int x = position.X + map.GhostPadding;
        int y = position.Y + map.GhostPadding;

        if (x < 0 || x >= map.Tiles.GetLength(0) ||
            y < 0 || y >= map.Tiles.GetLength(1))
            return null;

        return map.Tiles[x, y];
    }

    /// <summary>
    /// Convert tile map to serializable TileData flat list for network transmission
    /// Uses row-major order: index = y * totalWidth + x
    /// </summary>
    public static List<TileData> ToTileDataArray(this TileMap map)
    {
        int totalWidth = map.Tiles.GetLength(0);
        int totalHeight = map.Tiles.GetLength(1);
        var data = new List<TileData>(totalWidth * totalHeight);

        // Row-major order for easy JavaScript access
        for (int y = 0; y < totalHeight; y++)
        {
            for (int x = 0; x < totalWidth; x++)
            {
                var tile = map.Tiles[x, y];
                data.Add(new TileData(tile.Type, tile.Walkable));
            }
        }

        return data;
    }
}
