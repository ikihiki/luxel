using System.Globalization;

namespace Luxel.ValueDocument;

public sealed record ValueTreeRow(
    int Depth,
    string Pointer,
    string? Key,
    int? Index,
    NodeId NodeId,
    ValueNodeKind Kind,
    string ValueSummary,
    bool IsExpanded,
    bool HasChildren);

/// <summary>Caller-owned expansion/selection projection for tree-mode editors.</summary>
public sealed class ValueTreeController
{
    private readonly HashSet<NodeId> _expanded = [];

    public ValueTreeController(ValueDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        Document = document;
        _expanded.Add(document.AcceptedRoot.Id);
    }

    public ValueDocument Document { get; }
    public ISet<NodeId> Expanded => _expanded;
    public NodeId? SelectedNodeId => Document.Selection.NodeId;
    public bool IsReadOnly => Document.RawDraft?.IsDirty == true;

    public bool IsExpanded(NodeId id) => _expanded.Contains(id);
    public void SetExpanded(NodeId id, bool expanded)
    {
        if (expanded) _expanded.Add(id);
        else _expanded.Remove(id);
    }
    public void ToggleExpanded(NodeId id)
    {
        if (!_expanded.Remove(id)) _expanded.Add(id);
    }

    /// <summary>Enumerates visible rows, stopping after <paramref name="maxRows"/>.</summary>
    public IReadOnlyList<ValueTreeRow> EnumerateRows(int maxRows = 1_000, int arrayItemLimit = 256)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxRows);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(arrayItemLimit);
        var rows = new List<ValueTreeRow>(Math.Min(maxRows, 256));
        AppendVisible(Document.AcceptedRoot, string.Empty, null, null, 0, maxRows, arrayItemLimit, rows);
        return rows;
    }

    /// <summary>Enumerates a bounded chunk of an array's direct children.</summary>
    public IReadOnlyList<ValueTreeRow> EnumerateArrayRows(NodeId arrayId, int offset, int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (!Document.TryGetNode(arrayId, out ValueNode? node) || node is not ValueArrayNode array)
            return Array.Empty<ValueTreeRow>();
        string parent = Document.PointerOf(arrayId)!;
        int end = Math.Min(array.Items.Count, checked(offset + count));
        var rows = new List<ValueTreeRow>(Math.Max(0, end - offset));
        for (int i = offset; i < end; i++)
        {
            ValueNode item = array.Items[i];
            rows.Add(CreateRow(item, 0, parent + "/" + i.ToString(CultureInfo.InvariantCulture), null, i));
        }
        return rows;
    }

    public ValueApplyResult ReplaceScalar(NodeId id, ValueScalarNode value) => Document.ReplaceScalar(id, value);
    public ValueApplyResult AddObjectProperty(NodeId id, string name, ValueNode value) => Document.AddObjectProperty(id, name, value);
    public ValueApplyResult RemoveObjectProperty(NodeId id, string name) => Document.RemoveObjectProperty(id, name);
    public ValueApplyResult RenameObjectProperty(NodeId id, string oldName, string newName) => Document.RenameObjectProperty(id, oldName, newName);
    public ValueApplyResult InsertArrayItem(NodeId id, int index, ValueNode value) => Document.InsertArrayItem(id, index, value);
    public ValueApplyResult RemoveArrayItem(NodeId id, int index) => Document.RemoveArrayItem(id, index);
    public ValueApplyResult MoveArrayItem(NodeId id, int fromIndex, int toIndex) => Document.MoveArrayItem(id, fromIndex, toIndex);

    private void AppendVisible(ValueNode node, string pointer, string? key, int? index, int depth,
        int maxRows, int arrayItemLimit, List<ValueTreeRow> rows)
    {
        if (rows.Count >= maxRows) return;
        ValueTreeRow row = CreateRow(node, depth, pointer, key, index);
        rows.Add(row);
        if (!row.IsExpanded || rows.Count >= maxRows) return;

        if (node is ValueObjectNode obj)
        {
            foreach (ValueProperty property in obj.Properties)
            {
                AppendVisible(property.Value, pointer + "/" + JsonPointer.Escape(property.Name), property.Name, null,
                    depth + 1, maxRows, arrayItemLimit, rows);
                if (rows.Count >= maxRows) break;
            }
        }
        else if (node is ValueArrayNode array)
        {
            int count = Math.Min(array.Items.Count, arrayItemLimit);
            for (int i = 0; i < count && rows.Count < maxRows; i++)
                AppendVisible(array.Items[i], pointer + "/" + i.ToString(CultureInfo.InvariantCulture), null, i,
                    depth + 1, maxRows, arrayItemLimit, rows);
        }
    }

    private ValueTreeRow CreateRow(ValueNode node, int depth, string pointer, string? key, int? index)
    {
        bool hasChildren = node switch
        {
            ValueObjectNode obj => obj.Properties.Count > 0,
            ValueArrayNode array => array.Items.Count > 0,
            _ => false,
        };
        return new ValueTreeRow(depth, pointer, key, index, node.Id, node.Kind, Summary(node),
            hasChildren && _expanded.Contains(node.Id), hasChildren);
    }

    public static string Summary(ValueNode node) => node switch
    {
        ValueObjectNode obj => $"{{{obj.Properties.Count}}}",
        ValueArrayNode array => $"[{array.Items.Count}]",
        ValueScalarNode { ScalarKind: ValueScalarKind.Null } => "null",
        ValueScalarNode { ScalarKind: ValueScalarKind.Boolean } scalar => scalar.Boolean ? "true" : "false",
        ValueScalarNode { ScalarKind: ValueScalarKind.String } scalar => JsonValueCodec.Serialize(scalar),
        ValueScalarNode scalar => scalar.NumberLexeme,
        _ => string.Empty,
    };
}
