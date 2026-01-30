// phaser-renderer.js - Thin command executor for Phaser 4

// ============ COMMAND TYPE DEFINITIONS ============
// IMPORTANT: These types mirror RenderCommandType enum and command records in
// Mud.Client/Rendering/RenderCommand.cs. Any changes there must be reflected here.

/**
 * @typedef {'CreateSprite'|'DestroySprite'|'SetPosition'|'SetTint'|'SetVisible'|'SetDepth'|'TweenTo'|'BumpAttack'|'FloatingDamage'|'TweenCamera'|'SnapCamera'|'CreateHealthBar'|'UpdateHealthBar'|'SetTerrain'|'SwitchTerrainLayer'|'SetQueuedPath'|'SetTargetReticle'} RenderCommandType
 */

/** @typedef {{ type: 'CreateSprite', entityId: string, tileIndex: number, x: number, y: number, tint?: number, depth?: number }} CreateSpriteCmd */
/** @typedef {{ type: 'DestroySprite', entityId: string }} DestroySpriteCmd */
/** @typedef {{ type: 'SetPosition', entityId: string, x: number, y: number }} SetPositionCmd */
/** @typedef {{ type: 'SetTint', entityId: string, tint: number }} SetTintCmd */
/** @typedef {{ type: 'SetVisible', entityId: string, visible: boolean }} SetVisibleCmd */
/** @typedef {{ type: 'SetDepth', entityId: string, depth: number }} SetDepthCmd */
/** @typedef {{ type: 'TweenTo', entityId: string, x: number, y: number, durationMs?: number, easing?: string }} TweenToCmd */
/** @typedef {{ type: 'BumpAttack', attackerId: string, targetId: string, durationMs?: number }} BumpAttackCmd */
/** @typedef {{ type: 'FloatingDamage', x: number, y: number, damage: number, durationMs?: number }} FloatingDamageCmd */
/** @typedef {{ type: 'TweenCamera', x: number, y: number, durationMs: number, easing?: string }} TweenCameraCmd */
/** @typedef {{ type: 'SnapCamera', x: number, y: number }} SnapCameraCmd */
/** @typedef {{ type: 'CreateHealthBar', entityId: string, maxHealth: number, currentHealth: number }} CreateHealthBarCmd */
/** @typedef {{ type: 'UpdateHealthBar', entityId: string, currentHealth: number }} UpdateHealthBarCmd */
/** @typedef {{ type: 'SetTerrain', worldId: string, tiles: {type: number}[], width: number, height: number, ghostPadding: number, isInstance: boolean }} SetTerrainCmd */
/** @typedef {{ type: 'SwitchTerrainLayer', isInstance: boolean }} SwitchTerrainLayerCmd */
/** @typedef {{ type: 'SetQueuedPath', entityId: string, path: {x: number, y: number}[] }} SetQueuedPathCmd */
/** @typedef {{ type: 'SetTargetReticle', entityId: string|null }} SetTargetReticleCmd */

/** @typedef {CreateSpriteCmd|DestroySpriteCmd|SetPositionCmd|SetTintCmd|SetVisibleCmd|SetDepthCmd|TweenToCmd|BumpAttackCmd|FloatingDamageCmd|TweenCameraCmd|SnapCameraCmd|CreateHealthBarCmd|UpdateHealthBarCmd|SetTerrainCmd|SwitchTerrainLayerCmd|SetQueuedPathCmd|SetTargetReticleCmd} RenderCommand */

let game = null;
let mainScene = null;
const entities = new Map();
const healthBars = new Map();
const queuedPaths = new Map();
let targetReticle = null;

// Terrain sprite pools pre-allocated at startup for instant world transitions
let overworldContainer = null;
let instanceContainer = null;
let overworldSprites = [];
let instanceSprites = [];

// Must match server WorldConfig
const OVERWORLD_TOTAL = 190;
const INSTANCE_TOTAL = 60;
const TILE_SIZE = 20;
const TILESET_TILE_SIZE = 16;
const TILESET_SPACING = 1;

let TILESET_COLS = 49;

function xyToFrame(x, y) {
    return y * TILESET_COLS + x;
}

// ============ INITIALIZATION ============

let initResolve = null;

window.initPhaser = async function(containerId) {
    const container = document.getElementById(containerId);
    if (!container) {
        console.error('Container not found:', containerId);
        return;
    }

    const config = {
        type: Phaser.AUTO,
        width: 800,
        height: 600,
        parent: containerId,
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
    });
};

function preload() {
    this.load.spritesheet('tileset', 'assets/colored-transparent.png', {
        frameWidth: TILESET_TILE_SIZE,
        frameHeight: TILESET_TILE_SIZE,
        spacing: TILESET_SPACING,
        margin: 0
    });

    this.load.on('filecomplete-spritesheet-tileset', (key, type, texture) => {
        const tex = this.textures.get('tileset');
        if (tex && tex.source && tex.source[0]) {
            const imgWidth = tex.source[0].width;
            TILESET_COLS = Math.floor((imgWidth + TILESET_SPACING) / (TILESET_TILE_SIZE + TILESET_SPACING));
        }
    });
}

function create() {
    mainScene = this;
    this.cameras.main.setRoundPixels(true);

    overworldContainer = this.add.container(0, 0);
    overworldContainer.setDepth(0);
    overworldSprites = createTerrainPool(OVERWORLD_TOTAL * OVERWORLD_TOTAL, overworldContainer);

    instanceContainer = this.add.container(0, 0);
    instanceContainer.setDepth(0);
    instanceContainer.setVisible(false);
    instanceSprites = createTerrainPool(INSTANCE_TOTAL * INSTANCE_TOTAL, instanceContainer);

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

/**
 * @param {RenderCommand[]} commands
 */
window.executeCommands = function(commands) {
    if (!mainScene) {
        console.warn('Scene not ready, dropping', commands.length, 'commands');
        return;
    }

    for (const cmd of commands) {
        try {
            executeCommand(cmd);
        } catch (e) {
            console.error(`Command ${cmd.type} failed:`, e, cmd);
        }
    }
};

/**
 * @param {RenderCommand} cmd
 */
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
        case 'FloatingDamage': return floatingDamage(cmd);
        case 'TweenCamera': return tweenCamera(cmd);
        case 'SnapCamera': return snapCamera(cmd);
        case 'CreateHealthBar': return createHealthBar(cmd);
        case 'UpdateHealthBar': return updateHealthBar(cmd);
        case 'SetTerrain': return setTerrain(cmd);
        case 'SwitchTerrainLayer': return switchTerrainLayer(cmd);
        case 'SetQueuedPath': return setQueuedPath(cmd);
        case 'SetTargetReticle': return setTargetReticle(cmd);
        default:
            console.warn(`Unknown command: ${cmd.type}`);
    }
}

// ============ COMMAND IMPLEMENTATIONS ============

/** @param {CreateSpriteCmd} cmd */
function createSprite(cmd) {
    const { entityId, tileIndex, x, y, tint, depth } = cmd;

    if (entities.has(entityId)) return;

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

    const finalDepth = (depth !== undefined && depth !== null) ? depth : 50;
    sprite.setDepth(finalDepth);

    entities.set(entityId, sprite);
}

/** @param {DestroySpriteCmd} cmd */
function destroySprite(cmd) {
    const sprite = entities.get(cmd.entityId);
    if (sprite) {
        sprite.destroy();
        entities.delete(cmd.entityId);
    }

    const healthBar = healthBars.get(cmd.entityId);
    if (healthBar) {
        healthBar.bg.destroy();
        healthBar.fg.destroy();
        healthBars.delete(cmd.entityId);
    }

    const paths = queuedPaths.get(cmd.entityId);
    if (paths) {
        paths.forEach(g => g.destroy());
        queuedPaths.delete(cmd.entityId);
    }
}

function setPosition(cmd) {
    const sprite = entities.get(cmd.entityId);
    if (!sprite) return;

    // Kill active tweens so they don't override snap position
    mainScene.tweens.killTweensOf(sprite);

    sprite.x = cmd.x * TILE_SIZE;
    sprite.y = cmd.y * TILE_SIZE;

    const healthBar = healthBars.get(cmd.entityId);
    if (healthBar) {
        mainScene.tweens.killTweensOf(healthBar.bg);
        mainScene.tweens.killTweensOf(healthBar.fg);
    }
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

/** @param {BumpAttackCmd} cmd */
function bumpAttack(cmd) {
    const attacker = entities.get(cmd.attackerId);
    const target = entities.get(cmd.targetId);
    if (!attacker) return;

    // Use target position if available, otherwise attacker stays in place
    const targetX = target ? target.x : attacker.x;
    const targetY = target ? target.y : attacker.y;

    const startX = attacker.x;
    const startY = attacker.y;
    // Move 80% toward target (near tile edge) for satisfying "bounce" feel
    const bumpX = startX + (targetX - startX) * 0.8;
    const bumpY = startY + (targetY - startY) * 0.8;
    const duration = cmd.durationMs || 150;

    mainScene.tweens.chain({
        targets: attacker,
        tweens: [
            { x: bumpX, y: bumpY, duration: duration * 0.25, ease: 'Power2.easeOut' },
            { x: startX, y: startY, duration: duration * 0.75, ease: 'Elastic.easeOut' }
        ]
    });
}

/** @param {FloatingDamageCmd} cmd */
function floatingDamage(cmd) {
    const { x, y, damage, durationMs } = cmd;
    const duration = durationMs || 1000;

    // Create text at tile center, anchored at top
    const text = mainScene.add.text(
        x * TILE_SIZE + TILE_SIZE / 2,
        y * TILE_SIZE,
        `-${damage}`,
        {
            fontSize: '12px',
            fontFamily: 'monospace',
            color: '#ff0000',
            stroke: '#000000',
            strokeThickness: 2
        }
    );
    text.setOrigin(0.5, 0);
    text.setDepth(200);

    // Float downward and fade out (upward reserved for heals)
    mainScene.tweens.add({
        targets: text,
        y: text.y + 20,
        alpha: 0,
        duration: duration,
        ease: 'Power1.easeOut',
        onComplete: () => text.destroy()
    });
}

function tweenCamera(cmd) {
    // Force integer rounding every frame to prevent sub-pixel gaps between tiles
    mainScene.tweens.add({
        targets: mainScene.cameras.main,
        scrollX: Math.round(-cmd.x),
        scrollY: Math.round(-cmd.y),
        duration: cmd.durationMs,
        ease: cmd.easing || 'Sine.easeOut',
        onUpdate: (_tween, target) => {
            target.scrollX = Math.round(target.scrollX);
            target.scrollY = Math.round(target.scrollY);
        }
    });
}

function snapCamera(cmd) {
    // Kill active tweens so they don't override snap position
    mainScene.tweens.killTweensOf(mainScene.cameras.main);
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

    let color = 0x00ff00;
    if (percent <= 0.3) color = 0xff0000;
    else if (percent <= 0.6) color = 0xffff00;

    healthBar.fg.setFillStyle(color);
}

function setTerrain(cmd) {
    const { tiles, width, height, ghostPadding, isInstance } = cmd;
    const totalWidth = width + 2 * ghostPadding;

    const sprites = isInstance ? instanceSprites : overworldSprites;
    overworldContainer.setVisible(!isInstance);
    instanceContainer.setVisible(isInstance);

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

        const isGhost = worldX < 0 || worldX >= width || worldY < 0 || worldY >= height;
        sprite.setAlpha(isGhost ? 0.3 : 1);
        sprite.setVisible(true);
    }

    for (let i = tiles.length; i < sprites.length; i++) {
        sprites[i].setVisible(false);
    }
}

function switchTerrainLayer(cmd) {
    overworldContainer.setVisible(!cmd.isInstance);
    instanceContainer.setVisible(cmd.isInstance);
}

function setQueuedPath(cmd) {
    const { entityId, path } = cmd;

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

window.getInteractionInfo = (snapshot, playerId) => {
    if (!snapshot) return null;

    const player = snapshot.entities?.find(e => e.id === playerId);
    if (!player) return null;

    if (snapshot.exitMarker &&
        player.position.x === snapshot.exitMarker.x &&
        player.position.y === snapshot.exitMarker.y) {
        return { type: 'exit', text: 'Press Enter to Exit' };
    }

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
