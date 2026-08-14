using System.Collections.ObjectModel;

namespace Luxel.Graphics.RenderSystem;

public readonly record struct RenderFeatureSetId(string Value)
{
    public override string ToString() => Value;
}

public sealed record RenderFeatureSetDefinition(RenderFeatureSetId Id, string DisplayName);

public interface IRenderFeature
{
    void AddPasses(RenderFeatureContext context);
}

/// <summary>batch submit/present completion notification for features with success-only publication.</summary>
public interface IRenderFeatureBatchObserver
{
    void CompleteBatch(bool succeeded);
}

public sealed class RenderFeatureContext
{
    public RenderFeatureContext(
        RenderGraph.RenderGraph graph,
        RenderOpportunity opportunity = default,
        RenderSystemFrameSnapshot? frame = null)
    {
        Graph = graph ?? throw new ArgumentNullException(nameof(graph));
        Opportunity = opportunity;
        Frame = frame;
    }

    public RenderGraph.RenderGraph Graph { get; }
    public RenderOpportunity Opportunity { get; }
    public RenderSystemFrameSnapshot? Frame { get; }
}

public sealed record CompiledRenderFeatureSet(
    RenderFeatureSetId Id,
    IReadOnlySet<IRenderFeature> Features);

public sealed class RenderFeatureAssignmentBuilder
{
    private readonly Dictionary<RenderFeatureSetId, HashSet<IRenderFeature>> _features = [];

    public void Register(RenderFeatureSetId featureSet, params IRenderFeature[] features)
    {
        ArgumentNullException.ThrowIfNull(features);
        if (features.Length == 0) return;

        if (!_features.TryGetValue(featureSet, out HashSet<IRenderFeature>? membership))
        {
            membership = new HashSet<IRenderFeature>(ReferenceEqualityComparer.Instance);
            _features.Add(featureSet, membership);
        }

        foreach (IRenderFeature feature in features)
        {
            ArgumentNullException.ThrowIfNull(feature);
            membership.Add(feature);
        }
    }

    public IReadOnlyDictionary<RenderFeatureSetId, CompiledRenderFeatureSet> Build()
    {
        var result = new Dictionary<RenderFeatureSetId, CompiledRenderFeatureSet>(_features.Count);
        foreach ((RenderFeatureSetId id, HashSet<IRenderFeature> membership) in _features)
        {
            var snapshot = new HashSet<IRenderFeature>(membership, ReferenceEqualityComparer.Instance);
            result.Add(id, new CompiledRenderFeatureSet(id, new ReadOnlySet<IRenderFeature>(snapshot)));
        }

        return new ReadOnlyDictionary<RenderFeatureSetId, CompiledRenderFeatureSet>(result);
    }

    private sealed class ReadOnlySet<T>(ISet<T> source) : IReadOnlySet<T>
    {
        public int Count => source.Count;
        public bool Contains(T item) => source.Contains(item);
        public bool IsProperSubsetOf(IEnumerable<T> other) => source.IsProperSubsetOf(other);
        public bool IsProperSupersetOf(IEnumerable<T> other) => source.IsProperSupersetOf(other);
        public bool IsSubsetOf(IEnumerable<T> other) => source.IsSubsetOf(other);
        public bool IsSupersetOf(IEnumerable<T> other) => source.IsSupersetOf(other);
        public bool Overlaps(IEnumerable<T> other) => source.Overlaps(other);
        public bool SetEquals(IEnumerable<T> other) => source.SetEquals(other);
        public IEnumerator<T> GetEnumerator() => source.GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}

public sealed class RenderFeatureSetOrder : IReadOnlyList<RenderFeatureSetId>
{
    private readonly List<RenderFeatureSetId> _items = [];
    private readonly HashSet<RenderFeatureSetId> _membership = [];
    private bool _sealed;

    public int Count => _items.Count;
    public RenderFeatureSetId this[int index] => _items[index];

    public RenderFeatureSetOrder Add(RenderFeatureSetId id)
    {
        EnsureMutable();
        if (_membership.Add(id)) _items.Add(id);
        return this;
    }

    public RenderFeatureSetOrder InsertAfter(RenderFeatureSetId anchor, RenderFeatureSetId id)
    {
        EnsureMutable();
        if (!_membership.Add(id)) return this;
        int index = _items.IndexOf(anchor);
        if (index < 0) _items.Add(id);
        else _items.Insert(index + 1, id);
        return this;
    }

    public RenderFeatureSetOrder InsertBefore(RenderFeatureSetId anchor, RenderFeatureSetId id)
    {
        EnsureMutable();
        if (!_membership.Add(id)) return this;
        int index = _items.IndexOf(anchor);
        if (index < 0) _items.Add(id);
        else _items.Insert(index, id);
        return this;
    }

    public void Seal() => _sealed = true;
    public IEnumerator<RenderFeatureSetId> GetEnumerator() => _items.GetEnumerator();
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

    private void EnsureMutable()
    {
        if (_sealed) throw new InvalidOperationException("The render feature set order is sealed.");
    }
}
