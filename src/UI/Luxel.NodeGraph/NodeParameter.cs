namespace Luxel.NodeGraph;

/// <summary>
/// Immutable, JSON-compatible parameter values stored in <see cref="GraphNode.Data"/>. A node can expose
/// several typed parameters without making the node-graph core depend on a domain-specific payload type.
/// </summary>
public sealed class NodeParameterValues : IEquatable<NodeParameterValues>
{
    private readonly IReadOnlyDictionary<string, object?> _values;

    public static NodeParameterValues Empty { get; } = new(new Dictionary<string, object?>(StringComparer.Ordinal));

    public NodeParameterValues(IReadOnlyDictionary<string, object?> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        _values = new Dictionary<string, object?>(values, StringComparer.Ordinal);
    }

    public IReadOnlyDictionary<string, object?> Values => _values;

    public bool TryGetValue(string key, out object? value) => _values.TryGetValue(key, out value);

    public NodeParameterValues Set(string key, object? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        var values = new Dictionary<string, object?>(_values, StringComparer.Ordinal) { [key] = value };
        return new NodeParameterValues(values);
    }

    public bool Equals(NodeParameterValues? other)
    {
        if (ReferenceEquals(this, other)) return true;
        if (other is null || _values.Count != other._values.Count) return false;
        foreach ((string key, object? value) in _values)
            if (!other._values.TryGetValue(key, out object? candidate) || !Equals(value, candidate)) return false;
        return true;
    }

    public override bool Equals(object? obj) => obj is NodeParameterValues other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach ((string key, object? value) in _values.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            hash.Add(key, StringComparer.Ordinal);
            hash.Add(value);
        }
        return hash.ToHashCode();
    }
}

/// <summary>Untyped metadata carried by <see cref="NodeInlineDecoration"/>.</summary>
public abstract record NodeParameter(string Key, Type ValueType)
{
    internal abstract object? ReadBoxed(GraphNode node);
    internal abstract GraphChange SetBoxed(int nodeId, object? value);

    public static NodeParameter<T> Create<T>(string key, T defaultValue = default!) => new(key, defaultValue);
}

/// <summary>
/// Typed node parameter. Values are read from and written to the node's standard <see cref="NodeParameterValues"/>
/// payload, so edits participate in the graph document's history, dirty state, multi-view synchronization, and JSON.
/// </summary>
public sealed record NodeParameter<T> : NodeParameter
{
    public NodeParameter(string key, T defaultValue = default!) : base(key, typeof(T))
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        DefaultValue = defaultValue;
    }

    public T DefaultValue { get; }

    public T Read(GraphNode node)
    {
        if (node.Data is not NodeParameterValues values || !values.TryGetValue(Key, out object? value))
            return DefaultValue;
        if (value is null)
        {
            if (default(T) is null) return (T)value!;
            throw new InvalidOperationException($"Node parameter '{Key}' contains null, not {typeof(T).Name}.");
        }
        if (value is T typed) return typed;
        throw new InvalidOperationException($"Node parameter '{Key}' contains {value.GetType().Name}, not {typeof(T).Name}.");
    }

    public SetNodeParameter<T> Set(int nodeId, T value) => new(nodeId, this, value);

    internal override object? ReadBoxed(GraphNode node) => Read(node);

    internal override GraphChange SetBoxed(int nodeId, object? value)
    {
        if (value is T typed) return Set(nodeId, typed);
        if (value is null && default(T) is null) return Set(nodeId, (T)value!);
        throw new ArgumentException($"Node parameter '{Key}' expects {typeof(T).Name}.", nameof(value));
    }
}
