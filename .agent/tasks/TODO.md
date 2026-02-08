# Follow-up Tasks

## Adopt PlayerId for player-scoped dictionaries and method signatures

GameState, GameLoopService, and WorldUpdateExtensions use raw `string` connection IDs as dictionary keys and method parameters. `PlayerId` already exists as a strongly-typed wrapper but is unused. Wrap connection IDs at the SignalR boundary (GameHub) and thread `PlayerId` through GameState dictionaries (Sessions, PlayerInputQueues, XpEvents, ProgressionUpdates), PlayerSession.ConnectionId, GameLoopService methods, and WorldUpdateExtensions methods. Entity.Id stays `string` since it serves both players and monsters.

## Replace try/catch with Result pattern using FluentResults

Infrastructure methods (`IPersistenceService`, `ICharacterCache`) should catch exceptions internally and return `Result` objects (FluentResults library). Callers in GameLoopService (`SaveAllPlayersAsync`, `ProcessPendingPersistenceOps`, `RemovePlayerAsync`) currently wrap calls in try/catch — these should handle `Result` success/failure instead. Try/catch should only exist at the Infrastructure boundary, not in game logic or service code.

## Drop Async suffix from method names

Methods should not use the `_Async` suffix. Rename `RemovePlayerAsync`, `SaveAllPlayersAsync`, `ProcessPendingPersistenceOps` (already correct), `UpdateVolatileStateAsync`, `UpdateProgressionAsync`, `FlushAsync`, `GetProgressionAsync`, etc. across the codebase.
