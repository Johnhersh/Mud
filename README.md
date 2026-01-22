# Mud

A web-based MMO prototype featuring a retro-futuristic ASCII visual style.

## Tech Stack

- **Frontend**: Blazor WebAssembly
- **Backend**: ASP.NET Core
- **Real-time**: SignalR with MessagePack binary serialization
- **Rendering**: PixiJS (WebGL)

## Project Structure

- **Mud.Server**: ASP.NET Core Web App. Handles game simulation and broadcasts world snapshots.
- **Mud.Client**: Blazor WebAssembly app. Manages UI, input, and rendering.
- **Mud.Shared**: C# Class Library containing shared models and enums.

## Getting Started

### Prerequisites

- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

### Running the Project

1. Clone the repository.
2. Navigate to the root directory.
3. Run the following command:

   ```bash
   dotnet run --project Mud.Server
   ```

4. Open your browser and navigate to `http://localhost:5000` (or the port specified in the console output).

## Controls

- **Arrow Keys / WASD**: Move your character.
