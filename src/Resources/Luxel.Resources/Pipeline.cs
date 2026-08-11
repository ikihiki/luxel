namespace Luxel.Resources;

public interface IResourceSource
{
    IEnumerable<string> Schemes { get; }
    Task<byte[]> ReadAsync(ResourceUri uri, LoadContext ctx);
    IReloadToken? Watch(ResourceUri uri, Action onChanged) => null;
}

public interface IResourceStep { }

public interface IResourceStep<TIn, TOut> : IResourceStep
{
    IEnumerable<string>? Extensions => null;
    IEnumerable<string>? FragmentPatterns => null;
    Task<TOut> RunAsync(TIn input, ResourceUri uri, LoadContext ctx);
}

public delegate Task<T> Loader<T>(LoadContext ctx);

internal sealed class ResourceStepTable
{
    private readonly IReadOnlyDictionary<string, ResourceSourceDescriptor> _sources;
    private readonly IReadOnlyDictionary<Type, ResourceStepDescriptor[]> _steps;

    public ResourceStepTable(ResourceSourceDescriptor[] sources, ResourceStepDescriptor[] steps)
    {
        _sources = sources.SelectMany(source => source.Source.Schemes.Select(scheme => (scheme: scheme.ToLowerInvariant(), source)))
            .ToDictionary(pair => pair.scheme, pair => pair.source, StringComparer.OrdinalIgnoreCase);
        _steps = steps.GroupBy(step => step.Output).ToDictionary(group => group.Key,
            group => group.OrderByDescending(step => step.Priority).ToArray());
    }

    public ResourceSourceDescriptor? Source(string scheme) => _sources.TryGetValue(scheme.ToLowerInvariant(), out var source) ? source : null;

    public ResourceStepDescriptor? Select(Type output, string extension, string fragment, Type? input = null)
    {
        if (!_steps.TryGetValue(output, out ResourceStepDescriptor[]? registered)) return null;
        IEnumerable<ResourceStepDescriptor> candidates = input is null ? registered : registered.Where(step => step.Input == input);
        candidates = fragment.Length == 0
            ? candidates.Where(step => step.FragmentPatterns is null)
            : candidates.Where(step => step.FragmentPatterns is { } patterns && patterns.Any(pattern => FragmentMatch(pattern, fragment)));
        ResourceStepDescriptor[] matches = candidates.Where(step => step.Extensions is null || step.Extensions.Contains(extension)).ToArray();
        return matches.FirstOrDefault();
    }

    private static bool FragmentMatch(string pattern, string fragment) => pattern.EndsWith("/*", StringComparison.Ordinal)
        ? fragment.StartsWith(pattern[..^1], StringComparison.Ordinal)
        : string.Equals(pattern, fragment, StringComparison.Ordinal);
}
