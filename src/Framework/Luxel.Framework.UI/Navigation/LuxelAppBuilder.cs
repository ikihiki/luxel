namespace Luxel.Framework.UI;

/// <summary>Builds a mapped-screen Luxel UI application.</summary>
public sealed class LuxelAppBuilder
{
    private readonly LuxelAppLifecycle _lifecycle = new();
    private bool _built;

    internal LuxelAppBuilder(string[]? args)
    {
        Args = args ?? [];
    }

    /// <summary>Application arguments supplied to <see cref="LuxelApp.CreateBuilder"/>.</summary>
    public IReadOnlyList<string> Args { get; }

    /// <summary>Window and renderer options. Auto defaults follow the current operating system.</summary>
    public LuxelAppOptions Options { get; } = new();

    /// <summary>Configures runtime services before the root widget is built.</summary>
    public LuxelAppBuilder ConfigureRuntime(Action<LuxelAppRuntime> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        _lifecycle.Configure += configure;
        return this;
    }

    /// <summary>Runs after the main window and root UI have been created.</summary>
    public LuxelAppBuilder OnStarted(Action<LuxelAppRuntime> started)
    {
        ArgumentNullException.ThrowIfNull(started);
        _lifecycle.Started += started;
        return this;
    }

    /// <summary>Runs once after every application frame.</summary>
    public LuxelAppBuilder OnFrame(Action<LuxelAppRuntime, float> frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        _lifecycle.Frame += frame;
        return this;
    }

    /// <summary>Builds this builder once.</summary>
    public LuxelUiApplication Build()
    {
        if (_built) throw new InvalidOperationException("LuxelAppBuilder.Build may only be called once.");
        _built = true;
        return new LuxelUiApplication(Options, _lifecycle);
    }
}
