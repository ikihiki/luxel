namespace Luxel.UI;

/// <summary>Persistent content host that rebuilds the current screen when navigation changes.</summary>
public sealed class NavigationHost : CompositeControl
{
    private readonly Func<string, Navigation, Widget> _resolver;

    /// <summary>Creates a host backed by an application-independent screen resolver.</summary>
    public NavigationHost(Navigation navigation, Func<string, Navigation, Widget> resolver)
    {
        Navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
    }

    /// <summary>The navigation state used by this host.</summary>
    public Navigation Navigation { get; }

    protected override Widget Build()
        => _resolver(Navigation.CurrentPath, Navigation)
            ?? throw new InvalidOperationException($"The screen resolver returned null for '{Navigation.CurrentPath}'.");
}
