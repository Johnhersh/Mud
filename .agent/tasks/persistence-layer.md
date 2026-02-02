# Task: Database Persistence Layer

## Objective
Implement a persistence layer to save game data to PostgreSQL, enabling players to resume their progress across sessions.

## Problem Statement
Currently all game state is ephemeral - when the server restarts or a player disconnects, all progress is lost. An MMO requires persistent storage for player progression, position, and eventually world state.

## Key Insight: Memory vs Database

The server already holds authoritative state in memory for real-time broadcasting. The database is for **durability** - recovering state across disconnects and server restarts, not for real-time sharing.

---

## Data to Persist

### Player State
| Data | In Memory | Persist to DB | Rationale |
|------|-----------|---------------|-----------|
| Position | Always | On disconnect / graceful shutdown | Only need durability, not real-time |
| Health | Always | On disconnect / graceful shutdown | Same as position |
| XP | Always | Immediately (same tick as gain) | High-value, losing XP feels bad |
| Level | Always | Immediately (same tick as change) | High-value progression |
| Stats (Str/Dex/Sta) | Always | Immediately (same tick as change) | Tied to leveling |
| UnspentPoints | Always | Immediately (same tick as change) | Part of progression |

### Future Data (design for extensibility)
| Data | Persist When | Notes |
|------|--------------|-------|
| Inventory | On change | Items are valuable; persist immediately |
| World state (POIs) | On POI spawn/modification | For dynamically generated content |

---

## Save Strategy

**Event-driven persistence tied to game loop:**

1. **Same tick as change** - XP, Level, Stats, UnspentPoints (piggyback on game loop tick)
2. **On player disconnect** - Position, Health, CurrentWorldId, LastOverworldPosition
3. **On graceful server shutdown** - All connected players' volatile state

No periodic saves needed. Server crashes only lose position/health (minor), since progression data is persisted immediately.

---

## Technical Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Database | PostgreSQL | Already running, other projects use it |
| ORM | EF Core | .NET ecosystem standard, migrations, Identity support |
| Identity | ASP.NET Core Identity | Battle-tested auth with hashing built-in |
| Auth flow | Blazor routing + cookies | Standard pattern, SignalR not responsible for auth |
| Schema | Account + Character tables | Supports future multi-character, implement single for now |
| Concurrent logins | Kick existing session | Hard limit of one active session per account |
| Crash recovery | Accept minor loss | XP/Level persisted immediately; only position/health at risk |
| Server shutdown | Instances lost | Players resume at overworld return point |

---

## Authentication Architecture

**Blazor handles authentication, not SignalR.**

### Route Structure
| Route | Purpose | Auth |
|-------|---------|------|
| `/` | Landing page with login/register forms | Public |
| `/Game` | Game client (current Home.razor content) | `[Authorize]` |

### Auth Flow
1. User visits `/` → sees login/register UI
2. User registers or logs in → Blazor Identity sets auth cookie
3. Redirect to `/Game` → protected route loads game
4. SignalR connects → `Context.User` has authenticated identity from cookie
5. `GameHub` reads `AccountId` from `Context.User.Claims`

### If Already Authenticated
- Visit `/` → redirect to `/Game`

### If Not Authenticated
- Visit `/Game` → redirect to `/`

### Identity Model
- **AccountId** - The user's login account (from Identity), used for authentication
- **CharacterId** - The character being played, used for all game logic
- **ConnectionId** - SignalR's ephemeral session ID (transport layer only)

```
Account (AccountId) ← what you log in with
  └── Character (CharacterId) ← who you play as (1:1 for now, multi-char later)
```

### How It Works
1. Player authenticates → `AccountId` available in `Context.User.Claims`
2. `GameHub.JoinGame()` loads `CharacterId` for this account from DB
3. `SessionManager` maps `ConnectionId` ↔ `CharacterId` for SignalR message routing
4. All game logic uses `CharacterId` (replaces current `PlayerId`)

### Migration Notes
**Project rename:** `Mud.Shared` → `Mud.Core` (more appropriate name for central interfaces/models)

**Identity change:** Current codebase uses `PlayerId` wrapping `ConnectionId`. This will be replaced:
- Rename `PlayerId` → `CharacterId` (or keep `PlayerId` as alias if preferred)
- Change from wrapping `ConnectionId` to wrapping persistent `Guid`

---

## Project Architecture

### New Project: Mud.Infrastructure
All persistence and Identity code lives here, isolated from other projects.

**Dependency graph:**
```
Mud.Server ──► Mud.DependencyInjection ──► Mud.Infrastructure
     │                                            │
     │                                            ▼
     └──────────────► Mud.Core ◄────────────────┘
                          ▲
                          │
                      Mud.Client
```

**Strict isolation via `DisableTransitiveProjectReferences`:**
- `Mud.Server` → `Mud.DependencyInjection` + `Mud.Core`
- `Mud.Server` **cannot** access `Mud.Infrastructure` (transitive blocked)
- `Mud.DependencyInjection` → `Mud.Infrastructure` + `Mud.Core`
- `Mud.Infrastructure` → `Mud.Core`
- `Mud.Client` → `Mud.Core`

```xml
<!-- Mud.Server/Mud.Server.csproj -->
<PropertyGroup>
  <DisableTransitiveProjectReferences>true</DisableTransitiveProjectReferences>
</PropertyGroup>
```

### DI Registration Pattern
```csharp
// Mud.DependencyInjection/ServiceCollectionExtensions.cs
public static class ServiceCollectionExtensions
{
    public static void AddMudServices(this IServiceCollection services,
        IConfiguration configuration, bool isDevelopment)
    {
        // Calls into Infrastructure to register DbContext, Identity, services
        InfrastructureRegistration.AddInfrastructureServices(services, configuration, isDevelopment);
    }
}

// Mud.Infrastructure/InfrastructureRegistration.cs
public static class InfrastructureRegistration
{
    public static void AddInfrastructureServices(IServiceCollection services,
        IConfiguration configuration, bool isDevelopment)
    {
        // Register DbContext, Identity, PersistenceService, SessionManager, etc.
    }
}

// Mud.Server/Program.cs
builder.Services.AddMudServices(builder.Configuration, builder.Environment.IsDevelopment());
```

---

## Database Schema

**All tables created via EF Core Migrations** - no manual SQL.

### Identity Setup
```csharp
// Mud.Infrastructure/Data/MudDbContext.cs
public class MudDbContext : IdentityDbContext<ApplicationUser>
{
    public DbSet<CharacterEntity> Characters => Set<CharacterEntity>();
    // ...
}
```

Uses ASP.NET Core Identity with static SSR pattern.

### Character Entity
```csharp
// Mud.Infrastructure/Data/Entities/CharacterEntity.cs
public class CharacterEntity
{
    public Guid Id { get; set; }
    public string AccountId { get; set; }  // FK to ApplicationUser.Id
    public string Name { get; set; }

    // Progression (persisted immediately)
    public int Level { get; set; } = 1;
    public int Experience { get; set; } = 0;
    public int Strength { get; set; } = 5;
    public int Dexterity { get; set; } = 5;
    public int Stamina { get; set; } = 5;
    public int UnspentPoints { get; set; } = 0;

    // Volatile state (persisted on disconnect)
    public int Health { get; set; } = 100;
    public int MaxHealth { get; set; } = 100;
    public int PositionX { get; set; } = 0;
    public int PositionY { get; set; } = 0;
    public string? CurrentWorldId { get; set; }  // null = overworld
    public int LastOverworldX { get; set; } = 0;
    public int LastOverworldY { get; set; } = 0;

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Navigation
    public ApplicationUser Account { get; set; }
}
```

### Migrations
```bash
# From solution root
dotnet ef migrations add InitialCreate --project Mud.Infrastructure --startup-project Mud.Server
dotnet ef database update --project Mud.Infrastructure --startup-project Mud.Server
```

---

## Login/Resume Flow

### New Player Registration
1. Create account (ASP.NET Identity)
2. Auto-create character with default stats (name = username for now)
3. Redirect to `/Game`, spawn at overworld origin (0, 0)

### Returning Player Login
1. Authenticate via Identity
2. Load character data from DB
3. Check if `CurrentWorldId` references a live instance:
   - **Instance exists** → Resume at saved position
   - **Instance gone** → Spawn at `LastOverworldPosition`
4. Kick any existing session for this account

### Player Disconnect
1. Save volatile state (Position, Health, CurrentWorldId, LastOverworldPosition)
2. Remove from in-memory state

### Graceful Server Shutdown
1. Iterate all connected players
2. Save volatile state for each
3. Clear instances (they're ephemeral)

---

## Code Patterns to Follow

Based on codebase exploration:

### Strongly-Typed IDs
```csharp
public readonly record struct CharacterId(Guid Value);  // Game identity
public readonly record struct AccountId(string Value);  // Auth identity
// PlayerId removed - CharacterId serves this purpose
```

### Immutable Records for DB Entities
```csharp
public record CharacterEntity
{
    public Guid Id { get; init; }
    public string AccountId { get; init; }
    // ... etc
}
```

### Pipeline Pattern for Loading
```csharp
return await GetCharacterAsync(accountId)
    .ToPlayerState()
    .SpawnInWorld(worldManager);
```

### Singleton Services
GameLoopService is Singleton - persistence service should be Scoped (for DbContext) but accessed via IServiceScopeFactory from the Singleton.

---

## Integration Points

### Project Rename

**Mud.Shared → Mud.Core**
- Rename project folder and csproj
- Update all project references
- Update namespaces throughout codebase

### New Projects to Create

**Mud.Infrastructure** - Class library for persistence implementations
```bash
dotnet new classlib -n Mud.Infrastructure
dotnet sln add Mud.Infrastructure
cd Mud.Infrastructure
dotnet add reference ../Mud.Core
dotnet add package Microsoft.AspNetCore.Identity.EntityFrameworkCore
dotnet add package Npgsql.EntityFrameworkCore.PostgreSQL
dotnet add package Microsoft.EntityFrameworkCore.Tools
cd ..
```

**Mud.DependencyInjection** - Tiny project for DI registration (firewall)
```bash
dotnet new classlib -n Mud.DependencyInjection
dotnet sln add Mud.DependencyInjection
cd Mud.DependencyInjection
dotnet add reference ../Mud.Infrastructure
dotnet add reference ../Mud.Core
cd ..
```

**Update Mud.Server** - Add DI reference, enable strict isolation
```bash
cd Mud.Server
dotnet add reference ../Mud.DependencyInjection
cd ..
```
Then add to `Mud.Server.csproj`:
```xml
<PropertyGroup>
  <DisableTransitiveProjectReferences>true</DisableTransitiveProjectReferences>
</PropertyGroup>
```

### Files to Modify

| File | Changes |
|------|---------|
| `Mud.Server/Mud.Server.csproj` | Add `DisableTransitiveProjectReferences`, reference DependencyInjection |
| `Mud.Server/Program.cs` | Call `AddMudServices()`, add auth middleware |
| `Mud.Server/appsettings.json` | Add connection string |
| `Mud.Server/Services/GameLoopService.cs` | Hook persistence calls in `AwardXpToInstance()`, `AllocateStat()`, `RemovePlayer()` |
| `Mud.Server/Hubs/GameHub.cs` | Read AccountId from Context.User, load CharacterId, session management |
| `Mud.Client/Pages/Home.razor` | Rename to `Game.razor`, add `[Authorize]` attribute |

### New Files to Create

**Mud.Core (interfaces):**
| File | Purpose |
|------|---------|
| `Mud.Core/Services/IPersistenceService.cs` | Persistence interface |
| `Mud.Core/Services/ISessionManager.cs` | Session tracking interface |

**Mud.DependencyInjection:**
| File | Purpose |
|------|---------|
| `Mud.DependencyInjection/ServiceCollectionExtensions.cs` | `AddMudServices()` extension - calls into Infrastructure |

**Mud.Infrastructure (implementations):**
| File | Purpose |
|------|---------|
| `Mud.Infrastructure/InfrastructureRegistration.cs` | Internal registration called by DependencyInjection project |
| `Mud.Infrastructure/Data/MudDbContext.cs` | `IdentityDbContext<ApplicationUser>` |
| `Mud.Infrastructure/Data/ApplicationUser.cs` | Identity user (can extend later) |
| `Mud.Infrastructure/Data/Entities/CharacterEntity.cs` | Character DB entity |
| `Mud.Infrastructure/Services/PersistenceService.cs` | EF Core implementation |
| `Mud.Infrastructure/Services/SessionManager.cs` | Session tracking implementation |

**Mud.Client (pages):**
| File | Purpose |
|------|---------|
| `Mud.Client/Pages/Home.razor` | New landing page with login/register forms |
| `Mud.Client/Pages/Game.razor` | Current Home.razor content, protected with `[Authorize]` |

### NuGet Packages

**Mud.Infrastructure:**
- `Microsoft.AspNetCore.Identity.EntityFrameworkCore`
- `Npgsql.EntityFrameworkCore.PostgreSQL`
- `Microsoft.EntityFrameworkCore.Tools`

**Mud.Client:**
- `Microsoft.AspNetCore.Components.Authorization`

---

## Edge Cases

| Scenario | Handling |
|----------|----------|
| Instance destroyed while player offline | Spawn at `LastOverworldPosition` |
| Concurrent login attempt | Kick existing session, allow new one |
| Player dies (future) | Deferred - death system not implemented |
| Server crash | Accept loss of position/health; XP/Level safe |
| DB write fails during tick | Log error, retry on next relevant event; don't crash game loop |

---

## Architectural Refactor: Single Source of Truth

### Problem Identified
Current design has duplicate state:
- `Entity` holds progression fields (XP, Level, Stats) in memory
- `CharacterEntity` holds same fields in DB
- Must keep them in sync → risk of drift, complex code

### Solution: DB as Single Source of Truth with Cache

**Mental model:** DB is truth, in-memory is just a cache for performance.

**Use .NET's `IMemoryCache`:**
```csharp
// Read (cache miss loads from DB)
var character = await _cache.GetOrCreateAsync($"character:{id}",
    async entry => await _db.LoadCharacterAsync(id));

// Write (update DB, invalidate cache)
await _db.AllocateStatAsync(id, stat);
_cache.Remove($"character:{id}");
```

No manual sync. Write to DB, invalidate cache. Next read reloads from DB.

### Rename: PlayerState → PlayerSession

`PlayerState` is misleading - it's not persistent state, it's session data.

**PlayerSession** (transient, lost on disconnect):
- `ConnectionId` (string, from SignalR)
- `CharacterId` (link to persistent character)
- `CurrentWorldId`
- `Position`
- `LastOverworldPosition`

**CharacterData** (persistent, in DB, accessed via cache):
- Name, Level, Experience
- Strength, Dexterity, Stamina
- UnspentPoints, Health, MaxHealth

### Trim Entity (COMPLETED)

`Entity` now only holds volatile gameplay state needed every tick:
- Id, Name, Position, QueuedPath, Type, Health, MaxHealth, Level

Removed from Entity (now in DB/cache only):
- Experience, Strength, Dexterity, Stamina, UnspentPoints

Level is kept on Entity for health bar display. Monster combat stats use `MonsterStats` helper class.

### Flow

1. **Connect** → Create `PlayerSession`, load `CharacterData` from DB (cached)
2. **Gameplay** → Read stats from cache for damage calc, update position in session
3. **Stat allocation** → Write to DB, invalidate cache
4. **Disconnect** → Discard session, character persists in DB

### Tasks

- [x] Add `IMemoryCache` registration
- [x] Create `CharacterCache` service (`ICharacterCache` interface + implementation)
- [x] Rename `PlayerState` → `PlayerSession`
- [x] Remove `PlayerId` type, use `string ConnectionId`
- [x] Remove progression fields from `Entity` (Experience, Strength, Dexterity, Stamina, UnspentPoints removed; Level kept for display)
- [x] Update damage calculation to read from cache (infrastructure ready, still reads Entity for now)
- [x] Update stat allocation to write DB + invalidate cache
- [x] Update client to receive progression updates via separate event (`OnProgressionUpdate`)

---

## Deferred / Future Work

- [ ] Death system integration (where to spawn on death?)
- [ ] Character selection screen (when multi-character needed)
- [ ] Inventory persistence
- [ ] World state / POI persistence
- [ ] More robust server shutdown with player notification

---

## Connection String

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5432;Database=Mud;Username=john;Password=1234"
  }
}
```

Database `Mud` already exists in the Postgres instance.

---

## How to Test

### Browser Testing
After implementation is complete, spawn the `playwright-tester` agent:

```
Task(
  subagent_type: "playwright-tester",
  description: "Test persistence layer",
  prompt: """
  **Feature:** Player persistence across sessions

  **Test Steps:**
  1. Navigate to http://localhost:5148/ → Should see landing page with login/register forms
  2. Try to navigate to /Game directly → Should redirect back to /
  3. Register a new account (testuser / testpass) → Should redirect to /Game and enter game
  4. Kill a monster to gain XP → XP should increase
  5. Note the XP value, then refresh the page → Should still be on /Game (auth persists)
  6. XP should be the same as before refresh
  7. Move to a different position, then close browser tab
  8. Reopen and navigate to / → Should redirect to /Game (already authenticated)
  9. Position should be restored

  **Visual Verification:**
  - Landing page (/) shows login/register UI, not the game
  - /Game shows the game with player character
  - XP persists across page refreshes
  - Position persists across sessions
  - No JavaScript errors in console
  """
)
```

### Database Verification
After in-game actions, verify data is persisted correctly by inspecting the database:

```bash
# Connect to Postgres
psql -h localhost -U john -d Mud

# Check character was created on registration
SELECT * FROM "Characters" WHERE "Name" = 'testuser';

# After killing monsters, verify XP is updated (should happen immediately)
SELECT "Name", "Experience", "Level" FROM "Characters";

# After disconnect, verify position was saved
SELECT "Name", "PositionX", "PositionY", "CurrentWorldId" FROM "Characters";

# Verify stat allocation persists
SELECT "Name", "Strength", "Dexterity", "Stamina", "UnspentPoints" FROM "Characters";
```

**Expected behavior:**
- Character row created immediately on registration
- XP/Level columns update on the same tick as XP gain (not on disconnect)
- Position columns update only after disconnect
- Stat columns update immediately when points are allocated
