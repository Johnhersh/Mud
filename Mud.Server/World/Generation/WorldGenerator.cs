using Mud.Core;
using Mud.Core.World;

namespace Mud.Server.World.Generation;

/// <summary>
/// Generates complete worlds using the terrain pipeline
/// </summary>
public static class WorldGenerator
{
    /// <summary>
    /// Generate the overworld with spawn town and POIs
    /// </summary>
    public static WorldState GenerateOverworld(int seed) =>
        new TerrainSeed(seed, WorldConfig.OverworldWidth, WorldConfig.OverworldHeight)
            .GenerateNoise(WorldConfig.OverworldGhostPadding)
            .ToBiomes()
            .CarveRivers(Edge.North, Edge.South, seed)
            .PlacePOIs()
            .ToTileMap()
            .PlaceBridges()
            .ApplyGrassDensity()
            .ToWorldState(WorldId.Overworld, WorldType.Overworld);

    /// <summary>
    /// Generate an instance from a POI with overworld context
    /// </summary>
    public static WorldState GenerateInstance(POI poi, TileMap overworldTerrain, int worldSeed)
    {
        int instanceSeed = HashCode.Combine(worldSeed, poi.Position.X, poi.Position.Y);

        // Determine density based on tile at POI position
        var tile = overworldTerrain.GetTile(poi.Position);
        float densityThreshold = tile?.Type switch
        {
            TileType.TreeSparse or TileType.TreeMedium or TileType.TreeDense => 0.4f,
            TileType.Water => 0.5f,
            _ => 0.7f
        };
        var parentBiome = tile?.Type switch
        {
            TileType.TreeSparse or TileType.TreeMedium or TileType.TreeDense => BiomeType.Forest,
            TileType.Water => BiomeType.Water,
            _ => BiomeType.Plains
        };

        return new TerrainSeed(instanceSeed, WorldConfig.InstanceWidth, WorldConfig.InstanceHeight)
            .GenerateNoise(WorldConfig.InstanceGhostPadding)
            .ToBiomesWithDensity(densityThreshold)
            .WithOverworldContext(overworldTerrain, poi.Position)
            .PlaceExitMarker(poi.Id)
            .ToTileMap()
            .ApplyGrassDensity()
            .ToInstanceState(poi.Id, parentBiome);
    }

}
