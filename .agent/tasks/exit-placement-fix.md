# Fix Instance Exit Placement on Impassable Terrain

## Objective

Ensure instance exit points are always placed on walkable tiles so players can reach them and leave the instance.

## Problem Statement

When instances are generated, the exit portal is placed at a fixed position `(Width/2, Height-2)` without checking terrain walkability. If that position lands on water or forest (trees), the exit is unreachable and players are soft-locked.

## Success Criteria

- Exit portals are always placed on walkable tiles
- If the original exit position is impassable, the system finds the nearest walkable tile using spiral search
- Search continues until a valid tile is found (no radius limit)
- Players can always reach and use the exit

## Technical Approach

### Root Cause

In `POIPlacer.PlaceExitMarker()` (line 80), the exit position is hardcoded:
```csharp
Position = new Point(map.Width / 2, map.Height - 2)
```

This happens **before** checking if the position is walkable. The existing `IsWalkable()` helper (line 92) checks for `BiomeType.Plains` but is never called for exit placement.

### Solution

1. **Add a reusable `FindNearestWalkablePosition()` helper** to `POIPlacer.cs`
   - Takes a `BiomeMap` and a starting `Point`
   - Uses spiral search pattern expanding outward
   - Returns the first walkable position found
   - No radius limit — searches until found

2. **Modify `PlaceExitMarker()`** to use the helper
   - Start with intended position `(Width/2, Height-2)`
   - If not walkable, call `FindNearestWalkablePosition()`
   - Place exit at the returned position

### Spiral Search Algorithm

```
Start at center, then visit positions in this order:
(0,0) → (1,0) → (1,1) → (0,1) → (-1,1) → (-1,0) → (-1,-1) → (0,-1) → (1,-1) → (2,-1) → ...
```

Pattern: For each "ring" distance d from 1 to max:
- Move right along top edge
- Move down along right edge
- Move left along bottom edge
- Move up along left edge

## Code Patterns to Follow

From existing `POIPlacer.cs`:
- Use `IsWalkable(BiomeMap map, Point pos)` pattern for walkability checks
- Account for `GhostPadding` when accessing biome array
- Keep methods `private static` unless needed externally

## Edge Cases

| Edge Case | Handling |
|-----------|----------|
| Original position is walkable | Return immediately, no search needed |
| Exit deep in lake/forest | Spiral expands until walkable tile found |
| Entire instance non-walkable | Theoretically impossible given generation, but spiral would search entire map |

## Technical Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Search algorithm | Spiral | Simpler, resource-efficient, deterministic |
| Reusable helper | Yes | Can be used by future features |
| Search limit | None | Guarantees exit is always reachable |

## Files to Modify

| File | Changes |
|------|---------|
| `Mud.Server/World/Generation/POIPlacer.cs` | Add `FindNearestWalkablePosition()` helper; modify `PlaceExitMarker()` to use it |

## How to Test

After implementation is complete, spawn the `playwright-tester` agent:

```
Task(
  subagent_type: "playwright-tester",
  description: "Test exit placement",
  prompt: """
  **Feature:** Exit placement on walkable tiles

  **Test Steps:**
  1. Navigate to http://localhost:5148
  2. Wait for the game to load (canvas visible)
  3. Press Enter → Should transition to an instance
  4. Take a screenshot to see the instance terrain and exit marker
  5. Use arrow keys to navigate toward the exit (look for the exit marker tile)
  6. Verify the exit is on a walkable tile (grass, not trees/water)
  7. Walk onto the exit tile to confirm it's reachable

  **Visual Verification:**
  - Exit marker should be visible on the map
  - Exit should be on grass terrain (not blocked by trees or water)
  - Player should be able to walk directly to the exit
  - No soft-lock scenario where exit is surrounded by impassable terrain
  """
)
```

## Implementation Notes

- The spiral search is deterministic given the same starting position
- Instance seed is based on world seed + POI position, so regenerating the same instance will place the exit in the same (now valid) position
- No changes needed to client code — exit position is already sent via `WorldSnapshot.ExitMarker`
