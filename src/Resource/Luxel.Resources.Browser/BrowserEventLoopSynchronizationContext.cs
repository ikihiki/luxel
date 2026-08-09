namespace Luxel.Resources.Browser;

/// <summary>
/// Provides an event-loop post boundary when browser-WASM does not install an ambient
/// <see cref="SynchronizationContext"/> for an exported async entry point.
/// </summary>
internal sealed class BrowserEventLoopSynchronizationContext : SynchronizationContext
{
    public static BrowserEventLoopSynchronizationContext Instance { get; } = new();

    private BrowserEventLoopSynchronizationContext() { }

    public override void Post(SendOrPostCallback callback, object? state)
    {
        ArgumentNullException.ThrowIfNull(callback);
        _ = PostAsync(callback, state);
    }

    private static async Task PostAsync(SendOrPostCallback callback, object? state)
    {
        // Yielding creates a separate browser event-loop turn on single-threaded WASM.
        // It also preserves the TaskScheduler selected by a threaded browser host.
        await Task.Yield();
        callback(state);
    }
}
