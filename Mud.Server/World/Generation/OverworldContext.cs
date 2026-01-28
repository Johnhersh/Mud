using Mud.Shared;
using Mud.Shared.World;

namespace Mud.Server.World.Generation;

/// <summary>
/// Applies overworld context to instance ghost padding
/// </summary>
public static class OverworldContext
{
    /// <summary>
    /// Replace ghost padding with actual overworld terrain around the POI
    /// </summary>
    public static BiomeMap WithOverworldContext(this BiomeMap instance, TileMap overworld, Point poiPosition)
    {
        var biomes = instance.Biomes;
        int ghostPadding = instance.GhostPadding;
        int width = instance.Width;
        int height = instance.Height;

        // Fill ghost padding areas with adjacent overworld tiles
        for (int x = 0; x < biomes.GetLength(0); x++)
        {
            for (int y = 0; y < biomes.GetLength(1); y++)
            {
                int relativeX = x - ghostPadding;
                int relativeY = y - ghostPadding;

                // Skip if inside playable area (not ghost padding)
                if (relativeX >= 0 && relativeX < width &&
                    relativeY >= 0 && relativeY < height)
                    continue;

                // We're in ghost padding, so at least one of these will be non-zero
                int overworldOffsetX = relativeX < 0 ? -1 : (relativeX >= width ? 1 : 0);
                int overworldOffsetY = relativeY < 0 ? -1 : (relativeY >= height ? 1 : 0);

                var tile = overworld.GetTile(new Point(poiPosition.X + overworldOffsetX, poiPosition.Y + overworldOffsetY));
                if (tile is not null) biomes[x, y] = TileToBiome(tile.Type);
            }
        }

        return instance;
    }

    /// <summary>
    /// Convert tile type back to biome type
    /// </summary>
    private static BiomeType TileToBiome(TileType tileType) =>
        tileType switch
        {
            TileType.Water => BiomeType.Water,
            TileType.TreeSparse or TileType.TreeMedium or TileType.TreeDense => BiomeType.Forest,
            _ => BiomeType.Plains
        };
}
