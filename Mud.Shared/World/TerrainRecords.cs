using MessagePack;

namespace Mud.Shared.World;

/// <summary>
/// Biome classification for terrain generation
/// </summary>
public enum BiomeType
{
    Water,
    Plains,
    Forest
}

/// <summary>
/// Final tile types for rendering
/// </summary>
public enum TileType
{
    GrassSparse,
    GrassMedium,
    GrassDense,
    Water,
    Bridge,
    TreeSparse,
    TreeMedium,
    TreeDense,
    POIMarker,
    ExitMarker,
    TownCenter
}

/// <summary>
/// World scale types
/// </summary>
public enum WorldType
{
    Overworld,
    Instance
}

/// <summary>
/// Edge direction for river generation
/// </summary>
public enum Edge
{
    North,
    South,
    East,
    West
}

// Pipeline records - each stage transforms data flowing through the pipeline

/// <summary>
/// Stage 1: Initial seed parameters for terrain generation
/// </summary>
public record TerrainSeed(int Seed, int Width, int Height);

/// <summary>
/// Stage 2: Raw noise values from Perlin/Simplex generation
/// </summary>
public record NoiseMap(float[,] Values, int Width, int Height, int Seed, int GhostPadding = 0);

/// <summary>
/// Stage 3: Noise classified into biome types (keeps noise for density calculation)
/// </summary>
public record BiomeMap(BiomeType[,] Biomes, float[,] Noise, int Width, int Height, int Seed, int GhostPadding = 0);

/// <summary>
/// Stage 4: Map with POIs placed
/// </summary>
public record POIMap(BiomeType[,] Biomes, float[,] Noise, List<POI> POIs, int Width, int Height, int Seed, int GhostPadding = 0);

/// <summary>
/// Stage 5: Tile map with POIs (before influence applied)
/// </summary>
public record TileMapWithPOIs(Tile[,] Tiles, List<POI> POIs, int Width, int Height, int GhostPadding = 0);

/// <summary>
/// Stage 6: Final tile map ready for game use
/// </summary>
public record TileMap(Tile[,] Tiles, int Width, int Height, int GhostPadding = 0);

/// <summary>
/// Individual tile data
/// </summary>
[MessagePackObject]
public record Tile(
    [property: Key(0)] TileType Type,
    [property: Key(1)] bool Walkable);

/// <summary>
/// Serializable tile data for network transmission
/// </summary>
[MessagePackObject]
public record TileData(
    [property: Key(0)] TileType Type,
    [property: Key(1)] bool Walkable);
