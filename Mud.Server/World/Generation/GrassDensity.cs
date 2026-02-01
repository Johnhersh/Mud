using Mud.Core;
using Mud.Core.World;

namespace Mud.Server.World.Generation;

/// <summary>
/// Applies grass density around POIs
/// </summary>
public static class GrassDensity
{
    /// <summary>
    /// Apply grass density based on distance to POIs (closer = denser)
    /// </summary>
    public static TileMapWithPOIs ApplyGrassDensity(this TileMapWithPOIs map)
    {
        var tiles = map.Tiles;

        foreach (var poi in map.POIs)
        {
            ApplyDensityAround(tiles, poi.Position, poi.InfluenceRadius, map.GhostPadding);
        }

        return map;
    }

    private static void ApplyDensityAround(Tile[,] tiles, Point center, float radius, int ghostPadding)
    {
        int totalWidth = tiles.GetLength(0);
        int totalHeight = tiles.GetLength(1);
        int radiusInt = (int)Math.Ceiling(radius);
        float radiusSquared = radius * radius;

        int centerX = center.X + ghostPadding;
        int centerY = center.Y + ghostPadding;

        for (int dx = -radiusInt; dx <= radiusInt; dx++)
        {
            for (int dy = -radiusInt; dy <= radiusInt; dy++)
            {
                int x = centerX + dx;
                int y = centerY + dy;

                if (x < 0 || x >= totalWidth || y < 0 || y >= totalHeight) continue;
                if (!IsGrassTile(tiles[x, y].Type)) continue;

                float distanceSquared = dx * dx + dy * dy;
                if (distanceSquared > radiusSquared) continue;

                // Closer to center = denser grass
                float normalizedDistance = (float)Math.Sqrt(distanceSquared) / radius;
                var newType = normalizedDistance switch
                {
                    < 0.33f => TileType.GrassDense,
                    < 0.66f => TileType.GrassMedium,
                    _ => tiles[x, y].Type
                };

                // Only upgrade, never downgrade
                if (GetDensityLevel(newType) > GetDensityLevel(tiles[x, y].Type))
                {
                    tiles[x, y] = new Tile(newType, true);
                }
            }
        }
    }

    private static bool IsGrassTile(TileType type) =>
        type is TileType.GrassSparse or TileType.GrassMedium or TileType.GrassDense;

    private static int GetDensityLevel(TileType type) =>
        type switch
        {
            TileType.GrassDense => 2,
            TileType.GrassMedium => 1,
            _ => 0
        };
}
