namespace Mud.Client.Rendering;

/// <summary>
/// Base class for all render commands sent to Phaser.
/// </summary>
public abstract record RenderCommand(string Type, string? EntityId);

// ============ SPRITE LIFECYCLE ============

public record CreateSpriteCommand(
    string EntityId,
    int TileIndex,
    int X,
    int Y,
    uint? Tint = null,
    int? Depth = null
) : RenderCommand("CreateSprite", EntityId);

public record DestroySpriteCommand(string EntityId)
    : RenderCommand("DestroySprite", EntityId);

public record SetPositionCommand(string EntityId, int X, int Y)
    : RenderCommand("SetPosition", EntityId);

public record SetTintCommand(string EntityId, uint Tint)
    : RenderCommand("SetTint", EntityId);

public record SetVisibleCommand(string EntityId, bool Visible)
    : RenderCommand("SetVisible", EntityId);

public record SetDepthCommand(string EntityId, int Depth)
    : RenderCommand("SetDepth", EntityId);

// ============ MOVEMENT/ANIMATION ============

public record TweenToCommand(
    string EntityId,
    int X,
    int Y,
    int DurationMs = 300,
    string Easing = "Sine.easeInOut"
) : RenderCommand("TweenTo", EntityId);

public record BumpAttackCommand(
    string AttackerId,
    string TargetId,
    int DurationMs = 150
) : RenderCommand("BumpAttack", null);

// ============ CAMERA ============

public record TweenCameraCommand(
    int X,
    int Y,
    int DurationMs,
    string Easing = "Sine.easeOut"
) : RenderCommand("TweenCamera", null);

public record SnapCameraCommand(int X, int Y)
    : RenderCommand("SnapCamera", null);

// ============ HEALTH BARS ============

public record CreateHealthBarCommand(
    string EntityId,
    int MaxHealth,
    int CurrentHealth
) : RenderCommand("CreateHealthBar", EntityId);

public record UpdateHealthBarCommand(string EntityId, int CurrentHealth)
    : RenderCommand("UpdateHealthBar", EntityId);

// ============ TERRAIN ============

public record SetTerrainCommand(
    string WorldId,
    List<TileRenderData> Tiles,
    int Width,
    int Height,
    int GhostPadding,
    bool IsInstance
) : RenderCommand("SetTerrain", null);

/// <summary>
/// Simplified tile data for rendering (no walkability needed in JS).
/// </summary>
public record TileRenderData(int Type);

// ============ QUEUED PATH ============

public record SetQueuedPathCommand(string EntityId, List<PathPoint> Path)
    : RenderCommand("SetQueuedPath", EntityId);

/// <summary>
/// Path point for rendering (JavaScript-friendly).
/// </summary>
public record PathPoint(int X, int Y);

// ============ TARGETING ============

public record SetTargetReticleCommand(string? EntityId)
    : RenderCommand("SetTargetReticle", EntityId);
