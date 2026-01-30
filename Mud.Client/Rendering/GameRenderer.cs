using Mud.Shared;
using Mud.Shared.World;

namespace Mud.Client.Rendering;

/// <summary>
/// Owns scene graph state and produces render commands based on diffs.
/// No per-frame updates - all interpolation handled by Phaser.
/// </summary>
public class GameRenderer
{
    private readonly RenderCommandBuffer _buffer;
    private readonly Dictionary<string, EntityRenderState> _entities = new();
    private readonly HashSet<string> _staticSprites = new();  // POIs, exit markers, etc.
    private string? _currentWorldId;
    private string? _targetEntityId;
    private string? _myPlayerId;
    private bool _cameraInitialized;
    private bool _worldJustChanged;

    // Constants - match server tick interval for smooth movement
    private const int TickDurationMs = 300;
    private const int TileSize = 20;
    private const int CenterX = 400;
    private const int CenterY = 300;

    // Tile index mapping: frame = x + y * 49 (49 tiles per row in tileset)
    private static readonly Dictionary<TileType, (int frame, uint tint)> TileConfigs = new()
    {
        [TileType.GrassSparse] = (5, 0x228B22),     // (5,0) -> 5 + 0*16 = 5
        [TileType.GrassMedium] = (6, 0x228B22),     // (6,0) -> 6 + 0*16 = 6
        [TileType.GrassDense] = (7, 0x228B22),      // (7,0) -> 7 + 0*16 = 7
        [TileType.Water] = (88, 0xFFFFFF),          // (8,5) -> 8 + 5*16 = 88
        [TileType.Bridge] = (86, 0xFFFFFF),         // (6,5) -> 6 + 5*16 = 86
        [TileType.TreeSparse] = (17, 0xFFFFFF),     // (1,1) -> 1 + 1*16 = 17
        [TileType.TreeMedium] = (18, 0xFFFFFF),     // (2,1) -> 2 + 1*16 = 18
        [TileType.TreeDense] = (19, 0xFFFFFF),      // (3,1) -> 3 + 1*16 = 19
        [TileType.POIMarker] = (41, 0xFFD700),      // (9,2) -> 9 + 2*16 = 41
        [TileType.ExitMarker] = (11, 0xFF4500),     // (11,0) -> 11 + 0*16 = 11
        [TileType.TownCenter] = (46, 0xFFFFFF)      // (14,2) -> 14 + 2*16 = 46
    };

    public GameRenderer(RenderCommandBuffer buffer)
    {
        _buffer = buffer;
    }

    /// <summary>
    /// Processes a world snapshot and generates render commands.
    /// Called on server ticks (~3/second), not every frame.
    /// </summary>
    public void ProcessSnapshot(WorldSnapshot snapshot, string? targetId, string playerId)
    {
        _myPlayerId = playerId;

        // Detect world change (for camera/entity snap behavior)
        bool isInstance = snapshot.WorldType == WorldType.Instance;
        if (snapshot.WorldId != _currentWorldId)
        {
            _worldJustChanged = true;
            _currentWorldId = snapshot.WorldId;

            // Clear static sprites from old world (POIs, exit markers)
            foreach (var spriteId in _staticSprites)
            {
                _buffer.DestroySprite(spriteId);
            }
            _staticSprites.Clear();

            // Update terrain if tiles are provided (server only sends on first visit to a world)
            if (snapshot.Tiles != null && snapshot.Tiles.Count > 0)
            {
                var tileRenderData = snapshot.Tiles
                    .Select(t => new TileRenderData((int)t.Type))
                    .ToList();

                _buffer.SetTerrain(
                    snapshot.WorldId,
                    tileRenderData,
                    snapshot.Width,
                    snapshot.Height,
                    snapshot.GhostPadding,
                    isInstance
                );
            }
            else
            {
                // No tiles sent (already cached) - just switch which terrain layer is visible
                _buffer.SwitchTerrainLayer(isInstance);
            }
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
                // Tileset has 49 columns. Frame = y * 49 + x
                // Player: (25,0) = 0*49 + 25 = 25
                // Monster: (26,2) = 2*49 + 26 = 124
                var tileIndex = entity.Type == EntityType.Player ? 25 : 124;
                var tint = entity.Type == EntityType.Player ? 0xFFFF00u : 0xFF0000u;
                var depth = 50;  // Well above terrain (depth 0)

                _buffer.CreateSprite(entity.Id, tileIndex, entity.Position.X, entity.Position.Y, tint, depth);
                _buffer.CreateHealthBar(entity.Id, entity.MaxHealth, entity.Health);

                state = new EntityRenderState(entity);
                _entities[entity.Id] = state;
            }
            else
            {
                // Existing entity - check for changes
                if (entity.Position != state.Position)
                {
                    if (_worldJustChanged)
                    {
                        // World transition: snap to new position instantly
                        _buffer.SetPosition(entity.Id, entity.Position.X, entity.Position.Y);
                    }
                    else
                    {
                        // Normal movement: smooth tween
                        _buffer.TweenTo(entity.Id, entity.Position.X, entity.Position.Y, TickDurationMs);
                    }
                }

                if (entity.Health != state.Health)
                {
                    _buffer.UpdateHealthBar(entity.Id, entity.Health);
                }

                state.Update(entity);
            }

            // Update queued path
            var pathPoints = entity.QueuedPath
                .Select(p => new PathPoint(p.X, p.Y))
                .ToList();
            _buffer.SetQueuedPath(entity.Id, pathPoints);
        }

        // Process attack events for animations
        foreach (var attack in snapshot.AttackEvents)
        {
            if (attack.IsMelee) _buffer.BumpAttack(attack.AttackerId, attack.TargetId);

            _buffer.FloatingDamage(attack.TargetPosition.X, attack.TargetPosition.Y, attack.Damage);
        }

        // Camera follows player - send target, Phaser tweens to it
        var player = snapshot.Entities.FirstOrDefault(e => e.Id == playerId);
        if (player != null)
        {
            var camX = CenterX - (player.Position.X * TileSize);
            var camY = CenterY - (player.Position.Y * TileSize);

            if (!_cameraInitialized || _worldJustChanged)
            {
                // First frame or world transition: snap camera instantly
                _buffer.SnapCamera(camX, camY);
                _cameraInitialized = true;
                _worldJustChanged = false;
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

        // POIs (rendered as terrain sprites with specific tiles)
        if (snapshot.POIs != null && snapshot.POIs.Count > 0)
        {
            ProcessPOIs(snapshot.POIs);
        }

        // Exit marker
        if (snapshot.ExitMarker != null)
        {
            ProcessExitMarker(snapshot.ExitMarker);
        }
    }

    private void ProcessPOIs(List<POI> pois)
    {
        foreach (var poi in pois)
        {
            var spriteId = $"poi-{poi.Id}";

            if (_staticSprites.Contains(spriteId))
                continue;

            var tileConfig = poi.Type == POIType.Town
                ? TileConfigs[TileType.TownCenter]
                : TileConfigs[TileType.POIMarker];

            _buffer.CreateSprite(spriteId, tileConfig.frame, poi.Position.X, poi.Position.Y, tileConfig.tint, 5);
            _staticSprites.Add(spriteId);
        }
    }

    private void ProcessExitMarker(Point exitMarker)
    {
        var spriteId = "exit-marker";
        var tileConfig = TileConfigs[TileType.ExitMarker];

        if (!_staticSprites.Contains(spriteId))
        {
            _buffer.CreateSprite(spriteId, tileConfig.frame, exitMarker.X, exitMarker.Y, tileConfig.tint, 5);
            _staticSprites.Add(spriteId);
        }
    }

    /// <summary>
    /// Flushes all pending commands to JavaScript.
    /// </summary>
    public async ValueTask FlushAsync() => await _buffer.FlushAsync();
}
