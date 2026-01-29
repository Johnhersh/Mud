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

    // ============ FLUENT API ============

    public RenderCommandBuffer CreateSprite(string id, int tileIndex, int x, int y, uint? tint = null, int? depth = null)
    {
        _commands.Add(new CreateSpriteCommand(id, tileIndex, x, y, tint, depth));
        return this;
    }

    public RenderCommandBuffer DestroySprite(string id)
    {
        _commands.Add(new DestroySpriteCommand(id));
        return this;
    }

    public RenderCommandBuffer SetPosition(string id, int x, int y)
    {
        _commands.Add(new SetPositionCommand(id, x, y));
        return this;
    }

    public RenderCommandBuffer TweenTo(string id, int x, int y, int durationMs = 300, string easing = "Sine.easeInOut")
    {
        _commands.Add(new TweenToCommand(id, x, y, durationMs, easing));
        return this;
    }

    public RenderCommandBuffer BumpAttack(string attackerId, string targetId, int durationMs = 150)
    {
        _commands.Add(new BumpAttackCommand(attackerId, targetId, durationMs));
        return this;
    }

    public RenderCommandBuffer TweenCamera(int x, int y, int durationMs, string easing = "Sine.easeOut")
    {
        _commands.Add(new TweenCameraCommand(x, y, durationMs, easing));
        return this;
    }

    public RenderCommandBuffer SnapCamera(int x, int y)
    {
        _commands.Add(new SnapCameraCommand(x, y));
        return this;
    }

    public RenderCommandBuffer CreateHealthBar(string entityId, int maxHealth, int currentHealth)
    {
        _commands.Add(new CreateHealthBarCommand(entityId, maxHealth, currentHealth));
        return this;
    }

    public RenderCommandBuffer UpdateHealthBar(string entityId, int currentHealth)
    {
        _commands.Add(new UpdateHealthBarCommand(entityId, currentHealth));
        return this;
    }

    public RenderCommandBuffer SetTerrain(string worldId, List<TileRenderData> tiles, int width, int height, int ghostPadding, bool isInstance)
    {
        _commands.Add(new SetTerrainCommand(worldId, tiles, width, height, ghostPadding, isInstance));
        return this;
    }

    public RenderCommandBuffer SwitchTerrainLayer(bool isInstance)
    {
        _commands.Add(new SwitchTerrainLayerCommand(isInstance));
        return this;
    }

    public RenderCommandBuffer SetQueuedPath(string entityId, List<PathPoint> path)
    {
        _commands.Add(new SetQueuedPathCommand(entityId, path));
        return this;
    }

    public RenderCommandBuffer SetTargetReticle(string? entityId)
    {
        _commands.Add(new SetTargetReticleCommand(entityId));
        return this;
    }

    // ============ EXECUTION ============

    /// <summary>
    /// Flushes all commands to JavaScript with a single interop call.
    /// </summary>
    public async ValueTask FlushAsync()
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
