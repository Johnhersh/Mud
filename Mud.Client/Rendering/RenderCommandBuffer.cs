using Microsoft.JSInterop;

namespace Mud.Client.Rendering;

/// <summary>
/// Batches render commands and executes them with a single interop call.
/// </summary>
public class RenderCommandBuffer
{
    // Use List<object> to ensure polymorphic serialization includes all derived type properties
    private readonly List<object> _commands = new();
    private readonly IJSRuntime _js;

    public RenderCommandBuffer(IJSRuntime js) => _js = js;

    // ============ COMMAND QUEUEING ============

    public void CreateSprite(string id, int tileIndex, int x, int y, uint? tint = null, int? depth = null)
        => _commands.Add(new CreateSpriteCommand(id, tileIndex, x, y, tint, depth));

    public void DestroySprite(string id)
        => _commands.Add(new DestroySpriteCommand(id));

    public void SetPosition(string id, int x, int y)
        => _commands.Add(new SetPositionCommand(id, x, y));

    public void TweenTo(string id, int x, int y, int durationMs = 300, string easing = "Sine.easeInOut")
        => _commands.Add(new TweenToCommand(id, x, y, durationMs, easing));

    public void BumpAttack(string attackerId, string targetId, int durationMs = 150)
        => _commands.Add(new BumpAttackCommand(attackerId, targetId, durationMs));

    /// <summary>
    /// Generalized floating text command.
    /// </summary>
    public void FloatingText(int x, int y, string text, string color, int offsetY, int durationMs = 1000)
        => _commands.Add(new FloatingTextCommand(x, y, text, color, offsetY, durationMs));

    /// <summary>
    /// Floating damage text (red, floats down).
    /// </summary>
    public void FloatingDamage(int x, int y, int damage)
        => FloatingText(x, y, $"-{damage}", "#ff0000", 20, 1000);

    /// <summary>
    /// Floating XP text (green, floats up).
    /// </summary>
    public void FloatingXp(int x, int y, int amount)
        => FloatingText(x, y, $"+{amount} XP", "#00ff00", -30, 1000);

    /// <summary>
    /// Floating level up text (white, floats up).
    /// </summary>
    public void FloatingLevelUp(int x, int y)
        => FloatingText(x, y, "Level Up!", "#ffffff", -40, 1500);

    public void TweenCamera(int x, int y, int durationMs, string easing = "Sine.easeOut")
        => _commands.Add(new TweenCameraCommand(x, y, durationMs, easing));

    public void SnapCamera(int x, int y)
        => _commands.Add(new SnapCameraCommand(x, y));

    public void CreateHealthBar(string entityId, int maxHealth, int currentHealth, int level = 1)
        => _commands.Add(new CreateHealthBarCommand(entityId, maxHealth, currentHealth, level));

    public void UpdateHealthBar(string entityId, int currentHealth)
        => _commands.Add(new UpdateHealthBarCommand(entityId, currentHealth));

    public void UpdateLevelDisplay(string entityId, int level)
        => _commands.Add(new UpdateLevelDisplayCommand(entityId, level));

    public void SetTerrain(string worldId, List<TileRenderData> tiles, int width, int height, int ghostPadding, bool isInstance)
        => _commands.Add(new SetTerrainCommand(worldId, tiles, width, height, ghostPadding, isInstance));

    public void SwitchTerrainLayer(bool isInstance)
        => _commands.Add(new SwitchTerrainLayerCommand(isInstance));

    public void SetQueuedPath(string entityId, List<PathPoint> path)
        => _commands.Add(new SetQueuedPathCommand(entityId, path));

    // ============ EXECUTION ============

    public async ValueTask FlushToGameJS()
    {
        if (_commands.Count == 0) return;

        await _js.InvokeVoidAsync("executeCommands", _commands);
        _commands.Clear();
    }

    /// <summary>
    /// Clears all pending commands without executing them.
    /// </summary>
    public void Clear() => _commands.Clear();
}
