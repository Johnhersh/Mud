# Fix Keyboard Input Focus Issue

## Objective

Make keyboard controls work immediately when entering the game, regardless of which element has focus.

## Problem Statement

Currently, keyboard controls (arrow keys, Enter, Tab, etc.) only work after clicking on the "interaction-prompt" HTML element. The `game-container` div uses Blazor's `@onkeydown` binding which only fires when that element has focus. Nothing auto-focuses it on load.

## Success Criteria

- Keyboard controls work immediately upon navigating to `/Game`
- No clicking required to activate controls
- Controls work regardless of which element on the page has focus
- Existing functionality (movement, combat, character sheet) continues to work

## Technical Approach

### Solution: Document-Level Keyboard Handler with C# Configuration

Create a lightweight `KeyboardHandler` class inspired by Toolbelt.Blazor.HotKeys2's API, where:
- JS captures keypresses at document level
- C# configures which keys to capture via fluent `.Add()` API
- Callbacks are visible at registration site

### API Design

```csharp
// In Game.razor
private KeyboardHandler? _keyHandler;

protected override async Task OnAfterRenderAsync(bool firstRender)
{
    if (firstRender)
    {
        _keyHandler = await KeyboardHandler.CreateAsync(JS, keys => keys
            .Add(Code.ArrowUp,    () => QueueMove(Direction.Up))
            .Add(Code.ArrowDown,  () => QueueMove(Direction.Down))
            .Add(Code.ArrowLeft,  () => QueueMove(Direction.Left))
            .Add(Code.ArrowRight, () => QueueMove(Direction.Right))
            .Add(Code.KeyW,       () => QueueMove(Direction.Up))
            .Add(Code.KeyS,       () => QueueMove(Direction.Down))
            .Add(Code.KeyA,       () => QueueMove(Direction.Left))
            .Add(Code.KeyD,       () => QueueMove(Direction.Right))
            .Add(Code.Tab,        CycleTarget)
            .Add(Code.KeyF,       PerformRangedAttack)
            .Add(Code.KeyC,       ToggleCharacterSheet)
            .Add(Code.Enter,      HandleInteraction)
            .Add(Code.Escape,     CloseCharacterSheet));

        // ... rest of init
    }
}

public async ValueTask DisposeAsync()
{
    if (_keyHandler != null)
        await _keyHandler.DisposeAsync();
}
```

### Components to Create

1. **`Code` enum** (`Mud.Client/Input/Code.cs`)
   - Type-safe key codes for the keys we use
   - Values match JavaScript `KeyboardEvent.code` strings

2. **`KeyboardHandler` class** (`Mud.Client/Input/KeyboardHandler.cs`)
   - `CreateAsync(IJSRuntime js, Action<KeyboardHandlerBuilder> configure)` - factory method
   - Internal `KeyboardHandlerBuilder` with fluent `.Add(Code, Action)`
   - `DisposeAsync()` - unregisters from JS
   - `[JSInvokable] OnKeyDown(string code)` - callback from JS

3. **JS functions** (add to `phaser-renderer.js`)
   - `registerKeyboardHandler(dotNetRef, keyCodes)` - sets up document listener
   - `unregisterKeyboardHandler()` - removes listener

## Edge Cases

1. **Character sheet open** - Some keys (movement, Tab, F, Enter) should be blocked when `_characterSheetOpen` is true. The C# callbacks handle this check.

2. **Game not in Playing state** - All input should be ignored when `_status != GameStatus.Playing`. Add early return in callbacks or a global guard.

3. **Dispose during active game** - Must unregister JS listener to prevent callbacks to disposed component.

4. **Multiple rapid keypresses** - JS should debounce or the handler should be resilient to rapid invocations.

## Technical Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Document vs window listener | `document` | Standard practice, works in all scenarios |
| Key identification | `event.code` | Physical key location, layout-independent |
| Async vs sync callbacks | Support both via `Func<Task>` | Some handlers need async (GameClient calls) |
| preventDefault | Always for registered keys | Prevents browser shortcuts (Tab focus, etc.) |

## Files to Modify

### New Files
- `Mud.Client/Input/Code.cs` - Key code enum
- `Mud.Client/Input/KeyboardHandler.cs` - Handler class with builder

### Modified Files
- `Mud.Server/wwwroot/phaser-renderer.js` - Add JS keyboard functions
- `Mud.Client/Pages/Game.razor` - Replace `@onkeydown` with `KeyboardHandler`
  - Change `@implements IDisposable` → `@implements IAsyncDisposable`
  - Remove `@onkeydown` and `tabindex` from game-container div
  - Remove `HandleKeyDown` method
  - Add keyboard handler registration in `OnAfterRenderAsync`
  - Refactor key actions into separate methods for cleaner registration

## Code Patterns to Follow

From existing codebase:
- Use `IJSRuntime` for JS interop (already injected in Game.razor)
- Follow namespace convention: `Mud.Client.Input`
- Async disposal pattern with null checks
- Pipeline-style registration (fluent API)

## How to Test

### Step 1: Code Quality Review

```
Task(
  subagent_type: "code-quality-reviewer",
  description: "Review keyboard handler code",
  prompt: """
  Review the code changes for the keyboard focus fix.

  **Files changed:**
  - Mud.Client/Input/Code.cs
  - Mud.Client/Input/KeyboardHandler.cs
  - Mud.Server/wwwroot/phaser-renderer.js
  - Mud.Client/Pages/Game.razor

  **What to look for:**
  - Any #pragma directives (require user approval)
  - Proper event listener cleanup on component disposal
  - No memory leaks from event handlers or DotNetObjectReference
  - Adherence to project coding patterns
  """
)
```

### Step 2: Visual/Functional Testing

```
Task(
  subagent_type: "playwright-tester",
  description: "Test keyboard input on game load",
  prompt: """
  **Feature:** Keyboard controls work immediately on game load

  **Test Steps:**
  1. Navigate to /Game → Game should load and show "Playing" status
  2. WITHOUT clicking anything, press arrow keys → Player should move
  3. Press Tab → Should target a monster (if in instance with monsters)
  4. Press C → Character sheet should open
  5. Press C again → Character sheet should close
  6. Press arrow keys while character sheet is open → Player should NOT move

  **Visual Verification:**
  - Player sprite moves in response to arrow keys without any clicks
  - No JavaScript errors in console
  """
)
```
