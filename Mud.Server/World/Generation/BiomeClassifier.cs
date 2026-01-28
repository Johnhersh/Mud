using Mud.Shared.World;

namespace Mud.Server.World.Generation;

/// <summary>
/// Converts noise values to biome classifications
/// </summary>
public static class BiomeClassifier
{
    /// <summary>
    /// Convert noise map to biome map using default thresholds
    /// </summary>
    public static BiomeMap ToBiomes(this NoiseMap noise)
    {
        int totalWidth = noise.Values.GetLength(0);
        int totalHeight = noise.Values.GetLength(1);
        var biomes = new BiomeType[totalWidth, totalHeight];

        for (int x = 0; x < totalWidth; x++)
        {
            for (int y = 0; y < totalHeight; y++)
            {
                biomes[x, y] = noise.Values[x, y] switch
                {
                    < WorldConfig.WaterThreshold => BiomeType.Water,
                    < WorldConfig.PlainsThreshold => BiomeType.Plains,
                    _ => BiomeType.Forest
                };
            }
        }

        return new BiomeMap(biomes, noise.Values, noise.Width, noise.Height, noise.Seed, noise.GhostPadding);
    }

    /// <summary>
    /// Convert noise map to biome map with custom density threshold (for instances)
    /// </summary>
    public static BiomeMap ToBiomesWithDensity(this NoiseMap noise, float densityThreshold)
    {
        int totalWidth = noise.Values.GetLength(0);
        int totalHeight = noise.Values.GetLength(1);
        var biomes = new BiomeType[totalWidth, totalHeight];

        for (int x = 0; x < totalWidth; x++)
        {
            for (int y = 0; y < totalHeight; y++)
            {
                biomes[x, y] = noise.Values[x, y] switch
                {
                    < WorldConfig.WaterThreshold => BiomeType.Water,
                    var v when v < densityThreshold => BiomeType.Plains,
                    _ => BiomeType.Forest
                };
            }
        }

        return new BiomeMap(biomes, noise.Values, noise.Width, noise.Height, noise.Seed, noise.GhostPadding);
    }
}
