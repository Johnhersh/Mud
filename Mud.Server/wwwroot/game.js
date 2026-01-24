let app;
let tilesetTexture;
let floor;
let wallLayer, playerLayer, uiLayer;
const tileSize = 16;
const spacing = 1;
const renderScale = 20;

const textures = {};
const spriteMap = new Map();
const pathGraphicsMap = new Map();

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
        
        wallLayer = new PIXI.Container();
        playerLayer = new PIXI.Container();
        uiLayer = new PIXI.Container();
        
        app.stage.addChild(wallLayer, playerLayer, uiLayer);

        PIXI.Assets.load('assets/colored-transparent.png').then(tex => {
            tex.source.scaleMode = 'nearest';
            tex.source.mipmap = false;
            tex.source.update();
            tilesetTexture = tex;

            const grassTexture = getTexture(5, 0);
            floor = new PIXI.TilingSprite({
                texture: grassTexture,
                width: app.screen.width,
                height: app.screen.height
            });
            floor.tileScale.set(renderScale / tileSize);
            floor.tint = 0x228B22;
            app.stage.addChildAt(floor, 0);

            console.log("Tileset loaded and layers initialized");
        });
    });
};

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

window.renderSnapshot = (snapshot, targetId) => {
    if (!app || !tilesetTexture || !floor) return;
    
    spriteMap.forEach(s => s.usedThisFrame = false);
    pathGraphicsMap.forEach(g => g.usedThisFrame = false);
    
    const offsetX = 400;
    const offsetY = 300;

    floor.tilePosition.x = Math.round(offsetX);
    floor.tilePosition.y = Math.round(offsetY);

    const treeTexture = getTexture(0, 1);
    snapshot.walls.forEach((w, index) => {
        const id = `wall-${w.x}-${w.y}`;
        const s = getPersistentSprite(id, treeTexture, wallLayer);
        s.x = Math.round(w.x * renderScale + offsetX);
        s.y = Math.round(w.y * renderScale + offsetY);
        s.width = renderScale;
        s.height = renderScale;
        s.tint = 0x2e8b57;
    });
    
    const playerTexture = getTexture(25, 0);
    const monsterTexture = getTexture(26, 2);

    snapshot.entities.forEach(e => {
        if (e.queuedPath) {
            e.queuedPath.forEach((pt, index) => {
                const id = `path-${e.id}-${index}`;
                let g = pathGraphicsMap.get(id);
                if (!g) {
                    g = new PIXI.Graphics();
                    uiLayer.addChild(g);
                    pathGraphicsMap.set(id, g);
                }
                g.clear();
                g.rect(0, 0, renderScale, renderScale);
                g.fill({ color: 0xffff00, alpha: 0.4 });
                g.x = Math.round(pt.x * renderScale + offsetX);
                g.y = Math.round(pt.y * renderScale + offsetY);
                g.visible = true;
                g.usedThisFrame = true;
            });
        }

        const id = `entity-${e.id}`;
        const texture = e.type === 0 ? playerTexture : monsterTexture; // 0: Player, 1: Monster
        const s = getPersistentSprite(id, texture, playerLayer);
        s.x = Math.round(e.position.x * renderScale + offsetX);
        s.y = Math.round(e.position.y * renderScale + offsetY);
        s.width = renderScale;
        s.height = renderScale;
        s.tint = e.type === 0 ? 0xffff00 : 0xff0000;

        // Render health bar
        const healthId = `health-${e.id}`;
        let hg = pathGraphicsMap.get(healthId);
        if (!hg) {
            hg = new PIXI.Graphics();
            uiLayer.addChild(hg);
            pathGraphicsMap.set(healthId, hg);
        }
        hg.clear();
        hg.rect(0, 0, renderScale, 2);
        hg.fill({ color: 0x000000 });
        hg.rect(0, 0, renderScale * (e.health / e.maxHealth), 2);
        hg.fill({ color: 0x00ff00 });
        hg.x = s.x;
        hg.y = s.y - 4;
        hg.visible = true;
        hg.usedThisFrame = true;

        // Render reticle if targeted
        if (e.id === targetId) {
            const reticleId = `reticle-${e.id}`;
            let rg = pathGraphicsMap.get(reticleId);
            if (!rg) {
                rg = new PIXI.Graphics();
                uiLayer.addChild(rg);
                pathGraphicsMap.set(reticleId, rg);
            }
            rg.clear();
            rg.setStrokeStyle({ width: 2, color: 0xffffff });
            rg.rect(0, 0, renderScale, renderScale);
            rg.stroke();
            rg.x = s.x;
            rg.y = s.y;
            rg.visible = true;
            rg.usedThisFrame = true;
        }
    });

    // Cleanup unused sprites
    spriteMap.forEach((s, id) => {
        if (!s.usedThisFrame) {
            s.visible = false;
            s.parent.removeChild(s);
            spriteMap.delete(id);
        }
    });

    // Cleanup unused path graphics
    pathGraphicsMap.forEach((g, id) => {
        if (!g.usedThisFrame) {
            g.visible = false;
            g.parent.removeChild(g);
            pathGraphicsMap.delete(id);
        }
    });
};
