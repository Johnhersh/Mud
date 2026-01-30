using System.Text.Json.Serialization;

namespace Mud.Client.Rendering;

/// <summary>
/// Command types for the Phaser render pipeline.
/// IMPORTANT: These types are mirrored in phaser-renderer.js JSDoc typedefs.
/// Any changes here must be reflected there.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RenderCommandType
{
    // Sprite lifecycle
    CreateSprite,
    DestroySprite,
    SetPosition,
    SetTint,
    SetVisible,
    SetDepth,

    // Movement/Animation
    TweenTo,
    BumpAttack,
    FloatingDamage,

    // Camera
    TweenCamera,
    SnapCamera,

    // Health bars
    CreateHealthBar,
    UpdateHealthBar,

    // Terrain
    SetTerrain,
    SwitchTerrainLayer,

    // Queued path
    SetQueuedPath,

    // Targeting
    SetTargetReticle
}

/// <summary>
/// Base class for all render commands sent to Phaser.
/// </summary>
public abstract record RenderCommand(RenderCommandType Type, string? EntityId);

// ============ SPRITE LIFECYCLE ============

public record CreateSpriteCommand(
    string EntityId,
    int TileIndex,
    int X,
    int Y,
    uint? Tint = null,
    int? Depth = null
) : RenderCommand(RenderCommandType.CreateSprite, EntityId);

public record DestroySpriteCommand(string EntityId)
    : RenderCommand(RenderCommandType.DestroySprite, EntityId);

public record SetPositionCommand(string EntityId, int X, int Y)
    : RenderCommand(RenderCommandType.SetPosition, EntityId);

public record SetTintCommand(string EntityId, uint Tint)
    : RenderCommand(RenderCommandType.SetTint, EntityId);

public record SetVisibleCommand(string EntityId, bool Visible)
    : RenderCommand(RenderCommandType.SetVisible, EntityId);

public record SetDepthCommand(string EntityId, int Depth)
    : RenderCommand(RenderCommandType.SetDepth, EntityId);

// ============ MOVEMENT/ANIMATION ============

public record TweenToCommand(
    string EntityId,
    int X,
    int Y,
    int DurationMs = 300,
    string Easing = "Sine.easeInOut"
) : RenderCommand(RenderCommandType.TweenTo, EntityId);

public record BumpAttackCommand(
    string AttackerId,
    string TargetId,
    int DurationMs = 150
) : RenderCommand(RenderCommandType.BumpAttack, null);

public record FloatingDamageCommand(
    int X,
    int Y,
    int Damage,
    int DurationMs = 1000
) : RenderCommand(RenderCommandType.FloatingDamage, null);

// ============ CAMERA ============

public record TweenCameraCommand(
    int X,
    int Y,
    int DurationMs,
    string Easing = "Sine.easeOut"
) : RenderCommand(RenderCommandType.TweenCamera, null);

public record SnapCameraCommand(int X, int Y)
    : RenderCommand(RenderCommandType.SnapCamera, null);

// ============ HEALTH BARS ============

public record CreateHealthBarCommand(
    string EntityId,
    int MaxHealth,
    int CurrentHealth
) : RenderCommand(RenderCommandType.CreateHealthBar, EntityId);

public record UpdateHealthBarCommand(string EntityId, int CurrentHealth)
    : RenderCommand(RenderCommandType.UpdateHealthBar, EntityId);

// ============ TERRAIN ============

public record SetTerrainCommand(
    string WorldId,
    List<TileRenderData> Tiles,
    int Width,
    int Height,
    int GhostPadding,
    bool IsInstance
) : RenderCommand(RenderCommandType.SetTerrain, null);

/// <summary>
/// Switch which terrain layer is visible (overworld vs instance).
/// Used when returning to a world whose tiles were already sent.
/// </summary>
public record SwitchTerrainLayerCommand(bool IsInstance) : RenderCommand(RenderCommandType.SwitchTerrainLayer, null);

/// <summary>
/// Simplified tile data for rendering (no walkability needed in JS).
/// </summary>
public record TileRenderData(int Type);

// ============ QUEUED PATH ============

public record SetQueuedPathCommand(string EntityId, List<PathPoint> Path)
    : RenderCommand(RenderCommandType.SetQueuedPath, EntityId);

/// <summary>
/// Path point for rendering (JavaScript-friendly).
/// </summary>
public record PathPoint(int X, int Y);

// ============ TARGETING ============

public record SetTargetReticleCommand(string? EntityId)
    : RenderCommand(RenderCommandType.SetTargetReticle, EntityId);
