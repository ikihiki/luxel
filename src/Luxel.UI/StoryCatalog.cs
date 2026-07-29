namespace Luxel.UI;

/// <summary>明示登録された Story 群の immutable catalog。</summary>
public sealed class StoryCatalog
{
    private readonly IReadOnlyList<StoryInfo> _stories;
    private readonly IReadOnlyDictionary<string, StoryInfo> _byPath;
    private readonly IReadOnlyDictionary<string, string> _aliases;

    internal StoryCatalog(IReadOnlyList<StoryInfo> stories, IReadOnlyDictionary<string, string> aliases)
    {
        _stories = stories;
        _byPath = stories.ToDictionary(story => story.Path, StringComparer.Ordinal);
        _aliases = aliases;
    }

    public IReadOnlyList<StoryInfo> All => _stories;

    public StoryInfo? Find(string path)
    {
        string current = path;
        var visited = new HashSet<string>(StringComparer.Ordinal);
        while (_aliases.TryGetValue(current, out string? next))
        {
            if (!visited.Add(current)) throw new InvalidOperationException($"Story alias cycle detected at '{current}'.");
            current = next;
        }
        return _byPath.GetValueOrDefault(current);
    }

    public IReadOnlyList<string> AliasesFor(string canonicalPath) => _aliases
        .Where(pair => pair.Value == canonicalPath)
        .Select(pair => pair.Key)
        .Order(StringComparer.Ordinal)
        .ToArray();
}

/// <summary>Story project の composition root が使用する明示登録 builder。</summary>
public sealed class StoryCatalogBuilder
{
    private readonly Dictionary<string, StoryInfo> _stories = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _aliases = new(StringComparer.Ordinal);
    private readonly List<Action<StoryCatalogBuilder>> _providers = new();
    private bool _built;

    public StoryCatalogBuilder Add(StoryInfo story, bool replaceGenerated = false)
    {
        ObjectDisposedException.ThrowIf(_built, this);
        ArgumentNullException.ThrowIfNull(story);
        if (_stories.ContainsKey(story.Path) && !replaceGenerated)
            throw new InvalidOperationException($"Story '{story.Path}' is registered more than once.");
        _stories[story.Path] = story;
        return this;
    }

    public StoryCatalogBuilder AddAlias(string alias, string canonicalPath)
    {
        ObjectDisposedException.ThrowIf(_built, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(alias);
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalPath);
        if (alias == canonicalPath) throw new ArgumentException("Story alias must target another path.", nameof(canonicalPath));
        if (!_aliases.TryAdd(alias, canonicalPath))
            throw new InvalidOperationException($"Story alias '{alias}' is registered more than once.");
        return this;
    }

    public StoryCatalogBuilder AddProvider(Action<StoryCatalogBuilder> provider)
    {
        ObjectDisposedException.ThrowIf(_built, this);
        ArgumentNullException.ThrowIfNull(provider);
        _providers.Add(provider);
        return this;
    }

    public StoryCatalog Build()
    {
        ObjectDisposedException.ThrowIf(_built, this);
        foreach (Action<StoryCatalogBuilder> provider in _providers.ToArray()) provider(this);
        _built = true;

        foreach ((string alias, string target) in _aliases)
        {
            string current = target;
            var visited = new HashSet<string>(StringComparer.Ordinal) { alias };
            while (_aliases.TryGetValue(current, out string? next))
            {
                if (!visited.Add(current)) throw new InvalidOperationException($"Story alias cycle detected at '{current}'.");
                current = next;
            }
            if (!_stories.ContainsKey(current))
                throw new InvalidOperationException($"Story alias '{alias}' targets unknown story '{current}'.");
        }

        StoryInfo[] stories = _stories.Values
            .GroupBy(story => story.Component)
            .OrderBy(group => ComponentRank(group.Key))
            .ThenBy(group => group.Min(story => story.Order))
            .ThenBy(group => group.Key, StringComparer.Ordinal)
            .SelectMany(group => group.OrderBy(story => story.Order).ThenBy(story => story.Path, StringComparer.Ordinal))
            .ToArray();
        return new StoryCatalog(stories, new Dictionary<string, string>(_aliases, StringComparer.Ordinal));
    }

    private static int ComponentRank(string component) => component switch
    {
        "Start" => 0, "Learn" => 10, "Build" => 20, "Examples" => 30,
        "Controls" => 40, "Apps" => 50, "Game" => 60, "Reference" => 70,
        "Internals" => 80, "RealWindow" => 90, "ADR" => 100, "Docs" => 110,
        _ => 1000,
    };
}
