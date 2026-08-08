using Luxel.UI;

namespace Luxel.Framework.UI;

/// <summary>Registers fixed UI paths and launches them through the Luxel UI navigation core.</summary>
public sealed class LuxelUiApplication
{
    private readonly Dictionary<string, ScreenRoute> _routes = new(StringComparer.Ordinal);
    private readonly LuxelAppOptions _options;
    private readonly LuxelAppLifecycle _lifecycle;
    private bool _running;

    internal LuxelUiApplication(LuxelAppOptions options, LuxelAppLifecycle lifecycle)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));
    }

    /// <summary>Registers a screen that does not require navigation state.</summary>
    public LuxelUiApplication MapScreen(string path, Func<Widget> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return MapScreen(path, _ => handler());
    }

    /// <summary>Registers a screen that receives the shared navigation state.</summary>
    public LuxelUiApplication MapScreen(string path, Func<Navigation, Widget> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        string normalized = NavigationPath.Normalize(path);
        if (!_routes.TryAdd(normalized, new ScreenRoute(normalized, handler)))
            throw new InvalidOperationException($"Navigation path '{normalized}' is already registered.");
        return this;
    }

    /// <summary>Runs the application with the navigation content host as the root widget.</summary>
    public void Run(string initialPath = "/")
        => RunCore(initialPath, null);

    /// <summary>Runs the application with a persistent shell around the navigation content host.</summary>
    public void Run(string initialPath, Func<Navigation, Widget, Widget> shellFactory)
    {
        ArgumentNullException.ThrowIfNull(shellFactory);
        RunCore(initialPath, shellFactory);
    }

    internal Widget CreateRoot(string initialPath, Func<Navigation, Widget, Widget>? shellFactory = null)
    {
        string normalized = NavigationPath.Normalize(initialPath);
        if (!_routes.ContainsKey(normalized))
            throw new InvalidOperationException($"Navigation path '{normalized}' is not registered.");

        var navigation = new Navigation(normalized, _routes.ContainsKey);
        var host = new NavigationHost(navigation, Resolve);
        return shellFactory?.Invoke(navigation, host)
            ?? host;
    }

    private Widget Resolve(string path, Navigation navigation)
    {
        if (!_routes.TryGetValue(path, out ScreenRoute? route))
            throw new InvalidOperationException($"Navigation path '{path}' is not registered.");
        return route.Factory(navigation)
            ?? throw new InvalidOperationException($"The screen factory for '{path}' returned null.");
    }

    private void RunCore(string initialPath, Func<Navigation, Widget, Widget>? shellFactory)
    {
        if (_running) throw new InvalidOperationException("A Luxel UI application can only be run once.");
        _running = true;
        LuxelApp.Run(() => CreateRoot(initialPath, shellFactory), _options, _lifecycle);
    }
}
