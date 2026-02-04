using Mud.Core;
using Mud.Core.World;

namespace Mud.Client.Rendering;

/// <summary>
/// Owns scene graph state and produces render commands based on diffs.
/// No per-frame updates - all interpolation handled by Phaser.
/// </summary>
public class GameRenderer
{
    private readonly RenderCommandBuffer _buffer;
    private readonly Dictionary<string, EntityRenderState> _entities = new();
    private readonly HashSet<string> _staticSprites = new();
    private string? _currentWorldId;
    private bool _cameraInitialized;

    private const int TickDurationMs = 300;
    private const int TileSize = 20;
    private const int CenterX = 400;
    private const int CenterY = 300;

    private static readonly Dictionary<TileType, (int frame, uint tint)> TileConfigs = new()
    {
        [TileType.GrassSparse] = (5, 0x228B22),
        [TileType.GrassMedium] = (6, 0x228B22),
        [TileType.GrassDense] = (7, 0x228B22),
        [TileType.Water] = (88, 0xFFFFFF),
        [TileType.Bridge] = (86, 0xFFFFFF),
        [TileType.TreeSparse] = (17, 0xFFFFFF),
        [TileType.TreeMedium] = (18, 0xFFFFFF),
        [TileType.TreeDense] = (19, 0xFFFFFF),
        [TileType.POIMarker] = (41, 0xFFD700),
        [TileType.ExitMarker] = (11, 0xFF4500),
        [TileType.TownCenter] = (46, 0xFFFFFF)
    };

    public GameRenderer(RenderCommandBuffer buffer) => _buffer = buffer;

    /// <summary>
    /// Processes a world snapshot, generates render commands, and flushes to JS.
    /// </summary>
    public async ValueTask ProcessSnapshot(WorldSnapshot snapshot, string playerId)
    {
        bool worldChanged = ProcessWorldChange(snapshot);
        ProcessEntities(snapshot.Entities, worldChanged);
        ProcessAttackEvents(snapshot.AttackEvents);
        ProcessLevelUpEvents(snapshot.LevelUpEvents);
        ProcessCamera(snapshot.Entities, playerId, worldChanged);
        ProcessPOIs(snapshot.POIs);
        ProcessExitMarker(snapshot.ExitMarker);

        await _buffer.FlushToGameJS();
    }

    /// <summary>
    /// Process XP gain events (called separately from Home.razor's OnXpGain handler).
    /// </summary>
    public async ValueTask ProcessXpEvents(List<XpGainEvent> xpEvents)
    {
        foreach (var xpEvent in xpEvents)
        {
            _buffer.FloatingXp(xpEvent.Position.X, xpEvent.Position.Y, xpEvent.Amount);
        }

        await _buffer.FlushToGameJS();
    }

    private bool ProcessWorldChange(WorldSnapshot snapshot)
    {
        if (snapshot.WorldId == _currentWorldId) return false;

        _currentWorldId = snapshot.WorldId;

        // Clear static sprites from old world
        foreach (var spriteId in _staticSprites) _buffer.DestroySprite(spriteId);
        _staticSprites.Clear();

        // Update terrain
        bool isInstance = snapshot.WorldType == WorldType.Instance;
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
        else _buffer.SwitchTerrainLayer(isInstance);

        return true;
    }

    private void ProcessEntities(List<Entity> entities, bool worldChanged)
    {
        var currentEntityIds = entities.Select(e => e.Id).ToHashSet();

        // Remove entities no longer present
        foreach (var id in _entities.Keys.Except(currentEntityIds).ToList())
        {
            _buffer.DestroySprite(id);
            _buffer.DestroySprite($"{id}_health");
            _entities.Remove(id);
        }

        // Create or update entities
        foreach (var entity in entities)
        {
            if (!_entities.TryGetValue(entity.Id, out var state)) CreateEntity(entity);
            else UpdateEntity(entity, state, worldChanged);

            // Update queued path
            var pathPoints = entity.QueuedPath
                .Select(p => new PathPoint(p.X, p.Y))
                .ToList();
            _buffer.SetQueuedPath(entity.Id, pathPoints);
        }
    }

    private void CreateEntity(Entity entity)
    {
        var tileIndex = entity.Type == EntityType.Player ? 25 : 124;
        var tint = entity.Type == EntityType.Player ? 0xFFFF00u : 0xFF0000u;

        _buffer.CreateSprite(entity.Id, tileIndex, entity.Position.X, entity.Position.Y, tint, depth: 50);
        _buffer.CreateHealthBar(entity.Id, entity.MaxHealth, entity.Health, entity.Level);

        _entities[entity.Id] = new EntityRenderState(entity);
    }

    private void UpdateEntity(Entity entity, EntityRenderState state, bool worldChanged)
    {
        if (entity.Position != state.Position)
        {
            if (worldChanged) _buffer.SetPosition(entity.Id, entity.Position.X, entity.Position.Y);
            else _buffer.TweenTo(entity.Id, entity.Position.X, entity.Position.Y, TickDurationMs);
        }

        if (entity.Health != state.Health) _buffer.UpdateHealthBar(entity.Id, entity.Health);
        if (entity.Level != state.Level) _buffer.UpdateLevelDisplay(entity.Id, entity.Level);

        state.Update(entity);
    }

    private void ProcessAttackEvents(List<AttackEvent> attackEvents)
    {
        foreach (var attack in attackEvents)
        {
            if (attack.IsMelee) _buffer.BumpAttack(attack.AttackerId, attack.TargetId);

            _buffer.FloatingDamage(attack.TargetPosition.X, attack.TargetPosition.Y, attack.Damage);
        }
    }

    private void ProcessLevelUpEvents(List<LevelUpEvent>? levelUpEvents)
    {
        if (levelUpEvents is null || levelUpEvents.Count == 0) return;

        foreach (var levelEvent in levelUpEvents)
        {
            _buffer.FloatingLevelUp(levelEvent.Position.X, levelEvent.Position.Y);
        }
    }

    private void ProcessCamera(List<Entity> entities, string playerId, bool worldChanged)
    {
        var player = entities.FirstOrDefault(e => e.Id == playerId);
        if (player is null) return;

        var camX = CenterX - (player.Position.X * TileSize);
        var camY = CenterY - (player.Position.Y * TileSize);

        if (!_cameraInitialized || worldChanged)
        {
            _buffer.SnapCamera(camX, camY);
            _cameraInitialized = true;
        }
        else _buffer.TweenCamera(camX, camY, TickDurationMs);
    }

    private void ProcessPOIs(List<POI>? pois)
    {
        if (pois is null) return;

        foreach (var poi in pois)
        {
            var spriteId = $"poi-{poi.Id}";
            if (_staticSprites.Contains(spriteId)) continue;

            var (frame, tint) = poi.Type == POIType.Town
                ? TileConfigs[TileType.TownCenter]
                : TileConfigs[TileType.POIMarker];

            _buffer.CreateSprite(spriteId, frame, poi.Position.X, poi.Position.Y, tint, depth: 5);
            _staticSprites.Add(spriteId);
        }
    }

    private void ProcessExitMarker(Point? exitMarker)
    {
        if (exitMarker is null) return;

        const string spriteId = "exit-marker";
        if (_staticSprites.Contains(spriteId)) return;

        var (frame, tint) = TileConfigs[TileType.ExitMarker];
        _buffer.CreateSprite(spriteId, frame, exitMarker.X, exitMarker.Y, tint, depth: 5);
        _staticSprites.Add(spriteId);
    }
}
