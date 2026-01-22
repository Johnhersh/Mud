# Agent Guide: Mud

This repository contains "Mud", a web-based MMO prototype featuring a retro-futuristic ASCII visual style. It uses a Blazor WebAssembly frontend, an ASP.NET Core backend, and SignalR for real-time communication.

## 🏗 Project Structure

The solution is divided into three main projects:

- **Mud.Server**: ASP.NET Core Web App. Handles game simulation, authoritative state, and broadcasts world snapshots.
- **Mud.Client**: Blazor WebAssembly app. Manages UI, input handling, and rendering via PixiJS.
- **Mud.Shared**: C# Class Library containing shared models and enums used by both Client and Server.

## 🛠 Essential Commands

### Build & Run
- **Build Solution**: `dotnet build`
- **Run Server**: `dotnet run --project Mud.Server` (This also hosts the Blazor client)

### Testing
- **Run Tests**: `dotnet test` (Note: No tests are currently implemented in the prototype)

## 📡 Networking & Serialization

- **Protocol**: SignalR with **MessagePack** binary serialization.
- **Serialization**: All shared models in `Mud.Shared` must be decorated with `[MessagePackObject]` and properties with `[Key(n)]`.
- **Game Loop**: The server runs a `BackgroundService` (`GameLoopService`) that ticks every 500ms.
- **Movement Queuing**: Players can queue up to 5 moves. The server processes one move per tick. Queued paths are rendered with transparency.
- **Snapshots**: The server broadcasts a `WorldSnapshot` to all clients every tick.

## 🎨 Frontend & Rendering

- **Rendering Engine**: **PixiJS** is used for high-performance WebGL rendering.
- **JS Interop**: Blazor communicates with PixiJS via `IJSRuntime`. 
  - `initPixi(containerId)`: Initializes the Pixi application.
  - `renderSnapshot(snapshot)`: Updates the visual state based on the latest server snapshot.
- **Location**: JavaScript rendering logic is located in `Mud.Server/wwwroot/game.js`.

## 📝 Coding Conventions

- **Namespace**: Follows the project structure (e.g., `Mud.Server.Services`, `Mud.Shared`).
- **Models**: Use `record` types for immutable data structures in `Mud.Shared`.
- **Dependency Injection**:
  - `GameLoopService` is registered as a Singleton and a HostedService in the server.
  - `GameClient` is registered as a Scoped service in the client.

## ⚠️ Gotchas & Patterns

- **Coordinate System**: The game uses a tile-based coordinate system. Rendering in `game.js` offsets coordinates by `+400` (X) and `+300` (Y) to center the (0,0) position on an 800x600 canvas.
- **Input Handling**: Keyboard events are captured in `Home.razor` and sent to the server via `GameClient.MoveAsync`.
- **Collision**: Basic wall collision is handled server-side in `GameLoopService.Update()`.
- **Prerendering**: Interactive WebAssembly components are configured with `prerender: false` in `App.razor` to avoid issues with JS Interop during initial load.

## 📋 Task Planning & Implementation

When the user says they want to plan a new task, or when planning or starting a new task, follow the structured process defined in `.agent/TASK_PLANNING.md`:

1.  **Phase 1: Product Definition**: Understand goals and scope without code exploration. Ask clarifying questions one at a time.
2.  **Phase 2: Technical Specification**: After user approval, explore the codebase and draft a detailed task specification.
3.  **Task Documents**: Finalize plans as `.md` files in the `.agent/tasks/` folder.

When the user says to implement a task, look for it in this tasks folder.

## 🏁 Task Closing Flow

When a task is completed and the user asks to "close the task":
1. **Cleanup**: Delete the corresponding `.md` file from `.agent/tasks/`.
2. **Documentation**: Update `AGENTS.md` (or other relevant memory files) to reflect the new state of the project, including any new features, architectural changes, or roadmap progress.
3. **Verification**: Ensure the project still builds and the todo list is cleared.

## 🗺 Roadmap (V0 Prototype)
1. **Phase 1**: Project Scaffolding (Done)
2. **Phase 2**: Backend Core / Heartbeat (Done)
3. **Phase 3**: Frontend Core / Input (Done)
4. **Phase 4**: Rendering / PixiJS (Done)
5. **Phase 5**: Movement Queuing (Done)
6. **Phase 6**: Testing & Validation (Pending)
