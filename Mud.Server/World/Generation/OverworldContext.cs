using Mud.Core;
using Mud.Core.World;

namespace Mud.Server.World.Generation;

/// <summary>
/// Applies overworld context to instance edges with smooth biome blending
/// </summary>
public static class OverworldContext
{
    /// <summary>
    /// Apply overworld context with smooth edge blending using noise shifting
    /// </summary>
    public static BiomeMap WithOverworldContext(this BiomeMap instance, TileMap overworld, Point poiPosition, float densityThreshold)
    {
        int ghostPadding = instance.GhostPadding;

        // Guard: no blending if no ghost padding
        if (ghostPadding == 0)
            return instance;

        var biomes = instance.Biomes;
        var noise = instance.Noise;
        int width = instance.Width;
        int height = instance.Height;

        // Get parent biome as fallback for null neighbors
        var parentTile = overworld.GetTile(poiPosition);
        var parentBiome = parentTile is not null ? TileToBiome(parentTile.Type) : BiomeType.Plains;

        // Cache all 8 neighbor biomes
        var neighbors = new NeighborBiomes(
            GetNeighborBiome(overworld, poiPosition, 0, -1, parentBiome),   // North
            GetNeighborBiome(overworld, poiPosition, 0, 1, parentBiome),    // South
            GetNeighborBiome(overworld, poiPosition, -1, 0, parentBiome),   // West
            GetNeighborBiome(overworld, poiPosition, 1, 0, parentBiome),    // East
            GetNeighborBiome(overworld, poiPosition, -1, -1, parentBiome),  // NorthWest
            GetNeighborBiome(overworld, poiPosition, 1, -1, parentBiome),   // NorthEast
            GetNeighborBiome(overworld, poiPosition, -1, 1, parentBiome),   // SouthWest
            GetNeighborBiome(overworld, poiPosition, 1, 1, parentBiome)     // SouthEast
        );

        int totalWidth = biomes.GetLength(0);
        int totalHeight = biomes.GetLength(1);
        int influenceRange = ghostPadding * 2;

        for (int x = 0; x < totalWidth; x++)
        {
            for (int y = 0; y < totalHeight; y++)
            {
                // Calculate distances to each edge (negative = in ghost padding)
                int distanceToWest = x - ghostPadding;
                int distanceToEast = (width + ghostPadding - 1) - x;
                int distanceToNorth = y - ghostPadding;
                int distanceToSouth = (height + ghostPadding - 1) - y;

                // Calculate influence from each cardinal direction
                float westInfluence = CalculateInfluence(distanceToWest, ghostPadding, influenceRange);
                float eastInfluence = CalculateInfluence(distanceToEast, ghostPadding, influenceRange);
                float northInfluence = CalculateInfluence(distanceToNorth, ghostPadding, influenceRange);
                float southInfluence = CalculateInfluence(distanceToSouth, ghostPadding, influenceRange);

                // Calculate diagonal influences (only when both adjacent cardinals have influence)
                float nwInfluence = Math.Min(northInfluence, westInfluence) * 0.707f;
                float neInfluence = Math.Min(northInfluence, eastInfluence) * 0.707f;
                float swInfluence = Math.Min(southInfluence, westInfluence) * 0.707f;
                float seInfluence = Math.Min(southInfluence, eastInfluence) * 0.707f;

                // Accumulate weighted biome targets
                float targetSum = 0f;
                float weightSum = 0f;

                AccumulateInfluence(ref targetSum, ref weightSum, westInfluence, neighbors.West);
                AccumulateInfluence(ref targetSum, ref weightSum, eastInfluence, neighbors.East);
                AccumulateInfluence(ref targetSum, ref weightSum, northInfluence, neighbors.North);
                AccumulateInfluence(ref targetSum, ref weightSum, southInfluence, neighbors.South);
                AccumulateInfluence(ref targetSum, ref weightSum, nwInfluence, neighbors.NorthWest);
                AccumulateInfluence(ref targetSum, ref weightSum, neInfluence, neighbors.NorthEast);
                AccumulateInfluence(ref targetSum, ref weightSum, swInfluence, neighbors.SouthWest);
                AccumulateInfluence(ref targetSum, ref weightSum, seInfluence, neighbors.SouthEast);

                // Skip if no influence (position far from all edges)
                if (weightSum <= 0f)
                    continue;

                // Calculate weighted target and total influence
                float weightedTarget = targetSum / weightSum;
                float totalInfluence = Math.Min(1f, weightSum);

                // Shift noise toward weighted target
                float originalNoise = noise[x, y];
                float adjustedNoise = Lerp(originalNoise, weightedTarget, totalInfluence * WorldConfig.EdgeBlendStrength);
                adjustedNoise = Math.Clamp(adjustedNoise, 0f, 1f);

                // Reclassify biome based on adjusted noise
                biomes[x, y] = ClassifyBiome(adjustedNoise, densityThreshold);
            }
        }

        return instance;
    }

    /// <summary>
    /// Calculate influence for a single edge using smoothstep falloff
    /// </summary>
    private static float CalculateInfluence(int distance, int ghostPadding, int influenceRange)
    {
        // Continuous falloff from outer ghost edge to inner influence boundary
        float linearFalloff = Math.Clamp(1f - (distance + ghostPadding) / (float)influenceRange, 0f, 1f);
        return Smoothstep(linearFalloff);
    }

    /// <summary>
    /// Accumulate influence from a neighbor biome
    /// </summary>
    private static void AccumulateInfluence(ref float targetSum, ref float weightSum, float influence, BiomeType biome)
    {
        if (influence <= 0f)
            return;

        targetSum += BiomeTarget(biome) * influence;
        weightSum += influence;
    }

    /// <summary>
    /// Get biome target noise value (center of biome's noise range)
    /// </summary>
    private static float BiomeTarget(BiomeType biome) => biome switch
    {
        BiomeType.Water => WorldConfig.WaterThreshold / 2f,
        BiomeType.Plains => (WorldConfig.WaterThreshold + WorldConfig.PlainsThreshold) / 2f,
        BiomeType.Forest => (WorldConfig.PlainsThreshold + 1f) / 2f,
        _ => (WorldConfig.WaterThreshold + WorldConfig.PlainsThreshold) / 2f
    };

    /// <summary>
    /// Classify noise value to biome (same logic as ToBiomesWithDensity)
    /// </summary>
    private static BiomeType ClassifyBiome(float noise, float densityThreshold) => noise switch
    {
        < WorldConfig.WaterThreshold => BiomeType.Water,
        var v when v < densityThreshold => BiomeType.Plains,
        _ => BiomeType.Forest
    };

    /// <summary>
    /// Get neighbor biome with null fallback
    /// </summary>
    private static BiomeType GetNeighborBiome(TileMap overworld, Point poi, int dx, int dy, BiomeType fallback)
    {
        var tile = overworld.GetTile(new Point(poi.X + dx, poi.Y + dy));
        return tile is not null ? TileToBiome(tile.Type) : fallback;
    }

    /// <summary>
    /// Convert tile type to biome type
    /// </summary>
    private static BiomeType TileToBiome(TileType tileType) => tileType switch
    {
        TileType.Water => BiomeType.Water,
        TileType.TreeSparse or TileType.TreeMedium or TileType.TreeDense => BiomeType.Forest,
        _ => BiomeType.Plains
    };

    /// <summary>
    /// Smooth interpolation using 3x^2 - 2x^3
    /// </summary>
    private static float Smoothstep(float x) => x * x * (3f - 2f * x);

    /// <summary>
    /// Linear interpolation between two values
    /// </summary>
    private static float Lerp(float a, float b, float t) => a + (b - a) * t;

    /// <summary>
    /// Cached neighbor biomes for efficient lookup
    /// </summary>
    private readonly record struct NeighborBiomes(
        BiomeType North,
        BiomeType South,
        BiomeType West,
        BiomeType East,
        BiomeType NorthWest,
        BiomeType NorthEast,
        BiomeType SouthWest,
        BiomeType SouthEast
    );
}
