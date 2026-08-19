using System.Collections.ObjectModel;

namespace Luxel.ValueDocument;

public readonly record struct DescriptorId(string Value);
public readonly record struct MemberKey(string DeclaringType, string Name);

public enum ValueShape
{
    Any,
    Object,
    Array,
    Scalar,
}

public enum ValueEditorKind
{
    Default,
    Json,
    Text,
    Boolean,
    Number,
    Enum,
    Color,
    Vector,
    Length,
    Asset,
    Reference,
}

public sealed record ValueDescriptorOption(string Label, string Value);
public sealed record ValueNumericConstraint(decimal? Minimum = null, decimal? Maximum = null, decimal? Step = null);

public sealed class ValueDescriptor
{
    public ValueDescriptor(
        DescriptorId id,
        ValueShape shape,
        ValueEditorKind editorKind = ValueEditorKind.Default,
        MemberKey? memberKey = null,
        Type? clrType = null,
        string? displayName = null,
        string? description = null,
        string? group = null,
        int order = 0,
        bool nullable = false,
        bool isReadOnly = false,
        bool isHidden = false,
        bool isDeprecated = false,
        ValueNumericConstraint? numeric = null,
        IEnumerable<ValueDescriptorOption>? options = null,
        ValueNode? defaultValue = null,
        string? codecId = null,
        string? schemaReference = null,
        IEnumerable<string>? validatorIds = null,
        IReadOnlyDictionary<string, string>? annotations = null,
        IEnumerable<ValueDescriptor>? children = null,
        ValueDescriptor? itemDescriptor = null)
    {
        Id = id;
        Shape = shape;
        EditorKind = editorKind;
        MemberKey = memberKey;
        ClrType = clrType;
        DisplayName = displayName;
        Description = description;
        Group = group;
        Order = order;
        Nullable = nullable;
        IsReadOnly = isReadOnly;
        IsHidden = isHidden;
        IsDeprecated = isDeprecated;
        Numeric = numeric;
        Options = Array.AsReadOnly((options ?? []).ToArray());
        DefaultValue = defaultValue;
        CodecId = codecId;
        SchemaReference = schemaReference;
        ValidatorIds = Array.AsReadOnly((validatorIds ?? []).ToArray());
        Annotations = new ReadOnlyDictionary<string, string>(
            new Dictionary<string, string>(annotations ?? new Dictionary<string, string>(), StringComparer.Ordinal));
        Children = Array.AsReadOnly((children ?? []).ToArray());
        ItemDescriptor = itemDescriptor;
    }

    public DescriptorId Id { get; }
    public ValueShape Shape { get; }
    public ValueEditorKind EditorKind { get; }
    public MemberKey? MemberKey { get; }
    public Type? ClrType { get; }
    public string? DisplayName { get; }
    public string? Description { get; }
    public string? Group { get; }
    public int Order { get; }
    public bool Nullable { get; }
    public bool IsReadOnly { get; }
    public bool IsHidden { get; }
    public bool IsDeprecated { get; }
    public ValueNumericConstraint? Numeric { get; }
    public IReadOnlyList<ValueDescriptorOption> Options { get; }
    public ValueNode? DefaultValue { get; }
    public string? CodecId { get; }
    public string? SchemaReference { get; }
    public IReadOnlyList<string> ValidatorIds { get; }
    public IReadOnlyDictionary<string, string> Annotations { get; }
    public IReadOnlyList<ValueDescriptor> Children { get; }
    public ValueDescriptor? ItemDescriptor { get; }
}
