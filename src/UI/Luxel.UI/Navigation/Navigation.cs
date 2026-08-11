namespace Luxel.UI;

/// <summary>Owns the current UI path and a back stack independently of any application route registry.</summary>
public sealed class Navigation
{
    private readonly List<string> _history = new();
    private readonly Func<string, bool>? _canNavigate;
    private readonly Signal<string> _currentPath;
    private readonly Signal<int> _historyVersion = new(0);

    /// <summary>Creates navigation state at <paramref name="initialPath"/>.</summary>
    public Navigation(string initialPath, Func<string, bool>? canNavigate = null)
    {
        string normalized = NavigationPath.Normalize(initialPath);
        if (canNavigate is not null && !canNavigate(normalized))
            throw UnknownPath(normalized);

        _canNavigate = canNavigate;
        _history.Add(normalized);
        _currentPath = new Signal<string>(normalized);
    }

    /// <summary>The current normalized, case-sensitive path.</summary>
    public string CurrentPath => _currentPath.Value;

    /// <summary>Whether <see cref="Back"/> can return to an earlier path.</summary>
    public bool CanGoBack
    {
        get
        {
            _ = _historyVersion.Value;
            return _history.Count > 1;
        }
    }

    /// <summary>Pushes a destination onto the history stack. Navigating to the current path is a no-op.</summary>
    public void Navigate(string path)
    {
        string normalized = ValidateDestination(path);
        if (StringComparer.Ordinal.Equals(normalized, _currentPath.Peek())) return;
        _history.Add(normalized);
        Publish(normalized);
    }

    /// <summary>Replaces the current history entry without increasing history depth.</summary>
    public void Replace(string path)
    {
        string normalized = ValidateDestination(path);
        if (StringComparer.Ordinal.Equals(normalized, _currentPath.Peek())) return;
        _history[^1] = normalized;
        Publish(normalized);
    }

    /// <summary>Returns to the previous history entry, or returns false at the root entry.</summary>
    public bool Back()
    {
        if (_history.Count <= 1) return false;
        _history.RemoveAt(_history.Count - 1);
        Publish(_history[^1]);
        return true;
    }

    private string ValidateDestination(string path)
    {
        string normalized = NavigationPath.Normalize(path);
        if (_canNavigate is not null && !_canNavigate(normalized))
            throw UnknownPath(normalized);
        return normalized;
    }

    private void Publish(string path)
    {
        _currentPath.Value = path;
        _historyVersion.Value++;
    }

    private static InvalidOperationException UnknownPath(string path)
        => new($"Navigation path '{path}' is not registered.");
}
