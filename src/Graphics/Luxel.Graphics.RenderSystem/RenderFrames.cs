using System.Collections.ObjectModel;

namespace Luxel.Graphics.RenderSystem;

public readonly record struct RenderOpportunity(
    ulong Sequence,
    TimeSpan Timestamp,
    TimeSpan Delta);

[Flags]
public enum RenderSystemChangeFlags
{
    None = 0,
    Assignment = 1 << 0,
    Resize = 1 << 1,
    Device = 1 << 2,
}

public readonly record struct RenderSystemFrameContext(
    TimeSpan Elapsed,
    TimeSpan Delta,
    RenderSystemChangeFlags Changes,
    ulong AssignmentGeneration);

public sealed class CompiledRenderFeatureSetRegistry
{
    private readonly IReadOnlyDictionary<RenderFeatureSetId, CompiledRenderFeatureSet> _sets;

    public CompiledRenderFeatureSetRegistry(
        IReadOnlyDictionary<RenderFeatureSetId, CompiledRenderFeatureSet> sets)
    {
        ArgumentNullException.ThrowIfNull(sets);
        _sets = new ReadOnlyDictionary<RenderFeatureSetId, CompiledRenderFeatureSet>(
            new Dictionary<RenderFeatureSetId, CompiledRenderFeatureSet>(sets));
    }

    public IReadOnlyDictionary<RenderFeatureSetId, CompiledRenderFeatureSet> Sets => _sets;
    public bool TryGet(RenderFeatureSetId id, out CompiledRenderFeatureSet? set) => _sets.TryGetValue(id, out set);
}

public sealed class RenderFrameResourceRegistry
{
    private readonly IReadOnlyDictionary<Type, object> _resources;

    public RenderFrameResourceRegistry(IReadOnlyDictionary<Type, object>? resources = null)
    {
        _resources = new ReadOnlyDictionary<Type, object>(
            resources is null ? new Dictionary<Type, object>() : new Dictionary<Type, object>(resources));
    }

    public bool TryGet<T>(out T? value) where T : class
    {
        if (_resources.TryGetValue(typeof(T), out object? resource) && resource is T typed)
        {
            value = typed;
            return true;
        }
        value = null;
        return false;
    }

    public T GetRequired<T>() where T : class
        => TryGet<T>(out T? value)
            ? value!
            : throw new InvalidOperationException($"Frame resource '{typeof(T).FullName}' is not available.");

    public RenderFrameResourceRegistry With<T>(T resource) where T : class
    {
        ArgumentNullException.ThrowIfNull(resource);
        var resources = new Dictionary<Type, object>(_resources)
        {
            [typeof(T)] = resource,
        };
        return new RenderFrameResourceRegistry(resources);
    }
}

public sealed record RenderSystemFrameSnapshot(
    RenderSystemFrameContext Context,
    CompiledRenderFeatureSetRegistry FeatureSets,
    RenderFrameResourceRegistry Resources);
