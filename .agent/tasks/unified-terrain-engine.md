# Task: Unified Terrain Engine & Two-Scale World

## Objective
Implement a procedural terrain generation system that powers both the persistent Overworld and disposable Instances, enabling the core "Explore → Fight → Loot" loop.

## Problem Statement
The current prototype has a static, randomly-placed wall system with no terrain variety. Players exist in a single flat world with no sense of place or exploration. This task introduces procedural terrain generation, biomes, rivers, and a two-scale world architecture (overworld for travel, instances for combat).

## Success Criteria
1. Players spawn in a procedurally generated 150x150 overworld with visible biomes (plains, forest, water)
2. Rivers flow edge-to-edge across the overworld using A* pathfinding
3. POI markers appear on walkable tiles; pressing Enter on a POI teleports the player into a 50x50 instance
4. Instance terrain reflects the parent overworld tile's biome (fractal consistency)
5. Exit markers in instances return players to their overworld position
6. Camera follows the player instead of being fixed at center
7. Ghost chunks are visible beyond world boundaries but impassable
8. Multiple players can see each other in both overworld and instances
9. Spawn town at world center with influence mask clearing the area

---

## Technical Specification

### 1. Functional Terrain Pipeline

The terrain generation uses a pipeline architecture where each step is a **record type** with **extension methods** providing behavior. This enables a fluent API:

```csharp
var terrain = new TerrainSeed(seed, width, height)
    .GenerateNoise()
    .ToBiomes()
    .CarveRivers(startEdge, endEdge)
    .ApplyInfluenceMasks(pois)
    .ToTileMap();
```

#### Pipeline Stages (Mud.Server/Generation/)

| Stage | Input Record | Output Record | Description |
|-------|--------------|---------------|-------------|
| 1. Seed | `TerrainSeed` | `NoiseMap` | Initialize dimensions, seed value |
| 2. Noise | `NoiseMap` | `BiomeMap` | Perlin/Simplex noise → biome thresholds |
| 3. Biomes | `BiomeMap` | `BiomeMap` | Classify tiles: Water (<0.3), Plains (<0.6), Forest (≥0.6) |
| 4. Rivers | `BiomeMap` | `RiverMap` | A* pathfinding carves river from edge to edge |
| 5. Influence | `RiverMap` | `InfluencedMap` | POIs push back obstacles in radius |
| 6. Finalize | `InfluencedMap` | `TileMap` | Convert to final tile array |

#### Record Definitions (Mud.Shared/Generation/)

```csharp
public record TerrainSeed(int Seed, int Width, int Height);
public record NoiseMap(float[,] Values, int Width, int Height, int Seed);
public record BiomeMap(BiomeType[,] Biomes, int Width, int Height, int Seed);
public record RiverMap(BiomeType[,] Biomes, List<Point> RiverTiles, int Width, int Height, int Seed);
public record InfluencedMap(BiomeType[,] Biomes, List<Point> RiverTiles, int Width, int Height);
public record TileMap(Tile[,] Tiles, int Width, int Height);

public enum BiomeType { Water, Plains, Forest }
public record Tile(TileType Type, bool Walkable);
public enum TileType { Grass, Water, Tree, River, POIMarker, ExitMarker, TownCenter }
```

### 2. World State Architecture

#### Modified GameLoopService

```csharp
public class GameLoopService : BackgroundService
{
    // World management
    private readonly Dictionary<string, World> _worlds = new();
    private World _overworld; // Reference to the main overworld

    // Player tracking
    private readonly ConcurrentDictionary<string, PlayerState> _players = new();
}

public class PlayerState
{
    public string Id { get; set; }
    public string Name { get; set; }
    public string CurrentWorldId { get; set; }
    public Point Position { get; set; }
    public Point LastOverworldPosition { get; set; } // For reconnection
}

public class World
{
    public string Id { get; init; }
    public WorldType Type { get; init; } // Overworld or Instance
    public TileMap Terrain { get; init; }
    public List<POI> POIs { get; init; }
    public List<Entity> Entities { get; init; }
    public Point? ExitMarker { get; init; } // Only for instances
    public string ParentPOIId { get; init; } // Links instance to overworld POI
}

public enum WorldType { Overworld, Instance }
```

#### POI Model

```csharp
[MessagePackObject]
public record POI
{
    [Key(0)] public string Id { get; init; }
    [Key(1)] public Point Position { get; init; }
    [Key(2)] public POIType Type { get; init; }
    [Key(3)] public float InfluenceRadius { get; init; } // How far it clears obstacles
}

public enum POIType { Camp, Town, Dungeon }
```

### 3. WorldSnapshot Changes

```csharp
[MessagePackObject]
public record WorldSnapshot
{
    [Key(0)] public long Tick { get; init; }
    [Key(1)] public string WorldId { get; init; }
    [Key(2)] public WorldType WorldType { get; init; }
    [Key(3)] public List<Entity> Entities { get; init; }
    [Key(4)] public TileData[,] Tiles { get; init; } // Full terrain grid
    [Key(5)] public List<POI> POIs { get; init; }
    [Key(6)] public Point? ExitMarker { get; init; }
    [Key(7)] public int Width { get; init; }
    [Key(8)] public int Height { get; init; }
}

[MessagePackObject]
public record TileData([property: Key(0)] TileType Type, [property: Key(1)] bool Walkable);
```

### 4. Instance Lifecycle

#### Creation (on POI entry)
1. Player stands on POI tile, presses Enter
2. Server checks if instance exists for that POI ID
3. If not: generate instance terrain using parent tile's biome as seed parameter
4. Teleport player to random walkable tile in instance
5. Remove player entity from overworld
6. Broadcast updated snapshots to both worlds

#### Destruction (on empty)
1. Player stands on exit marker, presses Enter
2. Teleport player to `LastOverworldPosition`
3. Add player entity back to overworld
4. If instance has no remaining players: destroy instance, remove from `_worlds`

### 5. Fractal Consistency

When generating an instance, inherit parameters from the parent overworld tile:

```csharp
public static class InstanceGenerator
{
    public static TileMap GenerateInstance(Point overworldCoord, BiomeType parentBiome, int worldSeed)
    {
        // Derive instance seed from world seed + coordinates
        int instanceSeed = HashCode.Combine(worldSeed, overworldCoord.X, overworldCoord.Y);

        // Adjust generation parameters based on parent biome
        float densityThreshold = parentBiome switch
        {
            BiomeType.Forest => 0.4f,  // More trees
            BiomeType.Plains => 0.7f,  // Fewer obstacles
            BiomeType.Water => 0.5f,   // Shouldn't happen, but handle it
            _ => 0.6f
        };

        return new TerrainSeed(instanceSeed, 50, 50)
            .GenerateNoise()
            .ToBiomes(densityThreshold)
            .CarveRivers(Edge.North, Edge.South) // If parent had river
            .ApplyInfluenceMasks(new[] { exitPOI })
            .ToTileMap();
    }
}
```

### 6. A* River Carving

```csharp
public static class RiverCarver
{
    public static RiverMap CarveRivers(this BiomeMap biomes, Edge startEdge, Edge endEdge)
    {
        // 1. Pick random start point on startEdge
        // 2. Pick random end point on endEdge
        // 3. A* pathfind with cost function:
        //    - Water tiles: cost 1 (prefer existing water)
        //    - Plains tiles: cost 5 (okay to cross)
        //    - Forest tiles: cost 10 (avoid dense areas)
        // 4. Mark path tiles as River
        // 5. Return RiverMap with river tile list
    }
}
```

### 7. Influence Mask

```csharp
public static class InfluenceMask
{
    public static InfluencedMap ApplyInfluenceMasks(this RiverMap map, IEnumerable<POI> pois)
    {
        foreach (var poi in pois)
        {
            // Clear obstacles within poi.InfluenceRadius
            // Convert Forest → Plains within radius
            // Keep water as-is (or convert to shallow water?)
        }
        return new InfluencedMap(...);
    }
}
```

### 8. Ghost Chunks (World Boundaries)

```csharp
// In terrain generation, extend the noise beyond boundaries
public static NoiseMap GenerateNoise(this TerrainSeed seed)
{
    // Generate extra "ghost" tiles beyond Width/Height
    int ghostPadding = 20; // Visible but not walkable
    float[,] values = new float[seed.Width + ghostPadding * 2, seed.Height + ghostPadding * 2];
    // ... generate noise
    return new NoiseMap(values, seed.Width, seed.Height, ghostPadding, seed.Seed);
}

// Collision check respects actual boundaries, not ghost area
public bool IsWalkable(Point position)
{
    if (position.X < 0 || position.X >= Width ||
        position.Y < 0 || position.Y >= Height)
        return false; // Ghost area - visible but blocked
    return Tiles[position.X, position.Y].Walkable;
}
```

### 9. Camera System (game.js)

```javascript
// Current: fixed offset (+400, +300)
// New: offset based on player position

function renderSnapshot(snapshot, playerId) {
    const player = snapshot.entities.find(e => e.id === playerId);
    if (player) {
        cameraX = 400 - (player.position.x * TILE_SIZE);
        cameraY = 300 - (player.position.y * TILE_SIZE);
    }

    // Apply camera offset to all rendered elements
    worldContainer.x = cameraX;
    worldContainer.y = cameraY;
}
```

### 10. SignalR Hub Changes

```csharp
public class GameHub : Hub
{
    // Existing
    public Task Join(string name) { ... }
    public Task Move(Direction direction) { ... }

    // New
    public Task Interact() // Enter key - enter POI or exit instance
    {
        var player = _gameLoop.GetPlayer(Context.ConnectionId);
        var world = _gameLoop.GetWorld(player.CurrentWorldId);

        if (world.Type == WorldType.Overworld)
        {
            var poi = world.POIs.FirstOrDefault(p => p.Position == player.Position);
            if (poi != null)
                _gameLoop.EnterInstance(player, poi);
        }
        else // Instance
        {
            if (player.Position == world.ExitMarker)
                _gameLoop.ExitInstance(player);
        }
    }
}
```

### 11. Client UI Changes (Home.razor)

```razor
@* HTML overlay for interaction prompt *@
@if (ShowInteractionPrompt)
{
    <div class="interaction-prompt">
        Press Enter to @InteractionText
    </div>
}

@code {
    private bool ShowInteractionPrompt => CurrentPOI != null || IsOnExit;
    private string InteractionText => IsOnExit ? "Exit" : $"Enter {CurrentPOI?.Type}";
}
```

### 12. Rendering Changes (game.js)

```javascript
// Tile type to sprite mapping
const TILE_SPRITES = {
    Grass: { x: 0, y: 0 },      // Pick from tileset
    Water: { x: 1, y: 0 },
    Tree: { x: 2, y: 0 },
    River: { x: 3, y: 0 },
    POIMarker: { x: 4, y: 0 },
    ExitMarker: { x: 5, y: 0 },
    TownCenter: { x: 6, y: 0 }
};

function renderTerrain(tiles, width, height) {
    // Clear existing terrain sprites
    // For each tile in viewport + buffer:
    //   Create/reuse sprite from TILE_SPRITES mapping
    //   Position at tile coordinates * TILE_SIZE + camera offset
}
```

---

## Implementation Order

1. **Pipeline Foundation**
   - Create record types in Mud.Shared/Generation/
   - Implement extension methods in Mud.Server/Generation/
   - Start with NoiseMap → BiomeMap (no rivers yet)

2. **World State Refactor**
   - Refactor GameLoopService to support multiple worlds
   - Add PlayerState tracking
   - Update WorldSnapshot model

3. **Overworld Generation**
   - Generate 150x150 overworld on server start
   - Implement influence mask for spawn town
   - Add ghost chunk boundaries

4. **Rendering Updates**
   - Update game.js to render tile types
   - Implement camera follow
   - Add POI marker sprites

5. **Instance System**
   - Implement POI generation on overworld
   - Add Enter key handling (SignalR + client)
   - Implement instance creation/destruction
   - Add exit marker generation and handling

6. **River System**
   - Implement A* river carving
   - Apply to both overworld and instances
   - Test fractal consistency

7. **Polish**
   - HTML interaction prompts
   - Ghost chunk visibility
   - Edge case handling (disconnect, reconnect)

---

## Configuration Constants

```csharp
public static class WorldConfig
{
    public const int WorldSeed = 12345; // Hardcoded for now
    public const int OverworldWidth = 150;
    public const int OverworldHeight = 150;
    public const int InstanceWidth = 50;
    public const int InstanceHeight = 50;
    public const int GhostPadding = 20;

    // Biome thresholds
    public const float WaterThreshold = 0.3f;
    public const float PlainsThreshold = 0.6f;

    // POI generation
    public const float POIDensity = 0.02f; // 2% of walkable tiles
    public const int MinPOIDistance = 10; // Minimum tiles between POIs
    public const float TownInfluenceRadius = 15f;
    public const float CampInfluenceRadius = 5f;
}
```

---

## Edge Cases

| Scenario | Handling |
|----------|----------|
| Player disconnects in instance | On reconnect, spawn at `LastOverworldPosition` |
| Last player leaves instance | Destroy instance immediately |
| Two players enter same POI | Both end up in same instance (create if needed) |
| Player tries to walk into ghost area | Movement blocked (collision check fails) |
| POI spawns on water/obstacle | POI generation only picks walkable tiles |
| Exit marker spawns on obstacle | Influence mask clears area around exit |
| Instance biome is Water | Shouldn't happen - POIs only on walkable overworld tiles |

---

## Files to Create/Modify

### New Files
- `Mud.Shared/Generation/TerrainRecords.cs` - Pipeline record types
- `Mud.Shared/Generation/POI.cs` - POI model
- `Mud.Server/Generation/NoiseGenerator.cs` - Perlin noise
- `Mud.Server/Generation/BiomeClassifier.cs` - Noise → Biome
- `Mud.Server/Generation/RiverCarver.cs` - A* river pathfinding
- `Mud.Server/Generation/InfluenceMask.cs` - POI clearing
- `Mud.Server/Generation/TerrainPipeline.cs` - Extension methods
- `Mud.Server/Services/World.cs` - World state class
- `Mud.Server/Services/PlayerState.cs` - Player tracking

### Modified Files
- `Mud.Shared/Models.cs` - Update WorldSnapshot, add TileData
- `Mud.Server/Services/GameLoopService.cs` - Multi-world support
- `Mud.Server/Hubs/GameHub.cs` - Add Interact method
- `Mud.Client/Pages/Home.razor` - Enter key handling, prompt UI
- `Mud.Server/wwwroot/game.js` - Terrain rendering, camera follow
