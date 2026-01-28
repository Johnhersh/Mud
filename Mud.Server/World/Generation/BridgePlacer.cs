using Mud.Shared;
using Mud.Shared.World;

namespace Mud.Server.World.Generation;

/// <summary>
/// Places bridges across rivers to ensure map connectivity
/// </summary>
public static class BridgePlacer
{
    /// <summary>
    /// Place bridges at regular intervals along 2-tile wide rivers
    /// </summary>
    public static TileMapWithPOIs PlaceBridges(this TileMapWithPOIs map, int bridgeInterval = 20)
    {
        var tiles = (Tile[,])map.Tiles.Clone();
        int totalWidth = tiles.GetLength(0);
        int totalHeight = tiles.GetLength(1);

        // Scan for 2-tile wide water sections with land on outer sides
        for (int x = 2; x < totalWidth - 2; x++)
        {
            for (int y = 2; y < totalHeight - 2; y++)
            {
                if (tiles[x, y].Type != TileType.Water)
                    continue;

                // Check for horizontal bridge across 2-tile wide river (river flows vertically)
                // Pattern: Land | Water | Water | Land (horizontally)
                // AND water above and below both water tiles (to ensure it's a channel, not diagonal)
                bool isHorizontalRiver = tiles[x + 1, y].Type == TileType.Water;
                bool leftLand = IsWalkableLand(tiles[x - 1, y]);
                bool rightLand = IsWalkableLand(tiles[x + 2, y]);
                bool waterAbove = tiles[x, y - 1].Type == TileType.Water && tiles[x + 1, y - 1].Type == TileType.Water;
                bool waterBelow = tiles[x, y + 1].Type == TileType.Water && tiles[x + 1, y + 1].Type == TileType.Water;

                if (isHorizontalRiver && leftLand && rightLand && waterAbove && waterBelow && (x + y) % bridgeInterval == 0)
                {
                    tiles[x, y] = new Tile(TileType.Bridge, true);
                    tiles[x + 1, y] = new Tile(TileType.Bridge, true);
                    continue;
                }

                // Check for vertical bridge across 2-tile wide river (river flows horizontally)
                // Pattern: Land | Water | Water | Land (vertically)
                // AND water to the left and right of both water tiles (to ensure it's a channel, not diagonal)
                bool isVerticalRiver = tiles[x, y + 1].Type == TileType.Water;
                bool topLand = IsWalkableLand(tiles[x, y - 1]);
                bool bottomLand = IsWalkableLand(tiles[x, y + 2]);
                bool waterLeft = tiles[x - 1, y].Type == TileType.Water && tiles[x - 1, y + 1].Type == TileType.Water;
                bool waterRight = tiles[x + 1, y].Type == TileType.Water && tiles[x + 1, y + 1].Type == TileType.Water;

                if (isVerticalRiver && topLand && bottomLand && waterLeft && waterRight && (x + y) % bridgeInterval == 0)
                {
                    tiles[x, y] = new Tile(TileType.Bridge, true);
                    tiles[x, y + 1] = new Tile(TileType.Bridge, true);
                }
            }
        }

        return new TileMapWithPOIs(tiles, map.POIs, map.Width, map.Height, map.GhostPadding);
    }

    private static bool IsWalkableLand(Tile tile)
    {
        return tile.Walkable && tile.Type != TileType.Water;
    }
}
