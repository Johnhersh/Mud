# Follow-up Tasks

## Adopt PlayerId for player-scoped dictionaries and method signatures

GameState, GameLoopService, and WorldUpdateExtensions use raw `string` connection IDs as dictionary keys and method parameters. `PlayerId` already exists as a strongly-typed wrapper but is unused. Wrap connection IDs at the SignalR boundary (GameHub) and thread `PlayerId` through GameState dictionaries (Sessions, PlayerInputQueues, XpEvents, ProgressionUpdates), PlayerSession.ConnectionId, GameLoopService methods, and WorldUpdateExtensions methods. Entity.Id stays `string` since it serves both players and monsters.
