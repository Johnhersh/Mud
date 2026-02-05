using Microsoft.JSInterop;

namespace Mud.Client.Input;

/// <summary>
/// Handles keyboard input at the document level, routing key presses to registered callbacks.
/// </summary>
public sealed class KeyboardHandler : IAsyncDisposable
{
    private readonly IJSRuntime _js;
    private readonly DotNetObjectReference<KeyboardHandler> _dotNetRef;
    private readonly Dictionary<string, Func<Task>> _handlers = new();
    private bool _disposed;

    private KeyboardHandler(IJSRuntime js)
    {
        _js = js;
        _dotNetRef = DotNetObjectReference.Create(this);
    }

    /// <summary>
    /// Creates and registers a keyboard handler with the configured key bindings.
    /// </summary>
    public static async Task<KeyboardHandler> CreateAsync(IJSRuntime js, Action<Builder> configure)
    {
        var handler = new KeyboardHandler(js);
        var builder = new Builder(handler);
        configure(builder);

        var keyCodes = handler._handlers.Keys.ToArray();
        await js.InvokeVoidAsync("registerKeyboardHandler", handler._dotNetRef, keyCodes);

        return handler;
    }

    /// <summary>
    /// Called from JavaScript when a registered key is pressed.
    /// </summary>
    [JSInvokable]
    public async Task OnKeyDown(string code)
    {
        if (_disposed) return;

        if (_handlers.TryGetValue(code, out var callback))
        {
            await callback();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            await _js.InvokeVoidAsync("unregisterKeyboardHandler");
        }
        catch (JSDisconnectedException)
        {
            // Circuit disconnected, cleanup already happened
        }

        _dotNetRef.Dispose();
    }

    /// <summary>
    /// Fluent builder for configuring keyboard bindings.
    /// </summary>
    public sealed class Builder
    {
        private readonly KeyboardHandler _handler;

        internal Builder(KeyboardHandler handler) => _handler = handler;

        /// <summary>
        /// Registers a callback for the specified key code.
        /// </summary>
        public Builder Add(Code code, Func<Task> callback)
        {
            _handler._handlers[code.ToString()] = callback;
            return this;
        }

        /// <summary>
        /// Registers a synchronous callback for the specified key code.
        /// </summary>
        public Builder Add(Code code, Action callback)
        {
            _handler._handlers[code.ToString()] = () =>
            {
                callback();
                return Task.CompletedTask;
            };
            return this;
        }
    }
}
