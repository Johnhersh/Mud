// phaser-renderer.js - Thin command executor for Phaser 4

let game = null;
let mainScene = null;
const entities = new Map();      // entityId -> Phaser.GameObjects.Sprite
const healthBars = new Map();    // entityId -> { bg, fg, maxHealth, width }
const queuedPaths = new Map();   // entityId -> Phaser.GameObjects.Graphics[]
let targetReticle = null;

// Terrain sprite pools - pre-allocated at startup for instant world transitions
let overworldContainer = null;
let instanceContainer = null;
let overworldSprites = [];       // 190x190 = 36,100 sprites (150 + 2*20 ghost padding)
let instanceSprites = [];        // 60x60 = 3,600 sprites (50 + 2*5 ghost padding)

// World size constants (must match server WorldConfig)
const OVERWORLD_TOTAL = 190;     // 150 + 2*20
const INSTANCE_TOTAL = 60;       // 50 + 2*5

// Constants
const TILE_SIZE = 20;
const TILESET_TILE_SIZE = 16;
const TILESET_SPACING = 1;

// Tileset columns - determined after loading the spritesheet
let TILESET_COLS = 49;  // Default, will be updated when texture loads

// Helper to convert (x, y) grid coordinates to frame index
function xyToFrame(x, y) {
    return y * TILESET_COLS + x;
}

// ============ INITIALIZATION ============

let initResolve = null;

window.initPhaser = async function(containerId) {
    console.log('initPhaser called with container:', containerId);

    const container = document.getElementById(containerId);
    if (!container) {
        console.error('Container not found:', containerId);
        return;
    }

    const config = {
        type: Phaser.AUTO,
        width: 800,
        height: 600,
        parent: containerId,  // Use string ID instead of element
        pixelArt: true,
        roundPixels: true,
        backgroundColor: '#000000',
        scene: {
            preload: preload,
            create: create
        }
    };

    return new Promise(resolve => {
        initResolve = resolve;
        game = new Phaser.Game(config);
        console.log('Phaser Game created');
    });
};

function preload() {
    console.log('Phaser preload starting');
    this.load.spritesheet('tileset', 'assets/colored-transparent.png', {
        frameWidth: TILESET_TILE_SIZE,
        frameHeight: TILESET_TILE_SIZE,
        spacing: TILESET_SPACING,
        margin: 0
    });

    // Determine tileset columns from image dimensions
    this.load.on('filecomplete-spritesheet-tileset', (key, type, texture) => {
        const tex = this.textures.get('tileset');
        if (tex && tex.source && tex.source[0]) {
            const imgWidth = tex.source[0].width;
            TILESET_COLS = Math.floor((imgWidth + TILESET_SPACING) / (TILESET_TILE_SIZE + TILESET_SPACING));
            console.log('Tileset loaded:', imgWidth, 'px wide,', TILESET_COLS, 'columns');
        }
    });
}

function create() {
    console.log('Phaser create starting');
    mainScene = this;

    // Enable pixel-perfect camera to prevent gaps between tiles
    this.cameras.main.setRoundPixels(true);

    // Pre-allocate terrain sprite pools for instant world transitions.
    // Creating sprites is expensive, but updating their properties is cheap.
    // By allocating all sprites at startup, world transitions become instant.
    overworldContainer = this.add.container(0, 0);
    overworldContainer.setDepth(0);
    overworldSprites = createTerrainPool(OVERWORLD_TOTAL * OVERWORLD_TOTAL, overworldContainer);
    console.log(`Pre-allocated ${overworldSprites.length} overworld terrain sprites`);

    instanceContainer = this.add.container(0, 0);
    instanceContainer.setDepth(0);
    instanceContainer.setVisible(false);
    instanceSprites = createTerrainPool(INSTANCE_TOTAL * INSTANCE_TOTAL, instanceContainer);
    console.log(`Pre-allocated ${instanceSprites.length} instance terrain sprites`);

    console.log('Phaser scene created and ready');

    // Resolve the init promise now that scene is ready
    if (initResolve) {
        initResolve();
        initResolve = null;
    }
}

function createTerrainPool(count, container) {
    const sprites = [];
    for (let i = 0; i < count; i++) {
        const sprite = mainScene.add.sprite(0, 0, 'tileset', 0);
        sprite.setOrigin(0, 0);
        sprite.setDisplaySize(TILE_SIZE, TILE_SIZE);
        sprite.setVisible(false);
        container.add(sprite);
        sprites.push(sprite);
    }
    return sprites;
}

// ============ COMMAND DISPATCHER ============

window.executeCommands = function(commands) {
    if (!mainScene) {
        console.warn('Scene not ready, queuing', commands.length, 'commands');
        return;
    }

    // Log all command types to see what's being sent
    const cmdTypes = commands.map(c => c.type);
    const typeCounts = {};
    cmdTypes.forEach(t => typeCounts[t] = (typeCounts[t] || 0) + 1);
    console.log('Commands:', commands.length, typeCounts);

    for (const cmd of commands) {
        try {
            executeCommand(cmd);
        } catch (e) {
            console.error(`Command ${cmd.type} failed:`, e, cmd);
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

    console.log('Creating sprite:', entityId, 'at', x, y, 'frame:', tileIndex);

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

    // Use provided depth or default to 50 (well above terrain at 0)
    const finalDepth = (depth !== undefined && depth !== null) ? depth : 50;
    sprite.setDepth(finalDepth);

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

function setTint(cmd) {
    const sprite = entities.get(cmd.entityId);
    if (sprite) {
        sprite.setTint(cmd.tint);
    }
}

function setVisible(cmd) {
    const sprite = entities.get(cmd.entityId);
    if (sprite) {
        sprite.setVisible(cmd.visible);
    }
}

function setDepth(cmd) {
    const sprite = entities.get(cmd.entityId);
    if (sprite) {
        sprite.setDepth(cmd.depth);
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
    // Phaser tweens interpolate through sub-pixel values (e.g., 100 -> 112.5 -> 125),
    // which causes visible gaps between tiles even with roundPixels enabled.
    // The onUpdate callback forces integer rounding every frame during the tween.
    mainScene.tweens.add({
        targets: mainScene.cameras.main,
        scrollX: Math.round(-cmd.x),
        scrollY: Math.round(-cmd.y),
        duration: cmd.durationMs,
        ease: cmd.easing || 'Sine.easeOut',
        onUpdate: (tween, target) => {
            target.scrollX = Math.round(target.scrollX);
            target.scrollY = Math.round(target.scrollY);
        }
    });
}

function snapCamera(cmd) {
    // Instant camera position (first frame, world transitions)
    mainScene.cameras.main.setScroll(Math.round(-cmd.x), Math.round(-cmd.y));
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
    const { tiles, width, height, ghostPadding, isInstance } = cmd;
    const totalWidth = width + 2 * ghostPadding;

    // Select the appropriate sprite pool and toggle container visibility
    const sprites = isInstance ? instanceSprites : overworldSprites;
    overworldContainer.setVisible(!isInstance);
    instanceContainer.setVisible(isInstance);

    // Update each sprite's appearance (fast - no allocation, just property changes)
    for (let i = 0; i < tiles.length; i++) {
        const sprite = sprites[i];
        const tile = tiles[i];
        const x = i % totalWidth;
        const y = Math.floor(i / totalWidth);
        const worldX = x - ghostPadding;
        const worldY = y - ghostPadding;

        const tileConfig = getTileConfig(tile.type);
        sprite.setPosition(worldX * TILE_SIZE, worldY * TILE_SIZE);
        sprite.setFrame(tileConfig.frame);
        sprite.setTint(tileConfig.tint);

        // Ghost area dimming
        const isGhost = worldX < 0 || worldX >= width || worldY < 0 || worldY >= height;
        sprite.setAlpha(isGhost ? 0.3 : 1);
        sprite.setVisible(true);
    }

    // Hide any excess sprites (if world is smaller than pool)
    for (let i = tiles.length; i < sprites.length; i++) {
        sprites[i].setVisible(false);
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

    // Update position on each frame to follow target
    mainScene.events.on('update', () => {
        if (targetReticle && cmd.entityId) {
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
    // Tile configs using (x, y) grid coordinates - matches original game.js TILE_TEXTURES
    const configs = {
        0: { x: 5, y: 0, tint: 0x228B22 },      // GrassSparse
        1: { x: 6, y: 0, tint: 0x228B22 },      // GrassMedium
        2: { x: 7, y: 0, tint: 0x228B22 },      // GrassDense
        3: { x: 8, y: 5, tint: 0xFFFFFF },      // Water
        4: { x: 6, y: 5, tint: 0xFFFFFF },      // Bridge
        5: { x: 1, y: 1, tint: 0xFFFFFF },      // TreeSparse
        6: { x: 2, y: 1, tint: 0xFFFFFF },      // TreeMedium
        7: { x: 3, y: 1, tint: 0xFFFFFF },      // TreeDense
        8: { x: 9, y: 2, tint: 0xFFD700 },      // POIMarker
        9: { x: 11, y: 0, tint: 0xFF4500 },     // ExitMarker
        10: { x: 14, y: 2, tint: 0xFFFFFF }     // TownCenter
    };
    const cfg = configs[tileType] || configs[0];
    return { frame: xyToFrame(cfg.x, cfg.y), tint: cfg.tint };
}

function updateHealthBarPosition(entityId, tileX, tileY) {
    const healthBar = healthBars.get(entityId);
    if (!healthBar) return;

    healthBar.bg.x = tileX * TILE_SIZE + 1;
    healthBar.bg.y = tileY * TILE_SIZE - 4;
    healthBar.fg.x = tileX * TILE_SIZE + 1;
    healthBar.fg.y = tileY * TILE_SIZE - 4;
}

// Interaction info helper (for compatibility with Home.razor)
window.getInteractionInfo = (snapshot, playerId) => {
    if (!snapshot) return null;

    const player = snapshot.entities?.find(e => e.id === playerId);
    if (!player) return null;

    // Check for exit marker
    if (snapshot.exitMarker &&
        player.position.x === snapshot.exitMarker.x &&
        player.position.y === snapshot.exitMarker.y) {
        return { type: 'exit', text: 'Press Enter to Exit' };
    }

    // Check for POI
    if (snapshot.poIs) {
        const poi = snapshot.poIs.find(p =>
            p.position.x === player.position.x &&
            p.position.y === player.position.y
        );
        if (poi) {
            const poiTypeName = poi.type === 0 ? 'Camp' : poi.type === 1 ? 'Town' : 'Dungeon';
            return { type: 'poi', text: `Press Enter to Enter ${poiTypeName}` };
        }
    }

    return null;
};
