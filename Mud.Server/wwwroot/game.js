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

window.renderSnapshot = (snapshot) => {
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

    snapshot.players.forEach(p => {
        if (p.queuedPath) {
            p.queuedPath.forEach((pt, index) => {
                const id = `path-${p.id}-${index}`;
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

        const id = `player-${p.id}`;
        const s = getPersistentSprite(id, playerTexture, playerLayer);
        s.x = Math.round(p.position.x * renderScale + offsetX);
        s.y = Math.round(p.position.y * renderScale + offsetY);
        s.width = renderScale;
        s.height = renderScale;
        s.tint = 0xffff00;
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
