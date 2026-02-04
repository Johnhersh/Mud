# Agent Guide: Mud

This repository contains "Mud", a web-based MMO prototype featuring a retro-futuristic ASCII visual style. It uses a Blazor WebAssembly frontend, an ASP.NET Core backend, and SignalR for real-time communication.

## 🏗 Project Structure

The solution is divided into these projects:

- **Mud.Server**: ASP.NET Core Web App. Handles game simulation, authoritative state, and broadcasts world snapshots.
- **Mud.Client**: Blazor WebAssembly app. Manages UI, input handling, and rendering via Phaser 4.
- **Mud.Core**: C# Class Library containing shared models, enums, and service interfaces used by both Client and Server.
  - `Mud.Core.Models`: Domain entities (`ApplicationUser`, `CharacterEntity`) and Identity integration.
- **Mud.Infrastructure**: EF Core implementations (`MudDbContext`, `PersistenceService`, `CharacterCache`).
- **Mud.DependencyInjection**: Service registration and configuration.

## 🏛 Architectural Principles

These principles guide all development decisions. Follow them unless there's a compelling reason not to.

### Static SSR by Default
- All pages render as **Static SSR** (server-rendered HTML with no interactivity)
- **No Blazor Server interactivity** - avoid `InteractiveServer` render mode entirely
- **WebAssembly only for the game page** - `Game.razor` uses `InteractiveWebAssembly` because it needs real-time input handling (keyboard events, SignalR)
- Forms use `EditForm` with `[SupplyParameterFromForm]` and `method="post"` - the server handles the POST during the request, no JS required
- Never create API endpoints for operations that can be handled by Static SSR form posts

### Domain Models in Core
- Domain entities (`ApplicationUser`, `CharacterEntity`) live in `Mud.Core.Models`
- Infrastructure contains only implementations (DbContext, services), not models
- This keeps the domain portable and prevents circular dependencies

### EF Core Conventions Over Configuration
- Let EF Core infer relationships from navigation properties
- Avoid fluent API configuration unless necessary (indexes, default values, constraints)
- Use navigation properties (`entity.Account`) rather than raw foreign keys (`entity.AccountId`) when traversing relationships

### Project Dependency Direction
```
Mud.Server → Mud.DependencyInjection → Mud.Infrastructure → Mud.Core
                                                          ↗
Mud.Client ─────────────────────────────────────────────────
```
- Core has no dependencies on other Mud projects
- Infrastructure depends only on Core
- Server never references Infrastructure directly (goes through DependencyInjection)

## 🛠 Essential Commands

### Build & Run
- **Build Solution**: `dotnet build`
- **Run Server**: `dotnet run --project Mud.Server` (This also hosts the Blazor client)
- **Server URL**: `http://localhost:5213` (configured in `launchSettings.json`)

### Testing
- **Run Tests**: `dotnet test` (Note: No unit tests are currently implemented)
- **Visual Testing**: Use Playwright via MCP to verify features work in the browser (see `.agent/TESTING_AGENT.md`)

## 📡 Networking & Serialization

- **Protocol**: SignalR with **MessagePack** binary serialization.
- **Serialization**: All shared models in `Mud.Core` must be decorated with `[MessagePackObject]` and properties with `[Key(n)]`.
- **Game Loop**: The server runs a `BackgroundService` (`GameLoopService`) that ticks every 500ms.
- **Movement Queuing**: Players can queue up to 5 moves. The server processes one move per tick. Queued paths are rendered with transparency.
- **Snapshots**: The server broadcasts a `WorldSnapshot` to all clients every tick.
- **Combat System**:
  - **Entities**: `Player` generalized to `Entity` with `Health`, `MaxHealth`, and `EntityType` (Player, Monster).
  - **Melee**: "Bump" combat triggers when moving into a monster's tile. Damage = 5 + Strength.
  - **Ranged**: `Tab` to cycle targets, `f` to perform a ranged attack. Damage = 5 + Dexterity.
  - **Visuals**: Health bars and target reticles rendered via Phaser commands.
  - **Attack Events**: Server tracks `AttackEvent` records (attacker, target, damage, isMelee, position) per tick, broadcast in `WorldSnapshot.AttackEvents`.
  - **Attack Animations**: Melee attacks trigger bump animation (80% toward target with elastic return). All attacks spawn floating damage numbers that drift downward (upward reserved for heals).
- **Progression System**:
  - **XP**: Killing monsters grants 25 XP to all players in the instance.
  - **Leveling**: XP curve is `100 × level²`. Level cap is 60.
  - **Stat Points**: Each level grants 5 points to allocate among Strength, Dexterity, and Stamina.
  - **Attributes**: Strength (melee damage), Dexterity (ranged damage), Stamina (max HP = 50 + STA × 10).
  - **Character Sheet**: Press `C` to open/close. Shows level, XP bar, attributes with + buttons, derived stats.
  - **Visuals**: "+25 XP" floating text on kills, "Level Up!" on level gain, level number next to health bars.

## 🗄 State Architecture

State flows through layers with clear ownership. When adding new state, check if it already exists in a higher layer before creating new storage.

1. **Database** (`CharacterEntity` in `Mud.Core.Models`) - Source of truth for persistence. Progression saved immediately; volatile state (health, position) saved on disconnect.
2. **CharacterCache** - Read-through cache (30min expiry) for progression lookups. Invalidated on write.
3. **GameLoopService** - Owns ephemeral session state (`_sessions`), world state (`_worlds`), input queues, and per-tick event queues (attacks, XP, level-ups).
4. **WorldState.Entities** - Per-world entity state (position, health, level). Server is authoritative.
5. **Client** - Optimistic UI state (local input queue, target selection, render cache). Server state always wins on conflict.

**Principle:** Don't create new dictionaries/caches for data that can be looked up from an existing owner. Add fields to existing records instead of parallel mappings.

## 🎨 Frontend & Rendering

- **Rendering Engine**: **Phaser 4** with a thin command-based interop layer.
- **Architecture**: C# `GameRenderer` produces render commands, JS `phaser-renderer.js` executes them.
- **Tileset**: Uses `Mud.Server/wwwroot/assets/colored-transparent.png` (16x16 tiles with 1px spacing, rendered at 20x20).
- **Terrain Pooling**: Sprite pools pre-allocated at startup for instant world transitions (overworld: 36,100 sprites, instance: 3,600 sprites).
- **JS Interop**: Blazor communicates with Phaser via `IJSRuntime`.
  - `initPhaser(containerId)`: Initializes Phaser, loads tileset, pre-allocates terrain sprite pools.
  - `executeCommands(commands)`: Processes render commands (CreateSprite, TweenTo, SnapCamera, SetTerrain, etc.).
- **Location**: JavaScript rendering logic is in `Mud.Server/wwwroot/phaser-renderer.js`.

## 🎯 Code Style & Conventions

### Pipeline Pattern
Prefer fluent pipelines that transform data through discrete stages:
```csharp
return new Input(params)
    .StepOne()
    .StepTwo()
    .StepThree()
    .ToResult();
```
- Each stage returns a new type carrying data needed by subsequent stages
- Keep creation logic inside pipeline steps, not passed in from outside
- Avoid intermediate variables when a single chain suffices

### Fluent APIs
Only use fluent/chaining APIs when the output of one call semantically feeds the next (like LINQ or the pipeline pattern above). Don't return `this` just for chaining convenience when operations are independent. If calls are unrelated operations being queued or accumulated, return `void` - it's more honest about what's happening.

### Strongly-Typed Identifiers
Use wrapper types for IDs to prevent mixing up different identifier types:
```csharp
public readonly record struct PlayerId(string Value);
```
- Wrap raw strings at system boundaries (e.g., SignalR `ConnectionId` → `PlayerId`)
- Use `.Value` when interfacing with external systems that need the raw string

### Switch Expressions
Prefer switch expressions over if-chains. Use `when` guards for runtime values:
```csharp
// Compile-time constants - use relational patterns
value switch
{
    < Threshold1 => Result.A,
    < Threshold2 => Result.B,
    _ => Result.C
};

// Runtime values - use when guards
value switch
{
    < Threshold1 => Result.A,
    var v when v < runtimeThreshold => Result.B,
    _ => Result.C
};
```

### Naming Over Overloading
Prefer distinct method names over overloads when behavior differs:
```csharp
// Good: Clear intent
ToBiomes()              // Uses default thresholds
ToBiomesWithDensity()   // Custom density for instances

// Avoid: Ambiguous overloads
ToBiomes()
ToBiomes(float, float)
```

### CSS Isolation
Use Blazor CSS isolation (`Component.razor.css`) instead of inline styles or global CSS.

### Namespaces
Follow the project structure (e.g., `Mud.Server.Services`, `Mud.Core.Models`).

### Dependency Injection
- `GameLoopService` is registered as a Singleton and a HostedService in the server.
- `GameClient` is registered as a Scoped service in the client.

## 📥 Receiving Feedback

When receiving code suggestions (from code-quality-reviewer, user comments, or PR feedback):

1. **Verify first** - Check if the feedback applies to the actual code as written
2. **Check against architecture** - Does it conflict with documented decisions in this file?
3. **Push back when warranted**:
   - Breaks existing functionality
   - Adds speculative complexity (YAGNI violation)
   - Conflicts with State Architecture or other documented patterns
   - Reviewer lacks full context
4. **Respond factually** - "Fixed: [change]" or "Won't fix: [reason]"
5. **No performative agreement** - Skip "Great point!" or "You're right!" Just fix it or explain why not.

If you pushed back and were wrong, state the correction factually and move on. No long apologies.

## ⚠️ Gotchas & Patterns

- **Coordinate System**: The game uses a tile-based coordinate system. Camera is centered on the player by offsetting to (400, 300) on the 800x600 canvas.
- **Input Handling**: Keyboard events are captured in `Game.razor` and sent to the server via `GameClient.MoveAsync`.
- **Collision**: Basic wall collision is handled server-side in `GameLoopService.Update()`.
- **Prerendering**: Interactive WebAssembly components are configured with `prerender: false` in `App.razor` to avoid issues with JS Interop during initial load.
  - **Symptom**: "JavaScript interop calls cannot be issued at this time" or null reference on `IJSRuntime` during render.
- **MessagePack Serialization**: All shared models need `[MessagePackObject]` and `[Key(n)]` attributes.
  - **Symptom**: "Failed to serialize" errors or silent SignalR message failures.

## 📋 Task Planning & Implementation

When the user says they want to plan a new task, or when planning or starting a new task, follow the structured process defined in `.agent/TASK_PLANNING.md`. Read that document and follow it.

When the user says to implement a task, look for it in `.agent/tasks/`.

## 🏁 Task Closing Flow

When a task is completed and the user says "finalize", "close the task", or "I'm happy with this task", or something similar about ending the task:
1. **Cleanup**: Delete the corresponding `.md` file from `.agent/tasks/`.
2. **Documentation Review**: Review what was implemented, then read through `AGENTS.md` and verify the documented architecture still matches the code. Look for:
   - Descriptions that are now outdated or incorrect
   - New systems/patterns that should be documented
   - State ownership changes (check the State Architecture section)
   - Removed or renamed services/classes still mentioned
   Update any stale documentation and add new sections if needed.
3. **Verification**: Ensure the project still builds and the todo list is cleared.
4. **Completion**: Use the `todos` tool to clear the list and provide a concise summary of the final state.
5. **Git Commit Draft**: Provide a concise (1-2 sentence) summary of the task's purpose and impact, suitable for a git commit message. Focus on "why" and "how" the goal was achieved, rather than listing specific code changes.
