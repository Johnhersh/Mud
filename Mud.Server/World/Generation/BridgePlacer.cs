using Mud.Shared;
using Mud.Shared.World;

namespace Mud.Server.World.Generation;

/// <summary>
/// Places bridges across rivers to ensure map connectivity
/// </summary>
public static class BridgePlacer
{
    /// <summary>
    /// Place bridges at regular intervals along water tiles
    /// </summary>
    public static TileMapWithPOIs PlaceBridges(this TileMapWithPOIs map, int bridgeInterval = 20)
    {
        var tiles = (Tile[,])map.Tiles.Clone();
        int totalWidth = tiles.GetLength(0);
        int totalHeight = tiles.GetLength(1);

        // Scan for water tiles that have land on opposite sides (horizontal or vertical)
        for (int x = 1; x < totalWidth - 1; x++)
        {
            for (int y = 1; y < totalHeight - 1; y++)
            {
                if (tiles[x, y].Type != TileType.Water)
                    continue;

                // Check if this is a good bridge location:
                // Water tile with walkable land on both sides (horizontally or vertically)
                bool leftWalkable = tiles[x - 1, y].Walkable && tiles[x - 1, y].Type != TileType.Water;
                bool rightWalkable = tiles[x + 1, y].Walkable && tiles[x + 1, y].Type != TileType.Water;
                bool topWalkable = tiles[x, y - 1].Walkable && tiles[x, y - 1].Type != TileType.Water;
                bool bottomWalkable = tiles[x, y + 1].Walkable && tiles[x, y + 1].Type != TileType.Water;

                bool horizontalBridge = leftWalkable && rightWalkable;
                bool verticalBridge = topWalkable && bottomWalkable;

                if (!horizontalBridge && !verticalBridge)
                    continue;

                // Place bridge at intervals based on position
                // Using modulo on coordinates ensures consistent bridge placement
                if ((x + y) % bridgeInterval == 0)
                {
                    tiles[x, y] = new Tile(TileType.Bridge, true);
                }
            }
        }

        return new TileMapWithPOIs(tiles, map.POIs, map.Width, map.Height, map.GhostPadding);
    }
}
