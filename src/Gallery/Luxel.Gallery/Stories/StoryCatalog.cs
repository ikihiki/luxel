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

/// <summary>Galleryの閲覧導線に使う安定した表示順。</summary>
public static class StoryPresentationOrder
{
    private static readonly IReadOnlyDictionary<string, int> TopLevelRanks = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        ["Start"] = 0,
        ["Tutorials"] = 1,
        ["Learn"] = 2,
        ["Controls"] = 3,
        ["Examples"] = 4,
        ["Reference"] = 5,
        ["Internals"] = 6,
    };

    public static IReadOnlyList<StoryInfo> Apply(IEnumerable<StoryInfo> stories)
    {
        ArgumentNullException.ThrowIfNull(stories);
        return stories.Select((story, index) => new
            {
                Story = story,
                Index = index,
                TopLevelRank = Rank(story.Path),
                Depth = story.Path.Count(character => character == '/'),
            })
            .OrderBy(item => item.TopLevelRank)
            .ThenBy(item => item.Depth)
            .ThenBy(item => item.Index)
            .Select(item => item.Story)
            .ToArray();
    }

    private static int Rank(string path)
    {
        int slash = path.IndexOf('/');
        string topLevel = slash < 0 ? path : path[..slash];
        return TopLevelRanks.GetValueOrDefault(topLevel, int.MaxValue);
    }
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
        if (story.Kind == StoryKind.Unspecified)
            story = story with { Kind = StoryKindResolver.Infer(story.Path) };
        if (story.Ownership is null && _ownership is not null)
            story = story with { Ownership = _ownership };
        bool replacing = _stories.TryGetValue(story.Path, out StoryInfo? existing);
        if (replacing)
        {
            StoryInfo generated = existing!;
            if (!replaceGenerated)
                throw new InvalidOperationException($"Story '{story.Path}' is registered more than once.");
            if (generated.RegistrationKind != StoryRegistrationKind.GeneratedComponentFallback
                || story.RegistrationKind != StoryRegistrationKind.Authored
                || generated.ProductionComponent is null)
                throw new InvalidOperationException(
                    $"Story '{story.Path}' can only replace an exact generated component fallback with an authored story.");
            if (story.ProductionComponent is not null && story.ProductionComponent != generated.ProductionComponent)
                throw new InvalidOperationException(
                    $"Story '{story.Path}' attempted to replace a fallback for another production component.");
            // The authored implementation replaces only the renderer/source. Canonical production identity
            // remains authoritative; Playground inherits the generated schema while Basic is always args-free.
            story = story with
            {
                ArgDefinitions = story.Kind == StoryKind.Basic
                    ? Array.Empty<StoryArgDefinition>()
                    : story.ArgDefinitions ?? generated.ArgDefinitions,
                CapabilityNote = story.CapabilityNote ?? generated.CapabilityNote,
                ShortDescription = story.ShortDescription ?? generated.ShortDescription,
                LongDescription = story.LongDescription ?? generated.LongDescription,
                ProductionComponent = generated.ProductionComponent,
                Ownership = generated.Ownership,
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
