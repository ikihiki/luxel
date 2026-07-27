using Luxel.Diagnostics;
using Luxel.Platform.Abstraction;
using Luxel.Typography;

namespace Luxel.UI.App;

/// <summary>Services owned by a running Luxel UI application.</summary>
public sealed class LuxelAppRuntime
{
    private readonly List<IDisposable> _owned = [];

    internal LuxelAppRuntime(GpuDevice device, VectorFont font, WindowSystem windows, WindowManager manager)
    {
        Device = device;
        Font = font;
        Windows = windows;
        WindowManager = manager;
    }

    public GpuDevice Device { get; }
    public VectorFont Font { get; }
    public WindowSystem Windows { get; }
    public WindowManager WindowManager { get; }
    public EngineCommands Commands => WindowManager.Commands;
    public WindowHost MainWindow { get; internal set; } = null!;

    /// <summary>Registers a resource to be disposed in reverse order when the application stops.</summary>
    public T Own<T>(T resource) where T : IDisposable
    {
        ArgumentNullException.ThrowIfNull(resource);
        _owned.Add(resource);
        return resource;
    }

    internal void DisposeOwned()
    {
        for (int i = _owned.Count - 1; i >= 0; i--) _owned[i].Dispose();
        _owned.Clear();
    }
}

internal sealed class LuxelAppLifecycle
{
    public Action<LuxelAppRuntime>? Configure { get; set; }
    public Action<LuxelAppRuntime>? Started { get; set; }
    public Action<LuxelAppRuntime, float>? Frame { get; set; }
}
