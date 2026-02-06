# Integration Testing Infrastructure

## Objective

Establish a server-side integration testing framework and refactor `GameLoopService` to extract game logic into independently testable extension methods on `WorldState`, enabling future tasks to define product-level acceptance criteria that are verified by automated tests.

## Problem Statement

`GameLoopService` is a 900+ line monolith that owns all game logic (movement, combat, XP, leveling, instance transitions) alongside infrastructure concerns (SignalR broadcasting, persistence queuing, tick timing). This makes it impossible to test game logic without standing up the full server. There are currently zero automated tests in the project.

## Success Criteria

1. `dotnet test` runs and passes with at least one meaningful integration test (e.g., bump combat deals correct damage)
2. `GameLoopService` is a thin orchestrator: tick loop, SignalR broadcast, persistence — no game logic
3. Game logic lives in extension methods on `WorldState` (or a small number of focused static methods) that tests can call directly
4. A `GameState` class encapsulates all mutable game state (worlds, sessions, input queues, event collectors) independent of infrastructure
5. Tests create their own `GameState` and `WorldState`, populate them, call game logic, and assert outcomes — no SignalR, no database, no tick timer
6. The existing game works identically after refactoring (no behavioral changes)
7. TASK_PLANNING.md and AGENTS.md are updated to reflect the new testing workflow

## Technical Approach

### 1. Create `GameState` class

Extract all game-logic state from `GameLoopService` into a standalone class:

```csharp
// Mud.Server/Services/GameState.cs
public class GameState
{
    public ConcurrentDictionary<WorldId, WorldState> Worlds { get; } = new();
    public WorldState Overworld { get; set; } = null!;
    public ConcurrentDictionary<string, PlayerSession> Sessions { get; } = new();
    public ConcurrentDictionary<string, ConcurrentQueue<Direction>> PlayerInputQueues { get; } = new();

    // Event collectors (cleared after each broadcast)
    public ConcurrentDictionary<WorldId, ConcurrentBag<AttackEvent>> AttackEvents { get; } = new();
    public ConcurrentDictionary<WorldId, ConcurrentDictionary<string, List<XpGainEvent>>> XpEvents { get; } = new();
    public ConcurrentDictionary<WorldId, ConcurrentBag<LevelUpEvent>> LevelUpEvents { get; } = new();
    public ConcurrentDictionary<string, ProgressionUpdate> ProgressionUpdates { get; } = new();
}
```

### 2. Upgrade to .NET 10 / C# 14

The project currently targets .NET 9 and runs in a dev container based on `mcr.microsoft.com/devcontainers/dotnet:1-9.0-bookworm`. Update `.devcontainer/Dockerfile` to use the .NET 10 base image. Upgrade all `.csproj` files to `net10.0` to enable C# 14 extension member syntax. Update NuGet packages as needed.

### 3. Extract game logic into extension members

Move `UpdateWorld`, `ProcessAttack`, `AwardXpToInstance`, and related logic out of `GameLoopService` into extension members using C# 14 syntax. The key insight: these methods operate on `WorldState` + `GameState` and need `ICharacterCache` for stat lookups.

Uses the C# 14 `extension` block syntax — groups all WorldState extensions under a single receiver declaration:

```csharp
// Mud.Server/World/WorldUpdateExtensions.cs (or similar)
public static class WorldUpdateExtensions
{
    extension(WorldState world)
    {
        public void UpdateWorld(GameState state, ICharacterCache cache) { ... }
        public void ProcessAttack(GameState state, string attackerId, string targetId, bool isMelee, ICharacterCache cache) { ... }
        // etc.
    }
}
```

Reference: https://devblogs.microsoft.com/dotnet/csharp-exploring-extension-members/

The persistence queuing (`_pendingPersistenceOps`) stays in `GameLoopService` — it reads the events from `GameState` after the tick and handles DB writes.

### 4. Slim down `GameLoopService`

After extraction, `GameLoopService` becomes:
- Owns a `GameState` instance
- Tick loop: calls extension methods on each world, passing `GameState` + `ICharacterCache`
- After tick: reads events from `GameState`, broadcasts via SignalR, queues persistence ops, clears events
- Player join/leave: still managed here (interfaces with SignalR hub)

### 5. Create test project: `Mud.Tests`

- TUnit test framework (single `TUnit` NuGet package)
- References `Mud.Server` and `Mud.Core`
- Flat structure by feature: `MovementTests.cs`, `CombatTests.cs`, etc.
- Contains `TestCharacterCache` — a simple in-memory `ICharacterCache` implementation
- Contains test helpers for creating minimal worlds and entities

### 6. Write initial integration tests

A small set of tests proving the pattern works:

- **Movement**: Player moves to walkable tile, position updates
- **Wall collision**: Player moves into wall, position unchanged
- **Bump combat**: Player moves into monster, monster takes `5 + Strength` damage
- **Monster death**: Monster at 0 HP is removed from world
- **XP award**: Killing a monster grants XP to all players in instance

### 7. Update workflow documentation

- **TASK_PLANNING.md**: Phase 1 "How to Test" becomes acceptance criteria in plain language. Remove playwright-tester references from the template.
- **AGENTS.md**: Document that `dotnet test` is the primary verification method. Update testing section. Keep playwright-tester agent file but stop referencing it in workflow.

## Acceptance Criteria (for this task's own tests)

These are the product-level truths that the initial test suite should verify:

1. A player entity with a queued movement direction, after a world update, ends up at the expected position
2. A player entity that moves into a non-walkable tile stays at their original position
3. A player entity that moves into a monster's tile triggers combat — the monster loses HP equal to `5 + attacker's Strength`
4. A monster reduced to 0 or fewer HP is removed from the world
5. When a monster dies, an XP event is recorded for each player in that world

## Edge Cases

- **Empty input queue**: UpdateWorld should handle players with no queued input gracefully (skip them)
- **Dead entity mid-tick**: If ProcessAttack kills a monster, subsequent references to that entity should be safe
- **Multiple players in world**: XP award should apply to all players, not just the attacker
- **Monster stat lookup**: `GetAttackStats` for monsters uses `MonsterStats` (static) — no cache needed. Only player stats need `ICharacterCache`.

## Technical Decisions

- **C# 14 extension member syntax** — uses `extension(WorldState world) { }` blocks instead of `this` parameter syntax. Requires .NET 10 upgrade.
- **Extension methods rather than a GameEngine class** — keeps the code functional-style and composable, consistent with the WorldGenerator pipeline pattern
- **`ICharacterCache` injected directly** rather than creating a new abstraction — the interface already exists in Mud.Core and is clean
- **TUnit** — modern .NET test framework with source-generated tests, parallel by default, single NuGet package. Good fit for greenfield. Reference: https://tunit.dev
- **No mocking framework initially** — `TestCharacterCache` is trivial to write by hand. Add NSubstitute later if needed.

## Files to Modify

- **Modify**: `.devcontainer/Dockerfile` — upgrade base image to .NET 10
- **Modify**: All `.csproj` files — upgrade `TargetFramework` from `net9.0` to `net10.0`
- **New**: `Mud.Tests/Mud.Tests.csproj` — test project
- **New**: `Mud.Tests/TestCharacterCache.cs` — in-memory ICharacterCache
- **New**: `Mud.Tests/TestHelpers.cs` — world/entity creation helpers
- **New**: `Mud.Tests/WorldUpdateTests.cs` — initial integration tests
- **New**: `Mud.Server/Services/GameState.cs` — extracted game state
- **New**: `Mud.Server/World/WorldUpdateExtensions.cs` — extracted game logic (name TBD during implementation)
- **Modify**: `Mud.Server/Services/GameLoopService.cs` — slim down to orchestrator
- **Modify**: `Mud.sln` — add test project
- **Modify**: `.agent/TASK_PLANNING.md` — update How to Test template
- **Modify**: `AGENTS.md` — update testing documentation

## How to Test

### Acceptance Criteria

1. Player moves to adjacent walkable tile → position updates to new tile
2. Player moves into wall → position unchanged
3. Player bumps into monster → monster HP decreases by `5 + Strength`
4. Monster HP reaches 0 → monster removed from world entities
5. Monster killed → XP event recorded for all players in that world
6. `dotnet build` succeeds with no errors
7. `dotnet test` passes all tests
8. Manual smoke test: game still plays identically in browser (movement, combat, XP, instances all work)

### Step 1: Code Quality Review

```
Task(
  subagent_type: "code-quality-reviewer",
  description: "Review testing infrastructure",
  prompt: """
  Review the code changes for the integration testing infrastructure and GameLoopService refactor.

  **Files changed:**
  - Mud.Server/Services/GameState.cs (new)
  - Mud.Server/World/WorldUpdateExtensions.cs (new)
  - Mud.Server/Services/GameLoopService.cs (modified - slimmed down)
  - Mud.Tests/*.cs (new test project)
  - .agent/TASK_PLANNING.md (updated)
  - AGENTS.md (updated)

  **What to look for:**
  - Game logic in extension methods matches original GameLoopService behavior exactly
  - No state leaking between GameState and GameLoopService
  - Tests are meaningful (not just testing trivial things)
  - Documentation accurately reflects the new workflow
  """
)
```

### Step 2: Automated Tests

Run `dotnet test` and verify all tests pass.
