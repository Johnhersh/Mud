# Fix Instance Exit Placement

## Objective

Ensure instance exit portals always spawn within the playable area, not in the ghost padding.

## Problem Statement

Instance exits are sometimes placed within the ghost padding area around instance edges. Since players cannot enter the ghost padding, this makes the instance impossible to exit, trapping players.

## Success Criteria

- Exit portals always spawn inside the playable boundary (outside ghost padding)
- Existing placement rules still apply (not on walls, not on monsters, etc.)
- No regression in other spawn placement logic

## Technical Approach

### Root Cause

`POIPlacer.IsWalkable()` only validates array bounds after applying ghost padding offset. It doesn't check if the position is within the logical playable area (0 to Width-1, 0 to Height-1).

When `FindNearestWalkablePosition()` spiral-searches outward from an unwalkable starting point, it can find positions in the ghost padding zone (e.g., `(-2, 48)` or `(25, 52)` for a 50x50 instance) and incorrectly report them as walkable.

### Fix

Add logical bounds check to `POIPlacer.IsWalkable()`, matching the pattern used in `TerrainPipeline.IsWalkable()`:

```csharp
private static bool IsWalkable(BiomeMap map, Point pos)
{
    // Check logical playable area bounds (excludes ghost padding)
    if (pos.X < 0 || pos.X >= map.Width || pos.Y < 0 || pos.Y >= map.Height)
        return false;

    int x = pos.X + map.GhostPadding;
    int y = pos.Y + map.GhostPadding;

    return map.Biomes[x, y] == BiomeType.Plains;
}
```

This is a one-line addition that mirrors the working implementation.

## Files to Modify

- `Mud.Server/World/Generation/POIPlacer.cs` - Add logical bounds check to `IsWalkable()` method (line ~143)

## How to Test

### Step 1: Code Quality Review

After implementation, run the code quality reviewer on modified files.

### Step 2: Visual/Functional Testing

```
Task(
  subagent_type: "playwright-tester",
  description: "Test instance exit accessibility",
  prompt: """
  **Feature:** Instance exit placement fix

  **Test Steps:**
  1. Navigate to http://localhost:5213
  2. Log in and enter the game
  3. Press Enter to enter a town instance
  4. Locate the exit portal visually
  5. Walk to the exit portal and confirm it's reachable

  **Visual Verification:**
  - Exit portal is visible and not at the extreme edges of the instance
  - Player can walk to and stand on the exit portal
  - No areas of the instance are unreachable
  """
)
```

*Note: May need multiple test runs since placement is procedural*
