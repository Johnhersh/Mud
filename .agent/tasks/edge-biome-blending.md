# Edge Biome Blending

## Objective

Enhance instance edge generation to smoothly blend neighboring overworld biomes using density-based noise reinterpretation with distance falloff.

## Problem Statement

Currently, instance edges reflect neighboring overworld tiles but in a crude way - the entire edge is painted as the neighbor's biome (e.g., full forest). This creates unnatural hard boundaries. The existing noise system already creates variation, but it's not being leveraged to create natural transitions.

## Product Definition

### Approach: Density Bumping

Instead of replacing edge tiles wholesale, reinterpret the existing noise values based on proximity to edges. The neighboring biome "boosts" or shifts the noise threshold:

- Low-density plains near a forest edge → trees spawn where grass would normally be
- The noise pattern stays intact, preserving natural-looking variation
- Only the interpretation changes based on edge proximity

### Falloff Behavior

- Use the existing edge padding value to define how far the influence extends
- Falloff should be smooth (not linear cutoff) - tiles closer to the edge are more strongly influenced

### Corner Handling

Corners look at 3 neighboring tiles (cardinal + diagonal). Current behavior creates hard "squares" when the diagonal differs from cardinals.

**Solution: Distance-weighted influence**
- Cardinal neighbors (left, bottom) are closer to the instance edge than the diagonal (bottom-left)
- Diagonal is √2 times further → ~30% less influence
- At corners, blend all 3 neighbors weighted by inverse distance to their tile centers
- Result: forest-forest-water corner becomes mostly foresty with subtle water influence, not a hard square

### Biome Compatibility

All biomes can blend into all other biomes via density adjustment. No hard boundaries or special-case pairs.

## Success Criteria

1. Instance edges show gradual transitions into neighboring biomes
2. Existing noise patterns are preserved (terrain doesn't look "different", just reinterpreted)
3. Corners blend smoothly rather than showing hard squares
4. Edge padding value controls the blend distance

## Technical Approach

### Core Concept: Noise Value Shifting

Instead of adjusting classification thresholds, shift the noise value toward the neighbor's "target noise" before classification. Each biome has a target representing its center:

| Biome  | Target Noise | Range     |
|--------|--------------|-----------|
| Water  | 0.15         | 0.0 - 0.3 |
| Plains | 0.45         | 0.3 - 0.6 |
| Forest | 0.75         | 0.6 - 1.0 |

Near edges, noise is pulled toward the neighbor's target:
```csharp
adjustedNoise = Lerp(originalNoise, neighborTarget, influence * blendStrength)
```

### Neighbor Direction Mapping

The 8 overworld neighbors relative to POI position:
```
         North (0, -1)
            │
West (-1,0)─┼─East (+1, 0)
            │
         South (0, +1)
```
Diagonals: NW (-1,-1), NE (+1,-1), SW (-1,+1), SE (+1,+1)

Instance array layout (60×60 for 50×50 playable + 5 padding):
- North edge: y ∈ [0, 4] (ghost) and [5, 9] (playable influence zone)
- South edge: y ∈ [55, 59] (ghost) and [50, 54] (playable influence zone)
- West edge: x ∈ [0, 4] (ghost) and [5, 9] (playable influence zone)
- East edge: x ∈ [55, 59] (ghost) and [50, 54] (playable influence zone)

### Influence Calculation

**Distance to edge:**
```
distanceToWest = x - ghostPadding
distanceToEast = (width + ghostPadding - 1) - x
distanceToNorth = y - ghostPadding
distanceToSouth = (height + ghostPadding - 1) - y
```

Ghost padding positions have negative distance (past the edge) → maximum influence.

**Smoothstep falloff:**
```csharp
// Continuous falloff from outer ghost edge to inner influence boundary
int influenceRange = ghostPadding * 2;
float linearFalloff = Math.Clamp(1f - (distance + ghostPadding) / (float)influenceRange, 0f, 1f);
float influence = Smoothstep(linearFalloff);

static float Smoothstep(float x) => x * x * (3f - 2f * x);
```

### Multi-Edge Blending (Corners)

When a position is influenced by multiple edges, each neighbor (cardinal AND diagonal) contributes its own biome target weighted by its influence:

1. Calculate influence for each cardinal edge (West, East, North, South)
2. Diagonal neighbors contribute when BOTH adjacent cardinals have influence
3. Diagonal weight = min of the two adjacent cardinal influences × 0.707 (√2 penalty)
4. Compute weighted average of all contributing biome targets

```csharp
// Example: position near SW corner
float westInfluence = CalculateInfluence(distanceToWest);
float southInfluence = CalculateInfluence(distanceToSouth);
float swInfluence = Math.Min(westInfluence, southInfluence) * 0.707f;

// Each neighbor contributes its own biome's target noise
float targetSum = 0f;
float weightSum = 0f;

if (westInfluence > 0) {
    targetSum += BiomeTarget(westNeighborBiome) * westInfluence;
    weightSum += westInfluence;
}
if (southInfluence > 0) {
    targetSum += BiomeTarget(southNeighborBiome) * southInfluence;
    weightSum += southInfluence;
}
if (swInfluence > 0) {
    targetSum += BiomeTarget(swNeighborBiome) * swInfluence;
    weightSum += swInfluence;
}

// totalInfluence = weightSum clamped to 1
// More edges = stronger pull toward blended target
float totalInfluence = Math.Min(1f, weightSum);
float weightedTarget = targetSum / weightSum;
float adjustedNoise = Lerp(originalNoise, weightedTarget, totalInfluence * blendStrength);
```

**Example scenario:** West=Forest, South=Forest, SW=Water
- West contributes target 0.75 (Forest)
- South contributes target 0.75 (Forest)
- SW diagonal contributes target 0.15 (Water) with reduced weight

Result: Corner blends toward mostly forest with some water influence, not a hard square.

### Pipeline Integration

Current flow:
```csharp
.ToBiomesWithDensity(densityThreshold)
.WithOverworldContext(overworldTerrain, poi.Position)
```

New flow - pass density threshold for reclassification:
```csharp
.ToBiomesWithDensity(densityThreshold)
.WithOverworldContext(overworldTerrain, poi.Position, densityThreshold)
```

`WithOverworldContext` now:
1. Early exit if ghostPadding = 0
2. Cache all 8 neighbor biomes (with fallback to parent biome for nulls)
3. For each position in the array:
   - Calculate influence from each edge (skip if all zero)
   - Compute weighted target noise from all influencing neighbors
   - Shift noise: `adjusted = Lerp(original, weightedTarget, totalInfluence * blendStrength)`
   - Reclassify using same `densityThreshold` as initial classification

## Code Patterns to Follow

- **Pipeline pattern**: `WithOverworldContext` returns `BiomeMap` (mutates in place, which is acceptable for large arrays)
- **Switch expressions**: Use for biome-to-target mapping
- **Constants in WorldConfig**: All tunable values (targets, blend strength) go there
- **Extension methods**: Keep the fluent pipeline style

### Helper Methods Needed

```csharp
// Get biome target noise value
static float BiomeTarget(BiomeType biome) => biome switch
{
    BiomeType.Water => WorldConfig.BiomeTargetWater,
    BiomeType.Plains => WorldConfig.BiomeTargetPlains,
    BiomeType.Forest => WorldConfig.BiomeTargetForest,
    _ => WorldConfig.BiomeTargetPlains
};

// Reclassify noise to biome (same logic as ToBiomesWithDensity)
static BiomeType ClassifyBiome(float noise, float densityThreshold) => noise switch
{
    < WorldConfig.WaterThreshold => BiomeType.Water,
    var v when v < densityThreshold => BiomeType.Plains,
    _ => BiomeType.Forest
};

// Get neighbor biome with null fallback
static BiomeType GetNeighborBiome(TileMap overworld, Point poi, int dx, int dy, BiomeType fallback)
{
    var tile = overworld.GetTile(new Point(poi.X + dx, poi.Y + dy));
    return tile is not null ? TileToBiome(tile.Type) : fallback;
}
```

**Note:** The implementation must process all 8 neighbors (4 cardinal + 4 diagonal). The pseudocode example only shows SW corner for brevity.

## Edge Cases

| Case | Handling |
|------|----------|
| Neighbor tile is null (overworld edge) | Use parent biome from `overworld.GetTile(poiPosition)` as fallback |
| Adjusted noise < 0 or > 1 | Clamp to valid range |
| Position far from all edges (weightSum = 0) | Skip blending, keep original biome |
| All neighbors same biome | Uniform shift toward that biome (correct behavior) |
| ghostPadding = 0 | Guard: skip all blending (influenceRange would be 0) |
| Center tile null (shouldn't happen) | Default to Plains |

## Technical Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Mutation vs immutability | Mutate BiomeMap in place | Large 2D arrays; no benefit to copying |
| totalInfluence calculation | `Min(1, weightSum)` | Reuses existing calculation; multiple edges compound influence |
| Biome targets | Fixed (0.15, 0.45, 0.75) | Simple; overworld uses standard thresholds |
| Influence range | `ghostPadding * 2` | Extends equally into ghost padding and playable area |
| Diagonal weight penalty | 0.707 (1/√2) | Geometric distance ratio |
| Update noise array? | No - only update Biomes | Original noise creates natural density gradients at edges (sparse trees at forest edges, dense grass at plains edges) |

## Implementation Considerations

**Performance:** Iterating the full 60×60 array (3600 positions) with 8 influence calculations each is negligible for instance generation. No optimization needed.

**Neighbor caching:** Cache all 8 neighbor biomes at method start. Don't look up `overworld.GetTile()` per-position.

**Preserve non-edge positions:** Positions with `weightSum = 0` should keep their original biome unchanged. Don't reclassify everything.

**Threshold consistency:** Use the same `densityThreshold` for reclassification that `ToBiomesWithDensity` used initially.

## Files to Modify

| File | Changes |
|------|---------|
| `Mud.Core/World/WorldConfig.cs` | Add `BiomeTargetWater`, `BiomeTargetPlains`, `BiomeTargetForest`, `EdgeBlendStrength` constants |
| `Mud.Server/World/Generation/OverworldContext.cs` | Rewrite to implement noise shifting with smoothstep falloff and corner weighting |
| `Mud.Server/World/Generation/WorldGenerator.cs` | Pass `densityThreshold` to `WithOverworldContext` |

## How to Test

### Step 1: Code Quality Review

After implementation, run the code quality reviewer:

```
Task(
  subagent_type: "code-quality-reviewer",
  description: "Review edge blending code",
  prompt: """
  Review the code changes for edge biome blending.

  **Files changed:**
  - Mud.Core/World/WorldConfig.cs
  - Mud.Server/World/Generation/OverworldContext.cs
  - Mud.Server/World/Generation/WorldGenerator.cs

  **What to look for:**
  - Any #pragma directives
  - Proper null handling for neighbor tiles
  - Correct smoothstep implementation
  - Division by zero guard (weightSum = 0)
  """
)
```

### Step 2: Visual/Functional Testing

```
Task(
  subagent_type: "playwright-tester",
  description: "Test edge biome blending",
  prompt: """
  **Feature:** Instance edges blend smoothly with neighboring overworld biomes

  **Test Steps:**
  1. Navigate to http://localhost:5213/game
  2. Log in and enter the game
  3. Move to find a POI marker (yellow @) near a biome boundary (where forest meets plains or water)
  4. Press Enter to enter the instance
  5. Move to the edges of the instance to observe terrain

  **Visual Verification:**
  - Instance edges should NOT be solid blocks of a single biome
  - Edges should show gradual transitions with noise variation
  - Corners where two different biomes meet should blend smoothly, not show hard squares
  - The transition should feel natural, not artificial
  """
)
```
