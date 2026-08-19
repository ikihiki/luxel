using System.Collections.ObjectModel;
using System.Globalization;

namespace Luxel.ValueDocument;

public readonly record struct NodeId(long Value)
{
    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
}

public enum ValueNodeKind
{
    Object,
    Array,
    Scalar,
}

public enum ValueScalarKind
{
    Null,
    Boolean,
    String,
    Number,
}

public abstract class ValueNode
{
    protected ValueNode(NodeId id) => Id = id;

    public NodeId Id { get; }
    public abstract ValueNodeKind Kind { get; }
}

public sealed record ValueProperty
{
    public ValueProperty(string name, ValueNode value)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(value);
        Name = name;
        Value = value;
    }

    public string Name { get; }
    public ValueNode Value { get; }
}

public sealed class ValueObjectNode : ValueNode
{
    private readonly ReadOnlyCollection<ValueProperty> _properties;

    public ValueObjectNode(NodeId id, IEnumerable<ValueProperty> properties) : base(id)
    {
        ArgumentNullException.ThrowIfNull(properties);
        ValueProperty[] copy = properties.ToArray();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (ValueProperty property in copy)
        {
            if (!names.Add(property.Name))
                throw new ArgumentException($"Duplicate object property '{property.Name}'.", nameof(properties));
        }
        _properties = Array.AsReadOnly(copy);
    }

    public override ValueNodeKind Kind => ValueNodeKind.Object;
    public IReadOnlyList<ValueProperty> Properties => _properties;

    public bool TryGetProperty(string name, out ValueNode? value)
    {
        foreach (ValueProperty property in _properties)
        {
            if (string.Equals(property.Name, name, StringComparison.Ordinal))
            {
                value = property.Value;
                return true;
            }
        }
        value = null;
        return false;
    }
}

public sealed class ValueArrayNode : ValueNode
{
    private readonly ReadOnlyCollection<ValueNode> _items;

    public ValueArrayNode(NodeId id, IEnumerable<ValueNode> items) : base(id)
    {
        ArgumentNullException.ThrowIfNull(items);
        _items = Array.AsReadOnly(items.ToArray());
    }

    public override ValueNodeKind Kind => ValueNodeKind.Array;
    public IReadOnlyList<ValueNode> Items => _items;
}

public sealed class ValueScalarNode : ValueNode
{
    private ValueScalarNode(NodeId id, ValueScalarKind scalarKind, string? text, bool boolean) : base(id)
    {
        ScalarKind = scalarKind;
        Text = text;
        Boolean = boolean;
    }

    public override ValueNodeKind Kind => ValueNodeKind.Scalar;
    public ValueScalarKind ScalarKind { get; }
    public string? Text { get; }
    public bool Boolean { get; }
    public string NumberLexeme => ScalarKind == ValueScalarKind.Number
        ? Text!
        : throw new InvalidOperationException("The scalar is not a JSON number.");

    public static ValueScalarNode Null(NodeId id) => new(id, ValueScalarKind.Null, null, false);
    public static ValueScalarNode FromBoolean(NodeId id, bool value) => new(id, ValueScalarKind.Boolean, null, value);
    public static ValueScalarNode FromString(NodeId id, string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new(id, ValueScalarKind.String, value, false);
    }
    public static ValueScalarNode FromNumber(NodeId id, string lexeme)
    {
        ArgumentException.ThrowIfNullOrEmpty(lexeme);
        if (!JsonValueCodec.IsValidNumberLexeme(lexeme))
            throw new ArgumentException("The value is not a valid JSON number lexeme.", nameof(lexeme));
        return new(id, ValueScalarKind.Number, lexeme, false);
    }
}

public sealed class ValueNodeFactory
{
    private static long s_nextId;

    public NodeId NextId() => new(Interlocked.Increment(ref s_nextId));
    public ValueObjectNode Object(IEnumerable<ValueProperty> properties) => new(NextId(), properties);
    public ValueArrayNode Array(IEnumerable<ValueNode> items) => new(NextId(), items);
    public ValueScalarNode Null() => ValueScalarNode.Null(NextId());
    public ValueScalarNode Boolean(bool value) => ValueScalarNode.FromBoolean(NextId(), value);
    public ValueScalarNode String(string value) => ValueScalarNode.FromString(NextId(), value);
    public ValueScalarNode Number(string lexeme) => ValueScalarNode.FromNumber(NextId(), lexeme);
}
