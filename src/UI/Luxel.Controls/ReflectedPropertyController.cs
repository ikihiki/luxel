using System.Globalization;
using System.Numerics;
using System.Reflection;
using Luxel.UI;
using Luxel.ValueDocument;

namespace Luxel.Controls;

/// <summary>A reflected member exposed through a stable descriptor and a <see cref="ValueDocument.ValueDocument"/> transaction.</summary>
public sealed class ReflectedPropertyMember
{
    internal ReflectedPropertyMember(PropertyRow row, MemberInfo member, ValueDescriptor descriptor)
    {
        Row = row;
        Member = member;
        Descriptor = descriptor;
    }

    internal PropertyRow Row { get; }
    internal MemberInfo Member { get; }
    public ValueDescriptor Descriptor { get; }
    public string Name => Row.Name;
    public string Group => Row.Group;
    public Type Type => Row.Type;
    public float? RangeMin => Row.RangeMin;
    public float? RangeMax => Row.RangeMax;
}

/// <summary>Uncommitted editor text and its current validation/adapter diagnostic.</summary>
public sealed record PropertyValueDraft(string Text, ValueDiagnostic? Diagnostic = null);

/// <summary>
/// Reflection compatibility adapter for scalar PropertyGrid editing. The controller owns the accepted
/// document and transaction history, so rebuilding a view does not lose undo/redo state.
/// </summary>
public sealed class ReflectedPropertyController
{
    private readonly object _target;
    private readonly Dictionary<DescriptorId, ReflectedPropertyMember> _byId;
    private readonly Dictionary<string, ReflectedPropertyMember> _byName;
    private readonly Dictionary<DescriptorId, PropertyValueDraft> _drafts = [];
    private readonly ValueNodeFactory _nodes = new();

    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode(
        "ReflectedPropertyController discovers arbitrary runtime members. Native AOT callers should use generated/static property descriptors instead.")]
    public ReflectedPropertyController(object target)
    {
        ArgumentNullException.ThrowIfNull(target);
        _target = target;
        Members = DiscoverMembers(target);
        _byId = Members.ToDictionary(m => m.Descriptor.Id);
        _byName = Members.ToDictionary(m => m.Name, StringComparer.Ordinal);
        Document = new ValueDocument.ValueDocument(CreateRoot(), ApplyCandidate);
    }

    public object Target => _target;
    public IReadOnlyList<ReflectedPropertyMember> Members { get; }
    public ValueDocument.ValueDocument Document { get; }
    public bool CanUndo => Document.History.CanUndo;
    public bool CanRedo => Document.History.CanRedo;

    public PropertyValueDraft? DraftOf(DescriptorId id) => _drafts.GetValueOrDefault(id);
    public PropertyValueDraft? DraftOf(string memberName)
        => _byName.TryGetValue(memberName, out ReflectedPropertyMember? member) ? DraftOf(member.Descriptor.Id) : null;

    public object? AcceptedValue(DescriptorId id)
    {
        ReflectedPropertyMember member = _byId[id];
        ValueScalarNode node = FindNode(Document.AcceptedRoot, id);
        return Decode(node, member.Type);
    }

    public object? AcceptedValue(string memberName) => AcceptedValue(_byName[memberName].Descriptor.Id);

    public void SetDraft(DescriptorId id, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        ReflectedPropertyMember member = _byId[id];
        _drafts[id] = TryParseDraft(text, member.Type, out _)
            ? new PropertyValueDraft(text)
            : new PropertyValueDraft(text, Diagnostic($"'{text}' is not a valid {member.Type.Name} value.", id));
    }

    public void SetDraft(string memberName, string text) => SetDraft(_byName[memberName].Descriptor.Id, text);

    public ValueApplyResult CommitDraft(DescriptorId id)
    {
        ReflectedPropertyMember member = _byId[id];
        if (!_drafts.TryGetValue(id, out PropertyValueDraft? draft))
            return new ValueApplyResult(ValueApplyStatus.NoDraft);
        if (!TryParseDraft(draft.Text, member.Type, out object? value))
        {
            _drafts[id] = draft with { Diagnostic = Diagnostic($"'{draft.Text}' is not a valid {member.Type.Name} value.", id) };
            return new ValueApplyResult(ValueApplyStatus.ParseFailed);
        }
        return CommitValue(id, value);
    }

    public ValueApplyResult CommitDraft(string memberName) => CommitDraft(_byName[memberName].Descriptor.Id);
    public ValueApplyResult CommitValue(string memberName, object? value) => CommitValue(_byName[memberName].Descriptor.Id, value);

    public ValueApplyResult CommitValue(DescriptorId id, object? value)
    {
        ReflectedPropertyMember member = _byId[id];
        if (!IsAcceptedType(member.Type, value))
        {
            _drafts[id] = new PropertyValueDraft(value?.ToString() ?? string.Empty,
                Diagnostic($"The value is not assignable to {member.Type.Name}.", id));
            return new ValueApplyResult(ValueApplyStatus.ParseFailed);
        }

        if (Equals(AcceptedValue(id), value))
        {
            _drafts.Remove(id);
            return new ValueApplyResult(ValueApplyStatus.Accepted);
        }

        ValueObjectNode root = (ValueObjectNode)Document.AcceptedRoot;
        var properties = root.Properties.Select(p => p.Name == id.Value
            ? new ValueProperty(p.Name, Encode(value, member.Type))
            : p).ToArray();
        var candidate = _nodes.Object(properties);
        ValueApplyResult result = Document.ReplaceRoot(candidate, ValueTransactionOrigin.Property);
        if (result.Success) _drafts.Remove(id);
        else _drafts[id] = new PropertyValueDraft(value?.ToString() ?? string.Empty, Document.Diagnostics.FirstOrDefault());
        return result;
    }

    public ValueApplyResult Undo()
    {
        ValueApplyResult result = Document.Undo();
        if (result.Success) _drafts.Clear();
        return result;
    }

    public ValueApplyResult Redo()
    {
        ValueApplyResult result = Document.Redo();
        if (result.Success) _drafts.Clear();
        return result;
    }

    /// <summary>Accept externally changed target values and reset transaction history.</summary>
    public void RefreshFromTarget()
    {
        ValueObjectNode root = CreateRoot();
        if (string.Equals(JsonValueCodec.Serialize(root), JsonValueCodec.Serialize(Document.AcceptedRoot), StringComparison.Ordinal))
            return;
        Document.RefreshExternal(root, null);
        _drafts.Clear();
    }

    internal static List<(PropertyRow Row, MemberInfo Member)> DiscoverRows(object target)
    {
        var rows = new List<(PropertyRow, MemberInfo)>();
        Type type = target.GetType();
        var members = new List<MemberInfo>();
        members.AddRange(type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p is { CanRead: true, CanWrite: true } && p.GetIndexParameters().Length == 0)
            .OrderBy(p => p.MetadataToken));
        members.AddRange(type.GetFields(BindingFlags.Public | BindingFlags.Instance)
            .Where(f => !f.IsInitOnly).OrderBy(f => f.MetadataToken));

        foreach (MemberInfo member in members)
        {
            if (member.GetCustomAttribute<PropertyIgnoreAttribute>() is not null) continue;
            Type memberType = member is PropertyInfo property ? property.PropertyType : ((FieldInfo)member).FieldType;
            if (!PropertyGrid.Supported(memberType)) continue;
            PropertyRangeAttribute? range = member.GetCustomAttribute<PropertyRangeAttribute>();
            string group = member.GetCustomAttribute<PropertyGroupAttribute>()?.Name ?? string.Empty;
            Func<object?> get = member is PropertyInfo getProperty
                ? () => getProperty.GetValue(target)
                : () => ((FieldInfo)member).GetValue(target);
            Action<object?> set = member is PropertyInfo setProperty
                ? value => setProperty.SetValue(target, value)
                : value => ((FieldInfo)member).SetValue(target, value);
            rows.Add((new PropertyRow(member.Name, group, memberType, get, set, range?.Min, range?.Max), member));
        }
        return rows;
    }

    private IReadOnlyList<ReflectedPropertyMember> DiscoverMembers(object target)
    {
        int order = 0;
        return DiscoverRows(target).Select(pair =>
        {
            MemberInfo member = pair.Member;
            Type declaringType = member.DeclaringType ?? target.GetType();
            string declaringName = declaringType.FullName ?? declaringType.Name;
            var key = new MemberKey(declaringName, member.Name);
            var id = new DescriptorId($"{declaringName}::{member.Name}");
            ValueEditorKind kind = EditorKind(pair.Row.Type);
            ValueNumericConstraint? numeric = pair.Row.RangeMin is { } min && pair.Row.RangeMax is { } max
                ? new ValueNumericConstraint((decimal)min, (decimal)max)
                : null;
            IEnumerable<ValueDescriptorOption>? options = pair.Row.Type.IsEnum
                ? Enum.GetNames(pair.Row.Type).Select(name => new ValueDescriptorOption(name, name))
                : null;
            var descriptor = new ValueDescriptor(id, ValueShape.Scalar, kind, key, pair.Row.Type,
                pair.Row.Name, group: pair.Row.Group, order: order++, numeric: numeric, options: options);
            return new ReflectedPropertyMember(pair.Row, member, descriptor);
        }).ToArray();
    }

    private ValueObjectNode CreateRoot()
        => _nodes.Object(Members.Select(member => new ValueProperty(member.Descriptor.Id.Value,
            Encode(member.Row.Get(), member.Type))));

    private ValueCommitResult ApplyCandidate(ValueNode candidate, ValueCommitContext _)
    {
        if (candidate is not ValueObjectNode after || Document.AcceptedRoot is not ValueObjectNode before)
            return ValueCommitResult.Rejected(Diagnostic("The reflected property document must remain an object.", null));

        ReflectedPropertyMember? changed = null;
        ValueScalarNode? changedNode = null;
        foreach (ReflectedPropertyMember member in Members)
        {
            ValueScalarNode oldNode = FindNode(before, member.Descriptor.Id);
            ValueScalarNode newNode = FindNode(after, member.Descriptor.Id);
            if (ScalarEquals(oldNode, newNode)) continue;
            if (changed is not null)
                return ValueCommitResult.Rejected(Diagnostic("A property transaction may change only one member.", null));
            changed = member;
            changedNode = newNode;
        }
        if (changed is null) return ValueCommitResult.Accepted();

        object? oldValue = changed.Row.Get();
        try
        {
            changed.Row.Set(Decode(changedNode!, changed.Type));
            return ValueCommitResult.Accepted();
        }
        catch (Exception exception)
        {
            string message = exception is TargetInvocationException { InnerException: { } inner } ? inner.Message : exception.Message;
            try
            {
                if (!Equals(changed.Row.Get(), oldValue)) changed.Row.Set(oldValue);
            }
            catch (Exception rollback)
            {
                message += $" Rollback also failed: {rollback.Message}";
            }
            return ValueCommitResult.Rejected(Diagnostic(message, changed.Descriptor.Id));
        }
    }

    private static ValueScalarNode FindNode(ValueNode root, DescriptorId id)
    {
        if (root is ValueObjectNode obj && obj.TryGetProperty(id.Value, out ValueNode? node) && node is ValueScalarNode scalar)
            return scalar;
        throw new InvalidOperationException($"Missing reflected property node '{id.Value}'.");
    }

    private ValueScalarNode Encode(object? value, Type type)
    {
        if (type == typeof(bool)) return _nodes.Boolean((bool)value!);
        string text = type switch
        {
            _ when type == typeof(float) => ((float)value!).ToString("R", CultureInfo.InvariantCulture),
            _ when type == typeof(Vector2) => Format((Vector2)value!),
            _ when type == typeof(Vector3) => Format((Vector3)value!),
            _ when type.IsEnum => value!.ToString()!,
            _ => value?.ToString() ?? string.Empty,
        };
        return _nodes.String(text);
    }

    private static object? Decode(ValueScalarNode node, Type type)
    {
        if (type == typeof(bool)) return node.Boolean;
        string text = node.Text ?? string.Empty;
        if (type == typeof(string)) return text;
        if (type == typeof(int)) return int.Parse(text, CultureInfo.InvariantCulture);
        if (type == typeof(float)) return float.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
        if (type == typeof(uint)) return uint.Parse(text, CultureInfo.InvariantCulture);
        if (type.IsEnum) return Enum.Parse(type, text);
        if (type == typeof(Length)) return Length.Parse(text, CultureInfo.InvariantCulture);
        string[] parts = text.Split(',');
        if (type == typeof(Vector2)) return new Vector2(ParseFloat(parts[0]), ParseFloat(parts[1]));
        if (type == typeof(Vector3)) return new Vector3(ParseFloat(parts[0]), ParseFloat(parts[1]), ParseFloat(parts[2]));
        throw new NotSupportedException(type.FullName);
    }

    private static bool TryParseDraft(string text, Type type, out object? value)
    {
        value = null;
        if (type == typeof(string)) { value = text; return true; }
        if (type == typeof(int) && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int i)) { value = i; return true; }
        if (type == typeof(float) && TryParseFloatDraft(text, out float f)) { value = f; return true; }
        if (type == typeof(uint) && uint.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out uint u)) { value = u; return true; }
        if (type == typeof(bool) && bool.TryParse(text, out bool b)) { value = b; return true; }
        if (type == typeof(Length) && Length.TryParse(text, CultureInfo.InvariantCulture, out Length length)) { value = length; return true; }
        if (type.IsEnum && Enum.TryParse(type, text, out object? parsed)) { value = parsed; return true; }
        string[] parts = text.Split(',');
        if (type == typeof(Vector2) && parts.Length == 2
            && TryParseFloatDraft(parts[0], out float x2) && TryParseFloatDraft(parts[1], out float y2))
        {
            value = new Vector2(x2, y2);
            return true;
        }
        if (type == typeof(Vector3) && parts.Length == 3
            && TryParseFloatDraft(parts[0], out float x3) && TryParseFloatDraft(parts[1], out float y3)
            && TryParseFloatDraft(parts[2], out float z3))
        {
            value = new Vector3(x3, y3, z3);
            return true;
        }
        return false;
    }

    internal static bool TryParseFloatDraft(string text, out float value)
    {
        value = default;
        return !string.IsNullOrEmpty(text) && text[^1] != '.'
            && float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private static bool IsAcceptedType(Type type, object? value) => value is not null && value.GetType() == type;
    private static float ParseFloat(string text) => float.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
    private static string Format(Vector2 value) => $"{value.X.ToString("R", CultureInfo.InvariantCulture)},{value.Y.ToString("R", CultureInfo.InvariantCulture)}";
    private static string Format(Vector3 value) => $"{value.X.ToString("R", CultureInfo.InvariantCulture)},{value.Y.ToString("R", CultureInfo.InvariantCulture)},{value.Z.ToString("R", CultureInfo.InvariantCulture)}";
    private static bool ScalarEquals(ValueScalarNode left, ValueScalarNode right)
        => left.ScalarKind == right.ScalarKind && left.Text == right.Text && left.Boolean == right.Boolean;

    private static ValueEditorKind EditorKind(Type type) =>
        type == typeof(bool) ? ValueEditorKind.Boolean :
        type.IsEnum ? ValueEditorKind.Enum :
        type == typeof(uint) ? ValueEditorKind.Color :
        type == typeof(Vector2) || type == typeof(Vector3) ? ValueEditorKind.Vector :
        type == typeof(Length) ? ValueEditorKind.Length :
        type == typeof(int) || type == typeof(float) ? ValueEditorKind.Number :
        ValueEditorKind.Text;

    private static ValueDiagnostic Diagnostic(string message, DescriptorId? id)
        => new(message, ValueDiagnosticSeverity.Error, 0, 1, 1, id is null ? null : "/" + id.Value.Value, "property");
}
