using AStar;
using AStar.Options;
using Mud.Shared;
using Mud.Shared.World;

namespace Mud.Server.World.Generation;

/// <summary>
/// Carves rivers through terrain using A* pathfinding
/// </summary>
public static class RiverCarver
{
    /// <summary>
    /// Carve a river from one edge to another
    /// </summary>
    public static BiomeMap CarveRivers(this BiomeMap biomes, Edge startEdge, Edge endEdge, int? seed = null)
    {
        var random = new Random(seed ?? biomes.Seed);

        int totalWidth = biomes.Biomes.GetLength(0);
        int totalHeight = biomes.Biomes.GetLength(1);
        int ghostPadding = biomes.GhostPadding;

        Point start = GetEdgePoint(startEdge, biomes.Width, biomes.Height, random);
        Point end = GetEdgePoint(endEdge, biomes.Width, biomes.Height, random);

        // Build weighted grid with noise for natural meandering
        var grid = new WorldGrid(BuildWeightedGrid(biomes.Biomes, biomes.Noise, totalWidth, totalHeight));
        var pathfinder = new PathFinder(grid, new PathFinderOptions { Weighting = Weighting.Negative });

        var path = pathfinder.FindPath(
            new Position(start.X + ghostPadding, start.Y + ghostPadding),
            new Position(end.X + ghostPadding, end.Y + ghostPadding)
        );

        if (path == null || path.Length == 0)
            return biomes;

        var newBiomes = (BiomeType[,])biomes.Biomes.Clone();

        foreach (var pos in path)
        {
            // Make river 2 tiles wide by also setting the tile to the right
            if (pos.Row >= 0 && pos.Row < totalWidth && pos.Column >= 0 && pos.Column < totalHeight)
            {
                newBiomes[pos.Row, pos.Column] = BiomeType.Water;
            }
            if (pos.Row + 1 >= 0 && pos.Row + 1 < totalWidth && pos.Column >= 0 && pos.Column < totalHeight)
            {
                newBiomes[pos.Row + 1, pos.Column] = BiomeType.Water;
            }
        }

        return new BiomeMap(newBiomes, biomes.Noise, biomes.Width, biomes.Height, biomes.Seed, ghostPadding);
    }

    /// <summary>
    /// Build a weighted grid where lower values are preferred paths.
    /// Rivers prefer lower-noise plains (sparse grass) and avoid forests.
    /// </summary>
    private static short[,] BuildWeightedGrid(BiomeType[,] biomes, float[,] noise, int width, int height)
    {
        var grid = new short[width, height];

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                var biome = biomes[x, y];
                var noiseValue = noise[x, y];

                int cost = biome switch
                {
                    // Water is always cheapest
                    BiomeType.Water => WorldConfig.RiverCostWater,

                    // Plains cost scales with noise - lower noise (sparse grass) is cheaper
                    // This makes rivers naturally follow lower terrain / sparse areas
                    BiomeType.Plains => WorldConfig.RiverCostPlains + (int)(noiseValue * WorldConfig.RiverNoiseScale),

                    // Forest is very expensive to cut through
                    BiomeType.Forest => WorldConfig.RiverCostForest,

                    _ => WorldConfig.RiverCostPlains
                };

                grid[x, y] = (short)cost;
            }
        }

        return grid;
    }

    /// <summary>
    /// Get movement cost for traversing a biome type (lower = preferred)
    /// </summary>
    private static short GetMoveCost(BiomeType biome)
    {
        return biome switch
        {
            BiomeType.Water => (short)WorldConfig.RiverCostWater,
            BiomeType.Plains => (short)WorldConfig.RiverCostPlains,
            BiomeType.Forest => (short)WorldConfig.RiverCostForest,
            _ => (short)WorldConfig.RiverCostPlains
        };
    }

    /// <summary>
    /// Get a random point on the specified edge
    /// </summary>
    private static Point GetEdgePoint(Edge edge, int width, int height, Random random)
    {
        return edge switch
        {
            Edge.North => new Point(random.Next(width), 0),
            Edge.South => new Point(random.Next(width), height - 1),
            Edge.West => new Point(0, random.Next(height)),
            Edge.East => new Point(width - 1, random.Next(height)),
            _ => new Point(0, 0)
        };
    }
}
