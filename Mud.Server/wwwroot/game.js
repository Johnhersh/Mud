let app;
let tilesetTexture;
let floor;
let terrainLayer, wallLayer, playerLayer, uiLayer;
const tileSize = 16;
const spacing = 1;
const renderScale = 20;

// Camera state - smooth interpolation between server ticks
let cameraX = 400;
let cameraY = 300;
let targetCameraX = 400;
let targetCameraY = 300;
let cameraInitialized = false;
let worldContainer;
let lastFrameTime = 0;

// Loading state
let isInitialized = false;
let pendingSnapshot = null;
let pendingTargetId = null;
let pendingPlayerId = null;

// Tile caching - tiles only need to be sent once per world
let cachedWorldId = null;
let cachedTiles = null;
let cachedGhostPadding = 0;
let cachedWidth = 0;
let cachedHeight = 0;
let cachedTotalWidth = 0;
let cachedTotalHeight = 0;

const textures = {};
const spriteMap = new Map();
const pathGraphicsMap = new Map();
const terrainSpriteMap = new Map();

// TileType enum values from server
const TileType = {
    GrassSparse: 0,
    GrassMedium: 1,
    GrassDense: 2,
    Water: 3,
    TreeSparse: 4,
    TreeMedium: 5,
    TreeDense: 6,
    POIMarker: 7,
    ExitMarker: 8,
    TownCenter: 9
};

// Tile textures mapping (tileset grid coordinates)
const TILE_TEXTURES = {
    [TileType.GrassSparse]: { x: 5, y: 0, tint: 0x228B22 },   // Sparse grass (♣)
    [TileType.GrassMedium]: { x: 6, y: 0, tint: 0x228B22 },   // Medium grass (♠)
    [TileType.GrassDense]: { x: 7, y: 0, tint: 0x228B22 },    // Dense grass (•)
    [TileType.Water]: { x: 8, y: 5, tint: 0xFFFFFF },         // Water - light blue block
    [TileType.TreeSparse]: { x: 1, y: 1, tint: 0xFFFFFF },    // Single tree (2nd slot)
    [TileType.TreeMedium]: { x: 2, y: 1, tint: 0xFFFFFF },    // Medium tree (3rd slot)
    [TileType.TreeDense]: { x: 3, y: 1, tint: 0xFFFFFF },     // Double tree (4th slot)
    [TileType.POIMarker]: { x: 9, y: 2, tint: 0xFFD700 },     // POI - gold
    [TileType.ExitMarker]: { x: 11, y: 0, tint: 0xFF4500 },   // Exit - orange red
    [TileType.TownCenter]: { x: 14, y: 2, tint: 0xFFFFFF }    // Town - white
};

function getTexture(x, y) {
    const key = `${x},${y}`;
    if (textures[key]) return textures[key];

    const rect = new PIXI.Rectangle(
        x * (tileSize + spacing),
        y * (tileSize + spacing),
        tileSize,
        tileSize
    );
    const tex = new PIXI.Texture({
        source: tilesetTexture.source,
        frame: rect
    });
    textures[key] = tex;
    return tex;
}

window.initPixi = (containerId) => {
    const container = document.getElementById(containerId);
    app = new PIXI.Application();

    app.init({
        width: 800,
        height: 600,
        backgroundColor: 0x000000,
        resizeTo: container,
        antialias: false,
        roundPixels: true
    }).then(() => {
        container.appendChild(app.canvas);

        // Create world container that will be moved for camera
        worldContainer = new PIXI.Container();

        terrainLayer = new PIXI.Container();
        wallLayer = new PIXI.Container();
        playerLayer = new PIXI.Container();
        uiLayer = new PIXI.Container();

        worldContainer.addChild(terrainLayer, wallLayer, playerLayer);
        app.stage.addChild(worldContainer, uiLayer);

        PIXI.Assets.load('assets/colored-transparent.png').then(tex => {
            tex.source.scaleMode = 'nearest';
            tex.source.mipmap = false;
            tex.source.update();
            tilesetTexture = tex;

            // Create a simple black background
            floor = new PIXI.Graphics();
            floor.rect(0, 0, app.screen.width, app.screen.height);
            floor.fill({ color: 0x111111 });
            app.stage.addChildAt(floor, 0);

            isInitialized = true;
            console.log("Tileset loaded and layers initialized");

            // Start smooth camera update loop (runs every frame, not just on server ticks)
            app.ticker.add(updateCamera);

            // Process any pending snapshot that arrived before initialization
            if (pendingSnapshot) {
                console.log("Processing pending snapshot");
                doRenderSnapshot(pendingSnapshot, pendingTargetId, pendingPlayerId);
                pendingSnapshot = null;
            }
        }).catch(err => {
            console.error("Failed to load tileset:", err);
        });
    }).catch(err => {
        console.error("Failed to initialize Pixi:", err);
    });
};

// Smooth camera interpolation - runs every frame for smooth movement
function updateCamera(ticker) {
    if (!worldContainer) return;

    // Lerp factor - higher = faster, lower = smoother
    // Using delta time for frame-rate independent smoothing
    const lerpSpeed = 8; // units per second
    const t = Math.min(1, lerpSpeed * ticker.deltaTime / 60);

    // Smoothly interpolate camera toward target
    cameraX = cameraX + (targetCameraX - cameraX) * t;
    cameraY = cameraY + (targetCameraY - cameraY) * t;

    // Apply camera position to world container
    worldContainer.x = Math.round(cameraX);
    worldContainer.y = Math.round(cameraY);
}

function getPersistentSprite(id, texture, container) {
    let sprite = spriteMap.get(id);
    if (!sprite) {
        sprite = new PIXI.Sprite(texture);
        container.addChild(sprite);
        spriteMap.set(id, sprite);
    }
    sprite.texture = texture;
    sprite.visible = true;
    sprite.usedThisFrame = true;
    return sprite;
}

function getTerrainSprite(id, texture, container) {
    let sprite = terrainSpriteMap.get(id);
    if (!sprite) {
        sprite = new PIXI.Sprite(texture);
        container.addChild(sprite);
        terrainSpriteMap.set(id, sprite);
    }
    sprite.texture = texture;
    sprite.visible = true;
    sprite.usedThisFrame = true;
    return sprite;
}

window.renderSnapshot = (snapshot, targetId, playerId) => {
    if (!isInitialized) {
        // Buffer the snapshot until initialization completes
        // Prefer keeping a snapshot with tiles over one without
        if (!pendingSnapshot || (snapshot.tiles && !pendingSnapshot.tiles)) {
            pendingSnapshot = snapshot;
            pendingTargetId = targetId;
            pendingPlayerId = playerId;
        }
        return;
    }
    doRenderSnapshot(snapshot, targetId, playerId);
};

function doRenderSnapshot(snapshot, targetId, playerId) {
    if (!app || !tilesetTexture) return;

    spriteMap.forEach(s => s.usedThisFrame = false);
    pathGraphicsMap.forEach(g => g.usedThisFrame = false);

    // Find player entity for camera targeting
    const player = snapshot.entities?.find(e => e.id === playerId);
    if (player) {
        // Set target camera position (smooth interpolation happens in updateCamera)
        targetCameraX = 400 - (player.position.x * renderScale);
        targetCameraY = 300 - (player.position.y * renderScale);

        // On first frame, snap camera directly to player (no interpolation)
        if (!cameraInitialized) {
            cameraX = targetCameraX;
            cameraY = targetCameraY;
            cameraInitialized = true;
        }
    }

    // Camera position is applied in updateCamera() which runs every frame

    // Cache tiles when world changes (tiles are static, no need to resend every tick)
    // Tiles are now a flat array in row-major order: index = y * totalWidth + x
    const totalWidth = (snapshot.width || 0) + 2 * (snapshot.ghostPadding || 0);
    const totalHeight = (snapshot.height || 0) + 2 * (snapshot.ghostPadding || 0);
    if (snapshot.worldId !== cachedWorldId && snapshot.tiles && snapshot.tiles.length > 0) {
        console.log("Caching tiles for world:", snapshot.worldId);
        // Debug: count tile types
        const typeCounts = {};
        snapshot.tiles.forEach(t => {
            typeCounts[t.type] = (typeCounts[t.type] || 0) + 1;
        });
        console.log("Tile type distribution:", typeCounts);
        cachedWorldId = snapshot.worldId;
        cachedTiles = snapshot.tiles;
        cachedGhostPadding = snapshot.ghostPadding || 0;
        cachedWidth = snapshot.width;
        cachedHeight = snapshot.height;
        cachedTotalWidth = totalWidth;
        cachedTotalHeight = totalHeight;
        // Clear terrain sprites for new world
        terrainSpriteMap.forEach((s, id) => {
            if (s.parent) s.parent.removeChild(s);
        });
        terrainSpriteMap.clear();
    }

    // Render terrain from cache
    terrainSpriteMap.forEach(s => s.usedThisFrame = false);
    if (cachedTiles) {
        renderTerrain();
    }

    // Render POIs
    if (snapshot.poIs) {
        renderPOIs(snapshot.poIs);
    }

    // Render exit marker
    if (snapshot.exitMarker) {
        renderExitMarker(snapshot.exitMarker, snapshot.ghostPadding || 0);
    }

    // Render entities
    const playerTexture = getTexture(25, 0);
    const monsterTexture = getTexture(26, 2);

    if (snapshot.entities) {
        snapshot.entities.forEach(e => {
            if (e.queuedPath) {
                e.queuedPath.forEach((pt, index) => {
                    const id = `path-${e.id}-${index}`;
                    let g = pathGraphicsMap.get(id);
                    if (!g) {
                        g = new PIXI.Graphics();
                        playerLayer.addChild(g);
                        pathGraphicsMap.set(id, g);
                    }
                    g.clear();
                    g.rect(0, 0, renderScale, renderScale);
                    g.fill({ color: 0xffff00, alpha: 0.4 });
                    g.x = Math.round(pt.x * renderScale);
                    g.y = Math.round(pt.y * renderScale);
                    g.visible = true;
                    g.usedThisFrame = true;
                });
            }

            const id = `entity-${e.id}`;
            const texture = e.type === 0 ? playerTexture : monsterTexture;
            const s = getPersistentSprite(id, texture, playerLayer);
            s.x = Math.round(e.position.x * renderScale);
            s.y = Math.round(e.position.y * renderScale);
            s.width = renderScale;
            s.height = renderScale;
            s.tint = e.type === 0 ? 0xffff00 : 0xff0000;

            // Render health bar (in playerLayer so it moves with world/camera)
            const healthId = `health-${e.id}`;
            let hg = pathGraphicsMap.get(healthId);
            if (!hg) {
                hg = new PIXI.Graphics();
                playerLayer.addChild(hg);
                pathGraphicsMap.set(healthId, hg);
            }
            hg.clear();
            hg.rect(0, 0, renderScale, 2);
            hg.fill({ color: 0x000000 });
            hg.rect(0, 0, renderScale * (e.health / e.maxHealth), 2);
            hg.fill({ color: 0x00ff00 });
            // Use world coordinates (playerLayer is inside worldContainer which handles camera)
            hg.x = Math.round(e.position.x * renderScale);
            hg.y = Math.round(e.position.y * renderScale - 4);
            hg.visible = true;
            hg.usedThisFrame = true;

            // Render reticle if targeted (in playerLayer so it moves with world/camera)
            if (e.id === targetId) {
                const reticleId = `reticle-${e.id}`;
                let rg = pathGraphicsMap.get(reticleId);
                if (!rg) {
                    rg = new PIXI.Graphics();
                    playerLayer.addChild(rg);
                    pathGraphicsMap.set(reticleId, rg);
                }
                rg.clear();
                rg.setStrokeStyle({ width: 2, color: 0xffffff });
                rg.rect(0, 0, renderScale, renderScale);
                rg.stroke();
                // Use world coordinates
                rg.x = Math.round(e.position.x * renderScale);
                rg.y = Math.round(e.position.y * renderScale);
                rg.visible = true;
                rg.usedThisFrame = true;
            }
        });
    }

    // Cleanup unused sprites
    spriteMap.forEach((s, id) => {
        if (!s.usedThisFrame) {
            s.visible = false;
            if (s.parent) s.parent.removeChild(s);
            spriteMap.delete(id);
        }
    });

    // Cleanup unused path graphics
    pathGraphicsMap.forEach((g, id) => {
        if (!g.usedThisFrame) {
            g.visible = false;
            if (g.parent) g.parent.removeChild(g);
            pathGraphicsMap.delete(id);
        }
    });

    // Cleanup unused terrain sprites
    terrainSpriteMap.forEach((s, id) => {
        if (!s.usedThisFrame) {
            s.visible = false;
            if (s.parent) s.parent.removeChild(s);
            terrainSpriteMap.delete(id);
        }
    });
};

function renderTerrain() {
    // Use cached tile data (flat array in row-major order: index = y * totalWidth + x)
    const tiles = cachedTiles;
    const ghostPadding = cachedGhostPadding;
    const width = cachedWidth;
    const height = cachedHeight;
    const totalWidth = cachedTotalWidth;
    const totalHeight = cachedTotalHeight;

    if (!tiles || tiles.length === 0) return;

    // Calculate viewport bounds (with buffer for smooth scrolling)
    const viewLeft = Math.floor(-cameraX / renderScale) - 2;
    const viewTop = Math.floor(-cameraY / renderScale) - 2;
    const viewRight = Math.ceil((app.screen.width - cameraX) / renderScale) + 2;
    const viewBottom = Math.ceil((app.screen.height - cameraY) / renderScale) + 2;

    // Render visible tiles only
    for (let x = Math.max(0, viewLeft + ghostPadding); x < Math.min(totalWidth, viewRight + ghostPadding); x++) {
        for (let y = Math.max(0, viewTop + ghostPadding); y < Math.min(totalHeight, viewBottom + ghostPadding); y++) {
            // Flat array index: y * totalWidth + x (row-major order)
            const index = y * totalWidth + x;
            const tile = tiles[index];
            if (!tile) continue;

            const worldX = x - ghostPadding;
            const worldY = y - ghostPadding;
            const isGhostArea = worldX < 0 || worldX >= width || worldY < 0 || worldY >= height;

            const tileConfig = TILE_TEXTURES[tile.type] || TILE_TEXTURES[TileType.GrassSparse];
            const texture = getTexture(tileConfig.x, tileConfig.y);

            const id = `terrain-${x}-${y}`;
            const s = getTerrainSprite(id, texture, terrainLayer);
            s.x = Math.round(worldX * renderScale);
            s.y = Math.round(worldY * renderScale);
            s.width = renderScale;
            s.height = renderScale;
            s.tint = tileConfig.tint;

            // Dim ghost area tiles
            if (isGhostArea) {
                s.alpha = 0.3;
            } else {
                s.alpha = 1.0;
            }
        }
    }
}

function renderPOIs(pois) {
    pois.forEach(poi => {
        const tileConfig = poi.type === 1 ? TILE_TEXTURES[TileType.TownCenter] : TILE_TEXTURES[TileType.POIMarker];
        const texture = getTexture(tileConfig.x, tileConfig.y);

        const id = `poi-${poi.id}`;
        const s = getTerrainSprite(id, texture, wallLayer);
        s.x = Math.round(poi.position.x * renderScale);
        s.y = Math.round(poi.position.y * renderScale);
        s.width = renderScale;
        s.height = renderScale;
        s.tint = tileConfig.tint;
        s.alpha = 1.0;
    });
}

function renderExitMarker(exitMarker, ghostPadding) {
    const tileConfig = TILE_TEXTURES[TileType.ExitMarker];
    const texture = getTexture(tileConfig.x, tileConfig.y);

    const id = `exit-marker`;
    const s = getTerrainSprite(id, texture, wallLayer);
    s.x = Math.round(exitMarker.x * renderScale);
    s.y = Math.round(exitMarker.y * renderScale);
    s.width = renderScale;
    s.height = renderScale;
    s.tint = tileConfig.tint;
    s.alpha = 1.0;
}

// Return current interaction info based on player position
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
