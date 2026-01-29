# Task: Migrate Rendering to Phaser 4 Thin Interop Architecture

## Objective

Replace the current PixiJS rendering layer with Phaser 4 using a "thin interop" architecture where C# owns all game client logic and Phaser acts as a stateless command executor.

## Problem Statement

The current `game.js` (~477 lines) mixes rendering with game logic:
- Camera smoothing lives in JS
- Animation timing would need to be in JS
- Future features (bump animations, spell effects) would bloat the JS further
- Type mismatches between C# and JS cause friction

## Success Criteria

1. All game client logic (camera, animations, effects) lives in C#
2. JS/Phaser is a dumb command executor with no game logic
3. Single interop call per server tick (command buffer pattern)
4. Feature parity with current rendering (terrain, entities, health bars, targeting)
5. Foundation for future animations (bump attacks, projectiles, particles)
6. Bundle size under 200KB gzipped for Phaser

## Architecture Overview

```
┌─────────────────────────────────────────────────────────────┐
│                     C# (Mud.Client)                         │
│  ┌─────────────────────────────────────────────────────┐   │
│  │                   GameRenderer                       │   │
│  │  - Scene graph state (what entities exist)          │   │
│  │  - Diff tracking (what changed since last tick)     │   │
│  │  - Animation decisions (when to bump, fire, etc.)   │   │
│  └────────────────────────┬────────────────────────────┘   │
│                           │                                 │
│                           ▼                                 │
│  ┌─────────────────────────────────────────────────────┐   │
│  │              RenderCommandBuffer                     │   │
│  │  Commands: CreateSprite, TweenTo, TweenCamera, etc. │   │
│  └────────────────────────┬────────────────────────────┘   │
│                           │ Flush() - single interop call  │
└───────────────────────────┼────────────────────────────────┘
                            ▼
┌─────────────────────────────────────────────────────────────┐
│                   JS (phaser-renderer.js)                   │
│  ┌──────────────────────────────────────────────────────┐  │
│  │              CommandDispatcher                        │  │
│  │  - Execute commands in order                         │  │
│  │  - Manage Phaser game objects by ID                  │  │
│  │  - Trigger tweens (Phaser handles per-frame interp)  │  │
│  │  - NO game logic                                     │  │
│  └──────────────────────────────────────────────────────┘  │
│                         │                                   │
│                         ▼                                   │
│  ┌──────────────────────────────────────────────────────┐  │
│  │                   Phaser 4 Engine                     │  │
│  │  - WebGL rendering                                   │  │
│  │  - Tween system (handles 60fps interpolation)        │  │
│  │  - Particle system                                   │  │
│  │  - Camera tweening                                   │  │
│  └──────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────┘
```

**Key Principle:** C# sends *targets* (where things should go), Phaser handles *interpolation* (smooth 60fps movement). Interop only happens on server ticks (~3/second), not every frame.

## Technical Approach

### Phase 1: C# Command Infrastructure

#### 1.1 RenderCommand Types (Mud.Client/Rendering/Commands/)

```csharp
// Base command
public abstract record RenderCommand(string Type, string? EntityId);

// Sprite lifecycle
public record CreateSpriteCommand(string EntityId, int TileIndex, int X, int Y,
    uint? Tint = null, int? Depth = null) : RenderCommand("CreateSprite", EntityId);

public record DestroySpriteCommand(string EntityId) : RenderCommand("DestroySprite", EntityId);

public record SetPositionCommand(string EntityId, int X, int Y) : RenderCommand("SetPosition", EntityId);

public record SetTintCommand(string EntityId, uint Tint) : RenderCommand("SetTint", EntityId);

public record SetVisibleCommand(string EntityId, bool Visible) : RenderCommand("SetVisible", EntityId);

public record SetDepthCommand(string EntityId, int Depth) : RenderCommand("SetDepth", EntityId);

// Movement/Animation
public record TweenToCommand(string EntityId, int X, int Y, int DurationMs = 300,
    string Easing = "Sine.easeInOut") : RenderCommand("TweenTo", EntityId);

public record PlayAnimationCommand(string EntityId, string AnimationKey,
    bool Loop = false) : RenderCommand("PlayAnimation", EntityId);

public record BumpAttackCommand(string AttackerId, string TargetId,
    int DurationMs = 150) : RenderCommand("BumpAttack", null);

// Effects
public record SpawnParticlesCommand(string ParticleType, int X, int Y,
    Dictionary<string, object>? Config = null) : RenderCommand("SpawnParticles", null);

public record SpawnProjectileCommand(string FromEntityId, string ToEntityId,
    int TileIndex, int Speed = 200, ProjectileTrailConfig? Trail = null)
    : RenderCommand("SpawnProjectile", null);

// Camera (C# sends target, Phaser tweens to it)
public record TweenCameraCommand(int X, int Y, int DurationMs, string Easing = "Sine.easeOut")
    : RenderCommand("TweenCamera", null);

public record SnapCameraCommand(int X, int Y)
    : RenderCommand("SnapCamera", null);

// Health bars
public record CreateHealthBarCommand(string EntityId, int MaxHealth, int CurrentHealth)
    : RenderCommand("CreateHealthBar", EntityId);

public record UpdateHealthBarCommand(string EntityId, int CurrentHealth)
    : RenderCommand("UpdateHealthBar", EntityId);

// Terrain (batch operation)
public record SetTerrainCommand(string WorldId, List<TileRenderData> Tiles,
    int Width, int Height, int GhostPadding) : RenderCommand("SetTerrain", null);

// Queued path visualization
public record SetQueuedPathCommand(string EntityId, List<Point> Path)
    : RenderCommand("SetQueuedPath", EntityId);

// Targeting reticle
public record SetTargetReticleCommand(string? EntityId) : RenderCommand("SetTargetReticle", EntityId);
```

#### 1.2 RenderCommandBuffer (Mud.Client/Rendering/RenderCommandBuffer.cs)

```csharp
public class RenderCommandBuffer
{
    private readonly List<RenderCommand> _commands = new();
    private readonly IJSRuntime _js;

    public RenderCommandBuffer(IJSRuntime js) => _js = js;

    // Fluent API for building commands
    public RenderCommandBuffer CreateSprite(string id, int tileIndex, int x, int y, uint? tint = null)
    {
        _commands.Add(new CreateSpriteCommand(id, tileIndex, x, y, tint));
        return this;
    }

    public RenderCommandBuffer TweenTo(string id, int x, int y, int durationMs = 300)
    {
        _commands.Add(new TweenToCommand(id, x, y, durationMs));
        return this;
    }

    // ... other fluent methods

    public async ValueTask FlushAsync()
    {
        if (_commands.Count == 0) return;

        await _js.InvokeVoidAsync("executeCommands", _commands);
        _commands.Clear();
    }

    public void Clear() => _commands.Clear();
}
```

#### 1.3 GameRenderer (Mud.Client/Rendering/GameRenderer.cs)

Owns the scene graph state and produces diffs. **No per-frame updates** - all interpolation handled by Phaser.

```csharp
public class GameRenderer
{
    private readonly RenderCommandBuffer _buffer;
    private readonly Dictionary<string, EntityRenderState> _entities = new();
    private string? _currentWorldId;
    private string? _targetEntityId;
    private string? _myPlayerId;
    private bool _cameraInitialized;

    // Server tick interval - camera tweens should match this duration
    private const int TickDurationMs = 300;
    private const int TileSize = 20;
    private const int CenterX = 400;
    private const int CenterY = 300;

    public void ProcessSnapshot(WorldSnapshot snapshot, string? targetId, string playerId)
    {
        _myPlayerId = playerId;

        // Handle world change (terrain)
        if (snapshot.WorldId != _currentWorldId && snapshot.Tiles != null)
        {
            _buffer.SetTerrain(snapshot.WorldId, snapshot.Tiles, ...);
            _currentWorldId = snapshot.WorldId;
        }

        // Track which entities are in this snapshot
        var currentEntityIds = snapshot.Entities.Select(e => e.Id).ToHashSet();

        // Remove entities no longer present
        foreach (var id in _entities.Keys.Except(currentEntityIds).ToList())
        {
            _buffer.DestroySprite(id);
            _buffer.DestroySprite($"{id}_health");
            _entities.Remove(id);
        }

        // Create or update entities
        foreach (var entity in snapshot.Entities)
        {
            if (!_entities.TryGetValue(entity.Id, out var state))
            {
                // New entity - create sprite and health bar
                var tileIndex = entity.Type == EntityType.Player ? 425 : 458; // (25,0) or (26,2)
                var tint = entity.Type == EntityType.Player ? 0xFFFF00u : 0xFF0000u;

                _buffer.CreateSprite(entity.Id, tileIndex, entity.Position.X, entity.Position.Y, tint);
                _buffer.CreateHealthBar(entity.Id, entity.MaxHealth, entity.Health);

                state = new EntityRenderState(entity);
                _entities[entity.Id] = state;
            }
            else
            {
                // Existing entity - check for changes
                if (entity.Position != state.Position)
                {
                    // Queue smooth movement (Phaser handles interpolation)
                    _buffer.TweenTo(entity.Id, entity.Position.X, entity.Position.Y, TickDurationMs);
                }

                if (entity.Health != state.Health)
                {
                    _buffer.UpdateHealthBar(entity.Id, entity.Health);
                }

                state.Update(entity);
            }

            // Update queued path
            _buffer.SetQueuedPath(entity.Id, entity.QueuedPath ?? new());
        }

        // Camera follows player - send target, Phaser tweens to it
        var player = snapshot.Entities.FirstOrDefault(e => e.Id == playerId);
        if (player != null)
        {
            var camX = CenterX - (player.Position.X * TileSize);
            var camY = CenterY - (player.Position.Y * TileSize);

            if (!_cameraInitialized)
            {
                // First frame: snap camera instantly
                _buffer.SnapCamera(camX, camY);
                _cameraInitialized = true;
            }
            else
            {
                // Subsequent frames: smooth tween over tick duration
                _buffer.TweenCamera(camX, camY, TickDurationMs);
            }
        }

        // Targeting reticle
        if (targetId != _targetEntityId)
        {
            _buffer.SetTargetReticle(targetId);
            _targetEntityId = targetId;
        }

        // POIs and exit markers
        ProcessPOIs(snapshot.POIs);
        ProcessExitMarker(snapshot.ExitMarker);
    }

    public async ValueTask FlushAsync() => await _buffer.FlushAsync();
}
```

**Note:** No `Update(float deltaTime)` method - all per-frame interpolation is handled by Phaser's tween system. C# only runs on server ticks.

### Phase 2: Phaser 4 Renderer (JavaScript)

#### 2.1 phaser-renderer.js Structure

```javascript
// phaser-renderer.js - Thin command executor

let game = null;
let mainScene = null;
const entities = new Map();      // entityId -> Phaser.GameObjects.Sprite
const healthBars = new Map();    // entityId -> { bg, fg, maxHealth, width }
const queuedPaths = new Map();   // entityId -> Phaser.GameObjects.Graphics[]
let targetReticle = null;
let terrainContainer = null;

// Constants
const TILE_SIZE = 20;
const TILESET_COLS = 16;
const TILESET_TILE_SIZE = 16;
const TILESET_SPACING = 1;

// ============ INITIALIZATION ============

window.initPhaser = async function(containerId) {
    const container = document.getElementById(containerId);

    const config = {
        type: Phaser.AUTO,
        width: 800,
        height: 600,
        parent: container,
        pixelArt: true,
        roundPixels: true,
        backgroundColor: '#000000',
        scene: {
            preload: preload,
            create: create
        }
    };

    game = new Phaser.Game(config);

    return new Promise(resolve => {
        game.events.once('ready', resolve);
    });
};

function preload() {
    this.load.spritesheet('tileset', 'assets/colored-transparent.png', {
        frameWidth: TILESET_TILE_SIZE,
        frameHeight: TILESET_TILE_SIZE,
        spacing: TILESET_SPACING,
        margin: 0
    });
}

function create() {
    mainScene = this;

    // Create layer containers (depth ordering)
    terrainContainer = this.add.container(0, 0);
    terrainContainer.setDepth(0);

    // Notify C# that renderer is ready
    if (window.onPhaserReady) {
        window.onPhaserReady();
    }
}

// ============ COMMAND DISPATCHER ============

window.executeCommands = function(commands) {
    if (!mainScene) {
        console.warn('Scene not ready');
        return;
    }

    for (const cmd of commands) {
        try {
            executeCommand(cmd);
        } catch (e) {
            console.error(`Command ${cmd.type} failed:`, e);
        }
    }
};

function executeCommand(cmd) {
    switch (cmd.type) {
        case 'CreateSprite': return createSprite(cmd);
        case 'DestroySprite': return destroySprite(cmd);
        case 'SetPosition': return setPosition(cmd);
        case 'TweenTo': return tweenTo(cmd);
        case 'SetTint': return setTint(cmd);
        case 'SetVisible': return setVisible(cmd);
        case 'SetDepth': return setDepth(cmd);
        case 'BumpAttack': return bumpAttack(cmd);
        case 'SpawnParticles': return spawnParticles(cmd);
        case 'SpawnProjectile': return spawnProjectile(cmd);
        case 'TweenCamera': return tweenCamera(cmd);
        case 'SnapCamera': return snapCamera(cmd);
        case 'CreateHealthBar': return createHealthBar(cmd);
        case 'UpdateHealthBar': return updateHealthBar(cmd);
        case 'SetTerrain': return setTerrain(cmd);
        case 'SetQueuedPath': return setQueuedPath(cmd);
        case 'SetTargetReticle': return setTargetReticle(cmd);
        default:
            console.warn(`Unknown command: ${cmd.type}`);
    }
}

// ============ COMMAND IMPLEMENTATIONS ============

function createSprite(cmd) {
    const { entityId, tileIndex, x, y, tint, depth } = cmd;

    if (entities.has(entityId)) {
        console.warn(`Entity ${entityId} already exists`);
        return;
    }

    const sprite = mainScene.add.sprite(
        x * TILE_SIZE,
        y * TILE_SIZE,
        'tileset',
        tileIndex
    );
    sprite.setOrigin(0, 0);
    sprite.setDisplaySize(TILE_SIZE, TILE_SIZE);

    if (tint !== undefined && tint !== null) {
        sprite.setTint(tint);
    }
    if (depth !== undefined && depth !== null) {
        sprite.setDepth(depth);
    } else {
        sprite.setDepth(10); // Default entity depth
    }

    entities.set(entityId, sprite);
}

function destroySprite(cmd) {
    const sprite = entities.get(cmd.entityId);
    if (sprite) {
        sprite.destroy();
        entities.delete(cmd.entityId);
    }

    // Also clean up associated health bar
    const healthBar = healthBars.get(cmd.entityId);
    if (healthBar) {
        healthBar.bg.destroy();
        healthBar.fg.destroy();
        healthBars.delete(cmd.entityId);
    }

    // And queued path
    const paths = queuedPaths.get(cmd.entityId);
    if (paths) {
        paths.forEach(g => g.destroy());
        queuedPaths.delete(cmd.entityId);
    }
}

function setPosition(cmd) {
    const sprite = entities.get(cmd.entityId);
    if (!sprite) return;

    sprite.x = cmd.x * TILE_SIZE;
    sprite.y = cmd.y * TILE_SIZE;

    // Move health bar too
    updateHealthBarPosition(cmd.entityId, cmd.x, cmd.y);
}

function tweenTo(cmd) {
    const sprite = entities.get(cmd.entityId);
    if (!sprite) return;

    mainScene.tweens.add({
        targets: sprite,
        x: cmd.x * TILE_SIZE,
        y: cmd.y * TILE_SIZE,
        duration: cmd.durationMs || 300,
        ease: cmd.easing || 'Sine.easeInOut'
    });

    // Also tween health bar
    const healthBar = healthBars.get(cmd.entityId);
    if (healthBar) {
        mainScene.tweens.add({
            targets: [healthBar.bg, healthBar.fg],
            x: cmd.x * TILE_SIZE + 1,
            y: cmd.y * TILE_SIZE - 4,
            duration: cmd.durationMs || 300,
            ease: cmd.easing || 'Sine.easeInOut'
        });
    }
}

function bumpAttack(cmd) {
    const attacker = entities.get(cmd.attackerId);
    const target = entities.get(cmd.targetId);
    if (!attacker || !target) return;

    const startX = attacker.x;
    const startY = attacker.y;
    const bumpX = startX + (target.x - startX) * 0.5;
    const bumpY = startY + (target.y - startY) * 0.5;
    const duration = cmd.durationMs || 150;

    // Bump toward target and back
    mainScene.tweens.chain({
        targets: attacker,
        tweens: [
            { x: bumpX, y: bumpY, duration: duration / 2, ease: 'Power2' },
            { x: startX, y: startY, duration: duration / 2, ease: 'Power2' }
        ]
    });

    // Flash target
    mainScene.tweens.add({
        targets: target,
        alpha: 0.3,
        yoyo: true,
        duration: 80,
        repeat: 1
    });
}

function tweenCamera(cmd) {
    // Smooth camera movement - Phaser handles per-frame interpolation
    mainScene.tweens.add({
        targets: mainScene.cameras.main,
        scrollX: -cmd.x,
        scrollY: -cmd.y,
        duration: cmd.durationMs,
        ease: cmd.easing || 'Sine.easeOut'
    });
}

function snapCamera(cmd) {
    // Instant camera position (first frame, world transitions)
    mainScene.cameras.main.setScroll(-cmd.x, -cmd.y);
}

function createHealthBar(cmd) {
    const { entityId, maxHealth, currentHealth } = cmd;
    const sprite = entities.get(entityId);
    if (!sprite) return;

    const width = 14;
    const height = 2;
    const x = sprite.x + 1;
    const y = sprite.y - 4;

    const bg = mainScene.add.rectangle(x, y, width, height, 0x333333);
    bg.setOrigin(0, 0);
    bg.setDepth(100);

    const healthWidth = (currentHealth / maxHealth) * width;
    const fg = mainScene.add.rectangle(x, y, healthWidth, height, 0x00ff00);
    fg.setOrigin(0, 0);
    fg.setDepth(101);

    healthBars.set(entityId, { bg, fg, maxHealth, width });
}

function updateHealthBar(cmd) {
    const healthBar = healthBars.get(cmd.entityId);
    if (!healthBar) return;

    const percent = cmd.currentHealth / healthBar.maxHealth;
    const newWidth = Math.max(0, percent * healthBar.width);

    mainScene.tweens.add({
        targets: healthBar.fg,
        width: newWidth,
        duration: 200
    });

    // Color based on health
    let color = 0x00ff00;
    if (percent <= 0.3) color = 0xff0000;
    else if (percent <= 0.6) color = 0xffff00;

    healthBar.fg.setFillStyle(color);
}

function setTerrain(cmd) {
    // Clear existing terrain
    terrainContainer.removeAll(true);

    const { tiles, width, height, ghostPadding } = cmd;
    const totalWidth = width + 2 * ghostPadding;

    for (let i = 0; i < tiles.length; i++) {
        const tile = tiles[i];
        const x = i % totalWidth;
        const y = Math.floor(i / totalWidth);
        const worldX = x - ghostPadding;
        const worldY = y - ghostPadding;

        const tileConfig = getTileConfig(tile.type);
        const sprite = mainScene.add.sprite(
            worldX * TILE_SIZE,
            worldY * TILE_SIZE,
            'tileset',
            tileConfig.frame
        );
        sprite.setOrigin(0, 0);
        sprite.setDisplaySize(TILE_SIZE, TILE_SIZE);
        sprite.setTint(tileConfig.tint);

        // Ghost area dimming
        const isGhost = worldX < 0 || worldX >= width || worldY < 0 || worldY >= height;
        if (isGhost) {
            sprite.setAlpha(0.3);
        }

        terrainContainer.add(sprite);
    }
}

function setQueuedPath(cmd) {
    const { entityId, path } = cmd;

    // Clear existing path graphics
    const existing = queuedPaths.get(entityId);
    if (existing) {
        existing.forEach(g => g.destroy());
    }

    if (!path || path.length === 0) {
        queuedPaths.delete(entityId);
        return;
    }

    const graphics = path.map(pt => {
        const g = mainScene.add.graphics();
        g.fillStyle(0xffff00, 0.4);
        g.fillRect(pt.x * TILE_SIZE, pt.y * TILE_SIZE, TILE_SIZE, TILE_SIZE);
        g.setDepth(5);
        return g;
    });

    queuedPaths.set(entityId, graphics);
}

function setTargetReticle(cmd) {
    // Remove existing reticle
    if (targetReticle) {
        targetReticle.destroy();
        targetReticle = null;
    }

    if (!cmd.entityId) return;

    const target = entities.get(cmd.entityId);
    if (!target) return;

    targetReticle = mainScene.add.graphics();
    targetReticle.lineStyle(2, 0xffffff);
    targetReticle.strokeRect(0, 0, TILE_SIZE, TILE_SIZE);
    targetReticle.x = target.x;
    targetReticle.y = target.y;
    targetReticle.setDepth(150);

    // Follow target
    mainScene.tweens.add({
        targets: targetReticle,
        x: target.x,
        y: target.y,
        duration: 0,
        repeat: -1,
        onRepeat: () => {
            const t = entities.get(cmd.entityId);
            if (t) {
                targetReticle.x = t.x;
                targetReticle.y = t.y;
            }
        }
    });
}

// ============ HELPERS ============

function getTileConfig(tileType) {
    const configs = {
        0: { frame: 80, tint: 0x228B22 },   // GrassSparse
        1: { frame: 96, tint: 0x228B22 },   // GrassMedium
        2: { frame: 112, tint: 0x228B22 },  // GrassDense
        3: { frame: 88, tint: 0xFFFFFF },   // Water
        4: { frame: 86, tint: 0xFFFFFF },   // Bridge
        5: { frame: 17, tint: 0xFFFFFF },   // TreeSparse
        6: { frame: 18, tint: 0xFFFFFF },   // TreeMedium
        7: { frame: 19, tint: 0xFFFFFF },   // TreeDense
        8: { frame: 41, tint: 0xFFD700 },   // POIMarker
        9: { frame: 11, tint: 0xFF4500 },   // ExitMarker
        10: { frame: 46, tint: 0xFFFFFF }   // TownCenter
    };
    return configs[tileType] || configs[0];
}

function updateHealthBarPosition(entityId, tileX, tileY) {
    const healthBar = healthBars.get(entityId);
    if (!healthBar) return;

    healthBar.bg.x = tileX * TILE_SIZE + 1;
    healthBar.bg.y = tileY * TILE_SIZE - 4;
    healthBar.fg.x = tileX * TILE_SIZE + 1;
    healthBar.fg.y = tileY * TILE_SIZE - 4;
}
```

### Phase 3: Integration Changes

#### 3.1 Home.razor Changes

```csharp
@inject GameRenderer Renderer

// Initialization - once on first render
protected override async Task OnAfterRenderAsync(bool firstRender)
{
    if (firstRender)
    {
        await JS.InvokeVoidAsync("initPhaser", "game-container");
    }
}

// On server tick (HandleWorldUpdate) - replace current rendering:

// OLD:
await JS.InvokeVoidAsync("renderSnapshot", snapshot, _targetId, _myPlayerId);

// NEW:
Renderer.ProcessSnapshot(snapshot, _targetId, _myPlayerId);
await Renderer.FlushAsync();
```

**Key change:** No per-frame timer. C# only runs when server ticks arrive (~3/second). Phaser handles all 60fps interpolation internally via its tween system.

#### 3.2 Service Registration (Program.cs)

```csharp
builder.Services.AddScoped<RenderCommandBuffer>();
builder.Services.AddScoped<GameRenderer>();
```

### Phase 4: Future Animation Support (Foundation)

The command infrastructure supports future animations:

```csharp
// Bump attack (when combat triggers)
_buffer.BumpAttack(attackerId, targetId, durationMs: 150);

// Projectile (ranged attack)
_buffer.SpawnProjectile(fromId, toId, tileIndex: 42, speed: 200,
    trail: new ProjectileTrailConfig { Enabled = true, Tint = 0xFFAA00 });

// Particles (on hit, heal, etc.)
_buffer.SpawnParticles("hit", x, y, new { tint = 0xFF0000, quantity = 5 });
```

## Implementation Considerations

### Performance

1. **Single interop call per tick**: Command buffer batches all operations (~3 calls/second)
2. **Differential updates**: Only changed entities generate commands
3. **Phaser handles 60fps**: All interpolation (movement, camera) runs in JS via tweens
4. **No C# per-frame loop**: Eliminates interop overhead from frame updates
5. **Sprite pooling**: Phaser handles internally, we track by ID

### Bundle Size

- Use Phaser 4's tree-shakeable imports
- Exclude: Physics, Tilemaps (built-in), Sound
- Target: <150KB gzipped

### Migration Strategy

1. **Parallel implementation**: Keep game.js working while building new system
2. **Feature flag**: Toggle between old/new renderer
3. **Incremental migration**: Terrain → Entities → Effects
4. **Remove old code**: Delete game.js once feature-complete

## File Changes Summary

### New Files
- `Mud.Client/Rendering/Commands/*.cs` - Command record types
- `Mud.Client/Rendering/RenderCommandBuffer.cs` - Command batching
- `Mud.Client/Rendering/GameRenderer.cs` - Scene graph and diff tracking
- `Mud.Client/Rendering/EntityRenderState.cs` - Per-entity state tracking
- `Mud.Server/wwwroot/phaser-renderer.js` - Phaser command executor

### Modified Files
- `Mud.Client/Pages/Home.razor` - Use new renderer
- `Mud.Client/Program.cs` - Register services
- `Mud.Server/Components/App.razor` - Load Phaser 4 instead of PixiJS

### App.razor Script Changes

```html
<!-- OLD (PixiJS) -->
<script src="https://pixijs.download/release/pixi.min.js"></script>
<script src="game.js"></script>

<!-- NEW (Phaser 4 RC6) -->
<script src="https://cdn.jsdelivr.net/npm/phaser@4.0.0-rc.6/dist/phaser.min.js"></script>
<script src="phaser-renderer.js"></script>
```

**Note:** Using Phaser 4 RC6 (release candidate) which the Phaser team considers "production-ready". Can upgrade to stable 4.0.0 when released.

### Deleted Files (after migration complete)
- `Mud.Server/wwwroot/game.js` - Old PixiJS renderer

## Edge Cases & Error Handling

1. **Entity destroyed mid-tween**: Phaser handles gracefully (tween stops)
2. **Commands before init**: Buffer until `onPhaserReady` fires
3. **Missing entity references**: Log warning, skip command
4. **World change during animation**: Cancel tweens, rebuild terrain
5. **Rapid camera target changes**: New TweenCamera overwrites previous (Phaser handles gracefully)
6. **First snapshot**: Use SnapCamera (instant) instead of TweenCamera (smooth)

## Testing Checklist

- [ ] Terrain renders correctly (all tile types)
- [ ] Ghost padding/edge dimming works
- [ ] Entities appear/disappear correctly
- [ ] Entity movement is smooth (tweened)
- [ ] Health bars update and follow entities
- [ ] Camera follows player smoothly
- [ ] Queued path visualization works
- [ ] Target reticle follows selected entity
- [ ] POIs and exit markers render
- [ ] World transitions work (terrain swap)
- [ ] No visual regressions from current renderer

## Future Optimization (Post-Migration)

- [ ] Set up npm + Vite for tree-shaking
- [ ] Create custom Phaser build (exclude physics, sound)
- [ ] Target bundle size < 150KB gzipped
