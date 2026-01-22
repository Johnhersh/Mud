let app;
let graphics;
const tileSize = 16;

window.initPixi = (containerId) => {
    const container = document.getElementById(containerId);
    app = new PIXI.Application();
    
    app.init({ 
        width: 800, 
        height: 600, 
        backgroundColor: 0x1099bb,
        resizeTo: container
    }).then(() => {
        container.appendChild(app.canvas);
        graphics = new PIXI.Graphics();
        app.stage.addChild(graphics);
    });
};

window.renderSnapshot = (snapshot) => {
    if (!graphics) return;
    
    graphics.clear();
    
    // Draw walls
    snapshot.walls.forEach(w => {
        graphics.rect(w.x * tileSize + 400, w.y * tileSize + 300, tileSize, tileSize);
        graphics.fill(0x808080);
    });
    
    // Draw players
    snapshot.players.forEach(p => {
        // Draw queued path
        if (p.queuedPath) {
            p.queuedPath.forEach(pt => {
                graphics.rect(pt.x * tileSize + 400, pt.y * tileSize + 300, tileSize, tileSize);
                graphics.fill({ color: 0xffff00, alpha: 0.3 });
            });
        }

        graphics.rect(p.position.x * tileSize + 400, p.position.y * tileSize + 300, tileSize, tileSize);
        graphics.fill(0xffff00);
    });
};
