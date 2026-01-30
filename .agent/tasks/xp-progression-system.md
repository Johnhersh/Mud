# Task: XP & Progression System

## Objective
Add player progression via XP from kills, leveling, and allocatable attribute points.

## Problem Statement
Players have no sense of progression. Combat exists but has no lasting impact on character growth. There are no attributes affecting damage or health, and no way to customize a character build.

## Success Criteria
- [ ] Killing monsters grants 25 XP to all players in the instance
- [ ] XP accumulates toward levels using escalating curve (`100 × level²`)
- [ ] Level cap is 60; XP stops accumulating at cap
- [ ] Each level grants 5 stat points to allocate
- [ ] Three attributes exist: Strength (melee dmg), Dexterity (ranged dmg), Stamina (max HP)
- [ ] Damage formulas use attributes: melee = 5 + STR, ranged = 5 + DEX
- [ ] Max health formula: 50 + (STA × 10)
- [ ] Adding Stamina increases current health by same amount
- [ ] Leveling up heals player to 100%
- [ ] Level number displayed next to health bar (in Phaser)
- [ ] "+25 XP" floating text appears when monster dies
- [ ] "Level Up!" white floating text appears on level gain
- [ ] Character sheet (C key) shows: name, level, XP bar, attributes with + buttons, derived stats
- [ ] Character sheet closes with C toggle, Escape, or click outside

## Technical Decisions

### SignalR Broadcasting Architecture
**Decision:** Refactor from per-player sends to SignalR groups for efficiency.

- **World snapshot → Group broadcast**: All shared data (entities, tiles, attacks, level-ups) sent via `Clients.Group(worldId)`
- **XP events → Per-player**: Sent individually via `Clients.Client(playerId)` since amounts may vary (future buffs)
- **Level-up events → Grouped**: Everyone sees when someone levels up (social feature)

**Rationale:** Currently everything in the snapshot is identical for all players in a world, but we're doing N individual sends. Groups are more efficient. XP needs to be per-player to support future buff modifiers.

## Technical Approach

### 1. Extend Entity Model (`Mud.Shared/Models.cs`)

Add progression properties to the Entity record:

```csharp
[MessagePackObject]
public record Entity
{
    // Existing keys 0-6...

    // Progression
    [Key(7)] public int Level { get; init; } = 1;
    [Key(8)] public int Experience { get; init; } = 0;

    // Attributes (base values act as floor - can't go below)
    [Key(9)] public int Strength { get; init; } = 5;
    [Key(10)] public int Dexterity { get; init; } = 5;
    [Key(11)] public int Stamina { get; init; } = 5;
    [Key(12)] public int UnspentPoints { get; init; } = 0;
}
```

Add helper methods or a separate static class for formulas:

```csharp
public static class ProgressionFormulas
{
    public const int XpPerKill = 25;
    public const int PointsPerLevel = 5;
    public const int MaxLevel = 60;

    public const int BaseMeleeDamage = 5;
    public const int BaseRangedDamage = 5;
    public const int BaseHealth = 50;
    public const int HealthPerStamina = 10;

    public static int ExperienceForLevel(int level) => 100 * level * level;
    public static int MeleeDamage(int strength) => BaseMeleeDamage + strength;
    public static int RangedDamage(int dexterity) => BaseRangedDamage + dexterity;
    public static int MaxHealth(int stamina) => BaseHealth + (stamina * HealthPerStamina);
}
```

### 2. Add XP/Level Events (`Mud.Shared/Models.cs`)

Add event types for client-side visuals:

```csharp
// Sent per-player (separate from snapshot) - no PlayerId needed since recipient knows it's theirs
[MessagePackObject]
public record XpGainEvent(
    [property: Key(0)] int Amount,
    [property: Key(1)] Point Position  // Where to show floating text (e.g., monster death location)
);

// Sent in grouped snapshot - everyone sees level-ups (social feature)
[MessagePackObject]
public record LevelUpEvent(
    [property: Key(0)] string PlayerId,  // Needed so clients know which entity leveled
    [property: Key(1)] int NewLevel,
    [property: Key(2)] Point Position
);
```

Add to WorldSnapshot (only LevelUpEvents - XP is sent separately):
```csharp
[Key(11)] public List<LevelUpEvent> LevelUpEvents { get; init; } = new();
```

### 3. Update Combat System (`Mud.Server/Services/GameLoopService.cs`)

**Modify ProcessAttack to use attribute-based damage:**

```csharp
public void ProcessAttack(WorldState world, string attackerId, string targetId, bool isMelee)
{
    var attacker = world.GetEntity(attackerId);
    var target = world.GetEntity(targetId);
    if (attacker == null || target == null) return;

    // Calculate damage from attributes
    int damage = isMelee
        ? ProgressionFormulas.MeleeDamage(attacker.Strength)
        : ProgressionFormulas.RangedDamage(attacker.Dexterity);

    var targetPosition = target.Position;
    var newTarget = target with { Health = target.Health - damage };

    if (newTarget.Health <= 0)
    {
        world.RemoveEntity(targetId);

        // Award XP to all players in this instance
        if (target.Type == EntityType.Monster)
        {
            AwardXpToInstance(world, targetPosition);
        }
    }
    else
    {
        world.UpdateEntity(newTarget);
    }

    // Record attack event (existing logic)
    var attackEvent = new AttackEvent(attackerId, targetId, damage, isMelee, targetPosition);
    // ...
}
```

**Add XP award method:**

```csharp
private void AwardXpToInstance(WorldState world, Point killedPosition)
{
    var players = world.GetEntities()
        .Where(e => e.Type == EntityType.Player)
        .ToList();

    foreach (var player in players)
    {
        if (player.Level >= ProgressionFormulas.MaxLevel) continue;

        var newXp = player.Experience + ProgressionFormulas.XpPerKill;
        var newLevel = player.Level;
        var newUnspent = player.UnspentPoints;
        var newHealth = player.Health;
        var leveledUp = false;

        // Check for level up(s)
        while (newLevel < ProgressionFormulas.MaxLevel &&
               newXp >= ProgressionFormulas.ExperienceForLevel(newLevel))
        {
            newLevel++;
            newUnspent += ProgressionFormulas.PointsPerLevel;
            leveledUp = true;
        }

        // Cap XP at max level threshold
        if (newLevel >= ProgressionFormulas.MaxLevel)
        {
            newXp = ProgressionFormulas.ExperienceForLevel(ProgressionFormulas.MaxLevel);
        }

        // Full heal on level up
        if (leveledUp)
        {
            newHealth = ProgressionFormulas.MaxHealth(player.Stamina);
            // Record level up event
            RecordLevelUpEvent(world.Id, player.Id, newLevel, player.Position);
        }

        // Record XP gain event
        RecordXpGainEvent(world.Id, player.Id, ProgressionFormulas.XpPerKill, killedPosition);

        world.UpdateEntity(player with
        {
            Experience = newXp,
            Level = newLevel,
            UnspentPoints = newUnspent,
            Health = newHealth
        });
    }
}
```

**Track events (XP keyed by player, LevelUp keyed by world):**

```csharp
// XP events: keyed by world, then by player (for per-player sends)
private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, List<XpGainEvent>>>
    _worldPlayerXpEvents = new();

// Level-up events: keyed by world only (broadcast to all in world)
private readonly ConcurrentDictionary<string, ConcurrentBag<LevelUpEvent>> _worldLevelUpEvents = new();
```

### 4. Add Stat Allocation Hub Method (`Mud.Server/Hubs/GameHub.cs`)

```csharp
public void AllocateStat(string statName)
{
    _gameLoop.AllocateStat(PlayerId, statName);
}
```

### 5. Add Stat Allocation Logic (`Mud.Server/Services/GameLoopService.cs`)

```csharp
public void AllocateStat(PlayerId playerId, string statName)
{
    var (world, player) = FindPlayer(playerId);
    if (world == null || player == null) return;
    if (player.UnspentPoints <= 0) return;

    var updated = statName.ToLowerInvariant() switch
    {
        "strength" => player with
        {
            Strength = player.Strength + 1,
            UnspentPoints = player.UnspentPoints - 1
        },
        "dexterity" => player with
        {
            Dexterity = player.Dexterity + 1,
            UnspentPoints = player.UnspentPoints - 1
        },
        "stamina" => AllocateStamina(player),
        _ => player
    };

    world.UpdateEntity(updated);
}

private Entity AllocateStamina(Entity player)
{
    var newStamina = player.Stamina + 1;
    var newMaxHealth = ProgressionFormulas.MaxHealth(newStamina);
    var healthIncrease = ProgressionFormulas.HealthPerStamina;

    return player with
    {
        Stamina = newStamina,
        MaxHealth = newMaxHealth,
        Health = player.Health + healthIncrease,
        UnspentPoints = player.UnspentPoints - 1
    };
}
```

### 6. Update Entity Initialization

When creating a new player, calculate initial MaxHealth from Stamina:

```csharp
var player = new Entity
{
    Id = playerId.Value,
    Name = name,
    Position = spawnPoint,
    Type = EntityType.Player,
    Level = 1,
    Experience = 0,
    Strength = 5,
    Dexterity = 5,
    Stamina = 5,
    UnspentPoints = 0,
    Health = ProgressionFormulas.MaxHealth(5),  // 100
    MaxHealth = ProgressionFormulas.MaxHealth(5)
};
```

### 7. Refactor to SignalR Groups (`Mud.Server/Services/GameLoopService.cs`)

**Add players to groups on join/world transition:**

```csharp
// In AddPlayer or world transition logic
await _hubContext.Groups.AddToGroupAsync(playerId.Value, worldId);

// On leaving a world
await _hubContext.Groups.RemoveFromGroupAsync(playerId.Value, oldWorldId);
```

**Update Broadcast method to use groups:**

```csharp
private async Task Broadcast()
{
    foreach (var (worldId, world) in _worlds)
    {
        var snapshot = world.ToSnapshot(_tick, includeTiles: false, levelUpEvents);

        // Broadcast shared snapshot to entire world group
        await _hubContext.Clients.Group(worldId)
            .SendAsync("OnWorldUpdate", snapshot);

        // Send XP events individually to each player who earned XP this tick
        if (_worldPlayerXpEvents.TryGetValue(worldId, out var playerXpEvents))
        {
            foreach (var (playerId, xpEvents) in playerXpEvents)
            {
                if (xpEvents.Count > 0)
                {
                    await _hubContext.Clients.Client(playerId)
                        .SendAsync("OnXpGain", xpEvents);
                }
            }
            playerXpEvents.Clear();
        }
    }
}
```

### 8. Update GameClient for XP Events (`Mud.Client/Services/GameClient.cs`)

**Add handler for per-player XP events:**

```csharp
public event Action<List<XpGainEvent>>? OnXpGain;

// In connection setup
_connection.On<List<XpGainEvent>>("OnXpGain", xpEvents =>
{
    OnXpGain?.Invoke(xpEvents);
});
```

### 9. Add Render Commands (`Mud.Client/Rendering/RenderCommand.cs`)

```csharp
// Generalized floating text - replaces FloatingDamage, FloatingXp, FloatingLevelUp
public record FloatingTextCommand(
    int X,
    int Y,
    string Text,
    string Color,      // Hex color e.g. "#ff0000"
    int OffsetY,       // Positive = down, negative = up
    int DurationMs = 1000
) : RenderCommand("FloatingText", null);

public record UpdateLevelDisplayCommand(
    string EntityId, int Level
) : RenderCommand("UpdateLevelDisplay", EntityId);
```

**Note:** This replaces the existing `FloatingDamageCommand` for consistency. All floating text now uses the same generalized command.

### 10. Update Command Buffer (`Mud.Client/Rendering/RenderCommandBuffer.cs`)

```csharp
// Generalized floating text method
public RenderCommandBuffer FloatingText(int x, int y, string text, string color, int offsetY, int durationMs = 1000)
{
    _commands.Add(new FloatingTextCommand(x, y, text, color, offsetY, durationMs));
    return this;
}

// Convenience methods for common cases
public RenderCommandBuffer FloatingDamage(int x, int y, int damage)
    => FloatingText(x, y, $"-{damage}", "#ff0000", 20, 1000);

public RenderCommandBuffer FloatingXp(int x, int y, int amount)
    => FloatingText(x, y, $"+{amount} XP", "#00ff00", -30, 1000);

public RenderCommandBuffer FloatingLevelUp(int x, int y)
    => FloatingText(x, y, "Level Up!", "#ffffff", -40, 1500);

public RenderCommandBuffer UpdateLevelDisplay(string entityId, int level)
{
    _commands.Add(new UpdateLevelDisplayCommand(entityId, level));
    return this;
}
```

**Note:** Convenience methods delegate to `FloatingText`, keeping call sites clean while using a single implementation.

### 11. Update GameRenderer (`Mud.Client/Rendering/GameRenderer.cs`)

**Process level events from snapshot:**

```csharp
// Process level up events (from grouped snapshot - everyone sees these)
if (snapshot.LevelUpEvents?.Count > 0)
{
    foreach (var levelEvent in snapshot.LevelUpEvents)
    {
        _buffer.FloatingLevelUp(levelEvent.Position.X, levelEvent.Position.Y);
    }
}

// Update level displays when entity levels change
foreach (var entity in snapshot.Entities.Where(e => e.Type == EntityType.Player))
{
    if (EntityLevelChanged(entity))
    {
        _buffer.UpdateLevelDisplay(entity.Id, entity.Level);
    }
}
```

**Add separate method for XP events (called from Home.razor's OnXpGain handler):**

```csharp
public void ProcessXpEvents(List<XpGainEvent> xpEvents)
{
    foreach (var xpEvent in xpEvents)
    {
        _buffer.FloatingXp(xpEvent.Position.X, xpEvent.Position.Y, xpEvent.Amount);
    }
}
```

### 12. Update Phaser Renderer (`Mud.Server/wwwroot/phaser-renderer.js`)

**Replace `floatingDamage` with generalized `floatingText`:**

```javascript
function floatingText(cmd) {
    const { x, y, text, color, offsetY, durationMs } = cmd;
    const duration = durationMs || 1000;

    const textObj = mainScene.add.text(
        x * TILE_SIZE + TILE_SIZE / 2,
        y * TILE_SIZE,
        text,
        {
            fontSize: '12px',
            fontFamily: 'monospace',
            color: color,
            stroke: '#000000',
            strokeThickness: 2
        }
    );
    textObj.setOrigin(0.5, 1);
    textObj.setDepth(200);

    mainScene.tweens.add({
        targets: textObj,
        y: textObj.y + offsetY,  // Positive = down, negative = up
        alpha: 0,
        duration: duration,
        ease: 'Power1.easeOut',
        onComplete: () => textObj.destroy()
    });
}
```

**Usage examples (generated by C# convenience methods):**
- Damage: `{ text: "-10", color: "#ff0000", offsetY: 20 }` (floats down)
- XP: `{ text: "+25 XP", color: "#00ff00", offsetY: -30 }` (floats up)
- Level Up: `{ text: "Level Up!", color: "#ffffff", offsetY: -40 }` (floats up)

**Add level to health bar:**

Modify `createHealthBar` to include level text:

```javascript
function createHealthBar(cmd) {
    // ... existing health bar creation ...

    // Add level text to the left of the health bar
    const levelText = mainScene.add.text(
        -25, 0,  // Position relative to health bar
        `${cmd.level || 1}`,
        {
            fontSize: '10px',
            fontFamily: 'monospace',
            color: '#ffffff',
            stroke: '#000000',
            strokeThickness: 1
        }
    );
    levelText.setOrigin(0.5, 0.5);

    // Store reference for updates
    healthBar.levelText = levelText;
    container.add(levelText);
}

function updateLevelDisplay(cmd) {
    const healthBar = healthBars.get(cmd.entityId);
    if (healthBar?.levelText) {
        healthBar.levelText.setText(`${cmd.level}`);
    }
}
```

**Update command dispatcher:**

```javascript
case 'FloatingText': return floatingText(cmd);  // Replaces FloatingDamage
case 'UpdateLevelDisplay': return updateLevelDisplay(cmd);
```

**Remove:** The old `case 'FloatingDamage'` and `floatingDamage()` function.

### 13. Character Sheet UI (`Mud.Client/Components/CharacterSheet.razor`)

Create a new Blazor component:

```razor
@inject GameClient GameClient

<div class="character-sheet-overlay @(IsOpen ? "open" : "")" @onclick="OnOverlayClick">
    <div class="character-sheet" @onclick:stopPropagation>
        <h2>@PlayerName</h2>

        <div class="level-section">
            <span class="level">Level @Level</span>
            <div class="xp-bar">
                <div class="xp-fill" style="width: @XpPercentage%"></div>
            </div>
            <span class="xp-text">@Experience / @ExperienceToNext XP</span>
        </div>

        <div class="attributes">
            <div class="attribute">
                <span class="attr-name">Strength</span>
                <span class="attr-value">@Strength</span>
                @if (UnspentPoints > 0)
                {
                    <button class="add-btn" @onclick="() => AllocateStat("strength")">+</button>
                }
            </div>
            <div class="attribute">
                <span class="attr-name">Dexterity</span>
                <span class="attr-value">@Dexterity</span>
                @if (UnspentPoints > 0)
                {
                    <button class="add-btn" @onclick="() => AllocateStat("dexterity")">+</button>
                }
            </div>
            <div class="attribute">
                <span class="attr-name">Stamina</span>
                <span class="attr-value">@Stamina</span>
                @if (UnspentPoints > 0)
                {
                    <button class="add-btn" @onclick="() => AllocateStat("stamina")">+</button>
                }
            </div>
        </div>

        @if (UnspentPoints > 0)
        {
            <div class="unspent">@UnspentPoints unspent points</div>
        }

        <div class="derived-stats">
            <div>Melee Damage: @(5 + Strength)</div>
            <div>Ranged Damage: @(5 + Dexterity)</div>
            <div>Max Health: @(50 + Stamina * 10)</div>
        </div>
    </div>
</div>

@code {
    [Parameter] public bool IsOpen { get; set; }
    [Parameter] public EventCallback OnClose { get; set; }

    [Parameter] public string PlayerName { get; set; } = "";
    [Parameter] public int Level { get; set; }
    [Parameter] public int Experience { get; set; }
    [Parameter] public int Strength { get; set; }
    [Parameter] public int Dexterity { get; set; }
    [Parameter] public int Stamina { get; set; }
    [Parameter] public int UnspentPoints { get; set; }

    private int ExperienceToNext => 100 * Level * Level;
    private int PreviousLevelXp => Level > 1 ? 100 * (Level - 1) * (Level - 1) : 0;
    private double XpPercentage => (double)(Experience - PreviousLevelXp) / (ExperienceToNext - PreviousLevelXp) * 100;

    private async Task AllocateStat(string stat)
    {
        await GameClient.AllocateStatAsync(stat);
    }

    private async Task OnOverlayClick()
    {
        await OnClose.InvokeAsync();
    }
}
```

### 14. Character Sheet Styling (`Mud.Client/Components/CharacterSheet.razor.css`)

```css
.character-sheet-overlay {
    display: none;
    position: fixed;
    top: 0;
    left: 0;
    width: 100%;
    height: 100%;
    background: rgba(0, 0, 0, 0.5);
    justify-content: center;
    align-items: center;
    z-index: 1000;
}

.character-sheet-overlay.open {
    display: flex;
}

.character-sheet {
    background: #1a1a2e;
    border: 2px solid #4a4a6a;
    padding: 20px;
    min-width: 300px;
    color: #e0e0e0;
    font-family: monospace;
}

.character-sheet h2 {
    margin: 0 0 15px 0;
    color: #fff;
    border-bottom: 1px solid #4a4a6a;
    padding-bottom: 10px;
}

.level-section {
    margin-bottom: 20px;
}

.level {
    font-size: 18px;
    color: #ffd700;
}

.xp-bar {
    height: 8px;
    background: #333;
    margin: 8px 0;
    border-radius: 4px;
    overflow: hidden;
}

.xp-fill {
    height: 100%;
    background: linear-gradient(90deg, #4a90d9, #63b3ed);
    transition: width 0.3s ease;
}

.xp-text {
    font-size: 12px;
    color: #888;
}

.attributes {
    margin: 20px 0;
}

.attribute {
    display: flex;
    align-items: center;
    margin: 10px 0;
    gap: 10px;
}

.attr-name {
    width: 100px;
}

.attr-value {
    width: 30px;
    text-align: center;
    font-weight: bold;
    color: #63b3ed;
}

.add-btn {
    background: #2d5a27;
    border: 1px solid #4a9a3a;
    color: #fff;
    width: 24px;
    height: 24px;
    cursor: pointer;
    font-weight: bold;
}

.add-btn:hover {
    background: #3d7a37;
}

.unspent {
    color: #ffd700;
    text-align: center;
    margin: 15px 0;
}

.derived-stats {
    border-top: 1px solid #4a4a6a;
    padding-top: 15px;
    font-size: 13px;
    color: #aaa;
}

.derived-stats div {
    margin: 5px 0;
}
```

### 15. Integrate Character Sheet in Home.razor

**Add component and state:**

```razor
@* At top of file *@
<CharacterSheet
    IsOpen="_characterSheetOpen"
    OnClose="CloseCharacterSheet"
    PlayerName="@_playerEntity?.Name"
    Level="@(_playerEntity?.Level ?? 1)"
    Experience="@(_playerEntity?.Experience ?? 0)"
    Strength="@(_playerEntity?.Strength ?? 5)"
    Dexterity="@(_playerEntity?.Dexterity ?? 5)"
    Stamina="@(_playerEntity?.Stamina ?? 5)"
    UnspentPoints="@(_playerEntity?.UnspentPoints ?? 0)" />

@code {
    private bool _characterSheetOpen = false;
    private Entity? _playerEntity;

    private void ToggleCharacterSheet()
    {
        _characterSheetOpen = !_characterSheetOpen;
    }

    private void CloseCharacterSheet()
    {
        _characterSheetOpen = false;
    }
}
```

**Add keyboard handling:**

```csharp
private async Task HandleKeyDown(KeyboardEventArgs e)
{
    // Character sheet toggle
    if (e.Key == "c" || e.Key == "C")
    {
        ToggleCharacterSheet();
        return;
    }

    // Escape closes character sheet
    if (e.Key == "Escape" && _characterSheetOpen)
    {
        CloseCharacterSheet();
        return;
    }

    // Block game input while character sheet is open
    if (_characterSheetOpen) return;

    // ... existing input handling ...
}
```

**Update player entity reference on snapshot:**

```csharp
private void HandleWorldUpdate(WorldSnapshot snapshot)
{
    _snapshot = snapshot;
    _playerEntity = snapshot.Entities.FirstOrDefault(e => e.Id == _playerId);
    // ... existing logic ...
}
```

**Subscribe to XP events and process them:**

```csharp
// In OnInitializedAsync or similar
GameClient.OnXpGain += HandleXpGain;

private void HandleXpGain(List<XpGainEvent> xpEvents)
{
    _renderer.ProcessXpEvents(xpEvents);
}
```

### 16. Add GameClient AllocateStat Method (`Mud.Client/Services/GameClient.cs`)

```csharp
public async Task AllocateStatAsync(string statName)
{
    if (_connection?.State == HubConnectionState.Connected)
    {
        await _connection.InvokeAsync("AllocateStat", statName);
    }
}
```

## Implementation Order

1. **Shared Models** - Add progression fields to Entity, add event types, update WorldSnapshot (LevelUpEvents only)
2. **Progression Formulas** - Create static helper class with all constants and calculations
3. **Server Combat** - Update ProcessAttack to use attribute damage
4. **Server XP Logic** - Add AwardXpToInstance, level-up detection, event recording
5. **Server Stat Allocation** - Add AllocateStat hub method and logic
6. **Server Entity Init** - Update player creation with initial stats
7. **Server SignalR Groups** - Refactor broadcast to use groups, add per-player XP sends
8. **Client GameClient** - Add OnXpGain event handler, AllocateStatAsync method
9. **Client Render Commands** - Add FloatingXp, FloatingLevelUp, UpdateLevelDisplay
10. **Client GameRenderer** - Process level events from snapshot, add ProcessXpEvents method
11. **Phaser JS** - Add floating text functions, update health bar with level
12. **Character Sheet Component** - Create Blazor component with CSS
13. **Home.razor Integration** - Wire up C key, Escape, XP event subscription
14. **Testing** - Verify full flow: kill → XP → level → allocate → damage increase

## Edge Cases

| Scenario | Handling |
|----------|----------|
| Multiple kills same tick | Each triggers XP award; multiple level-ups possible |
| Level up from 59 to 60 | XP caps at level 60 threshold |
| Already at level 60 | Skip XP award entirely |
| Character sheet open during combat | Input blocked; can still take damage |
| No unspent points | + buttons hidden |
| Rapid stat allocation clicks | Server validates UnspentPoints > 0 each time |
| Player dies then levels up | N/A - death removes from world (no persistence yet) |

## Files Modified

| File | Changes |
|------|---------|
| `Mud.Shared/Models.cs` | Add progression fields to Entity, add XpGainEvent, LevelUpEvent, update WorldSnapshot (LevelUpEvents only) |
| `Mud.Shared/ProgressionFormulas.cs` | New file with constants and calculation methods |
| `Mud.Server/Services/GameLoopService.cs` | Attribute-based damage, XP awards, stat allocation, event tracking, SignalR groups, per-player XP broadcast |
| `Mud.Server/Hubs/GameHub.cs` | Add AllocateStat method, group management on join/disconnect |
| `Mud.Client/Rendering/RenderCommand.cs` | Replace FloatingDamageCommand with generalized FloatingTextCommand, add UpdateLevelDisplayCommand |
| `Mud.Client/Rendering/RenderCommandBuffer.cs` | Add FloatingText base method, convenience methods (FloatingDamage, FloatingXp, FloatingLevelUp), UpdateLevelDisplay |
| `Mud.Client/Rendering/GameRenderer.cs` | Process level events from snapshot, add ProcessXpEvents method |
| `Mud.Client/Components/CharacterSheet.razor` | New component |
| `Mud.Client/Components/CharacterSheet.razor.css` | New stylesheet |
| `Mud.Client/Pages/Home.razor` | Integrate character sheet, C key handling, XP event subscription |
| `Mud.Client/Services/GameClient.cs` | Add OnXpGain event, AllocateStatAsync method |
| `Mud.Server/wwwroot/phaser-renderer.js` | Replace floatingDamage with generalized floatingText, add level display to health bars |

## Future Considerations (Out of Scope)

- Database persistence for progression
- Monster XP scaling by type/level
- Racial starting stat variations
- Stat respec/reset functionality
- Visual indicator for unspent points in HUD
