using Luxel.UI;
namespace Luxel.Gallery;

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

/// <summary>同じStoryMetaパス内で、現在ページの前後にある文書候補。</summary>
public readonly record struct StoryPageNavigation(StoryInfo? Previous, StoryInfo? Next)
{
    public bool IsEmpty => Previous is null && Next is null;

    public static StoryPageNavigation Resolve(StoryCatalog catalog, StoryInfo current)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(current);
        int slash = current.Path.LastIndexOf('/');
        if (slash < 0 || !current.IncludeInPageNavigation) return default;
        string group = current.Path[..slash];
        StoryInfo[] pages = catalog.All.Where(story => story.IncludeInPageNavigation
            && story.Path.LastIndexOf('/') == slash
            && story.Path.StartsWith(group + "/", StringComparison.Ordinal)).ToArray();
        int index = Array.FindIndex(pages, story => story.Path == current.Path);
        if (index < 0) return default;
        return new StoryPageNavigation(
            index > 0 ? pages[index - 1] : null,
            index + 1 < pages.Length ? pages[index + 1] : null);
    }
}

/// <summary>Story project の composition root が使用する明示登録 builder。</summary>
public sealed class StoryCatalogBuilder
{
    private readonly Dictionary<string, StoryInfo> _stories = new(StringComparer.Ordinal);
    private readonly List<string> _registrationOrder = new();
    private readonly Dictionary<string, string> _aliases = new(StringComparer.Ordinal);
    private readonly List<Action<StoryCatalogBuilder>> _providers = new();
    private StoryOwnership? _ownership;
    private bool _built;

    public IDisposable BeginOwnership(StoryOwnership ownership)
    {
        ObjectDisposedException.ThrowIf(_built, this);
        ArgumentNullException.ThrowIfNull(ownership);
        StoryOwnership? previous = _ownership;
        _ownership = ownership;
        return new OwnershipScope(this, previous);
    }

    public bool ContainsPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return _stories.ContainsKey(path);
    }

    public StoryCatalogBuilder Add(StoryInfo story, bool replaceGenerated = false)
    {
        ObjectDisposedException.ThrowIf(_built, this);
        ArgumentNullException.ThrowIfNull(story);
        if (story.Ownership is null && _ownership is not null)
            story = story with { Ownership = _ownership };
        bool replacing = _stories.TryGetValue(story.Path, out StoryInfo? existing);
        if (replacing)
        {
            if (!replaceGenerated)
                throw new InvalidOperationException($"Story '{story.Path}' is registered more than once.");
            if (existing.RegistrationKind != StoryRegistrationKind.GeneratedComponentFallback
                || story.RegistrationKind != StoryRegistrationKind.Authored
                || existing.ProductionComponent is null)
                throw new InvalidOperationException(
                    $"Story '{story.Path}' can only replace an exact generated component fallback with an authored story.");
            if (story.ProductionComponent is not null && story.ProductionComponent != existing.ProductionComponent)
                throw new InvalidOperationException(
                    $"Story '{story.Path}' attempted to replace a fallback for another production component.");
            // The authored implementation replaces only the renderer/source. Canonical production identity
            // and the generated static schema remain authoritative for URLs/manifests.
            story = story with
            {
                ArgDefinitions = story.ArgDefinitions ?? existing.ArgDefinitions,
                CapabilityNote = story.CapabilityNote ?? existing.CapabilityNote,
                ProductionComponent = existing.ProductionComponent,
                Ownership = existing.Ownership,
            };
        }
        _stories[story.Path] = story;
        if (!replacing) _registrationOrder.Add(story.Path);
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

        StoryInfo[] stories = _registrationOrder.Select(path => _stories[path]).ToArray();
        return new StoryCatalog(stories, new Dictionary<string, string>(_aliases, StringComparer.Ordinal));
    }

    private sealed class OwnershipScope(StoryCatalogBuilder builder, StoryOwnership? previous) : IDisposable
    {
        private StoryCatalogBuilder? _builder = builder;

        public void Dispose()
        {
            StoryCatalogBuilder? current = Interlocked.Exchange(ref _builder, null);
            if (current is not null) current._ownership = previous;
        }
    }

}
