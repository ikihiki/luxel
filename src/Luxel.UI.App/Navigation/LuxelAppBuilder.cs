namespace Luxel.UI.App;

/// <summary>Builds a mapped-screen Luxel UI application.</summary>
public sealed class LuxelAppBuilder
{
    private bool _built;

    internal LuxelAppBuilder(string[]? args)
    {
        Args = args ?? [];
    }

    /// <summary>Application arguments supplied to <see cref="LuxelApp.CreateBuilder"/>.</summary>
    public IReadOnlyList<string> Args { get; }

    /// <summary>Window and renderer options.</summary>
    public LuxelAppOptions Options { get; } = new();

    /// <summary>Builds this builder once.</summary>
    public LuxelUiApplication Build()
    {
        if (_built) throw new InvalidOperationException("LuxelAppBuilder.Build may only be called once.");
        _built = true;
        return new LuxelUiApplication(Options);
    }
}
