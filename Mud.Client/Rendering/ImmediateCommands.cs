using Microsoft.JSInterop;

namespace Mud.Client.Rendering;

/// <summary>
/// Client-side-only commands sent immediately to JS without buffering.
/// Use for UI state that doesn't depend on server ticks.
/// </summary>
public class ImmediateCommands
{
    private readonly IJSRuntime _js;

    public ImmediateCommands(IJSRuntime js) => _js = js;

    public async ValueTask SetTargetReticle(string? entityId)
        => await _js.InvokeVoidAsync("executeCommands", new[] { new SetTargetReticleCommand(entityId) });
}
