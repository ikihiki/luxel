using Luxel.Typography;
using Luxel.UI;
using Luxel.ValueDocument;
using static Luxel.Controls.Kit;

namespace Luxel.Controls;

/// <summary>Minimal editable native projection of a JSON <see cref="ValueDocument.ValueDocument"/>.</summary>
public sealed class JsonTreeView : CompositeControl
{
    private readonly float _width;
    private readonly Signal<int> _version = new(0);
    private readonly Dictionary<NodeId, Signal<string>> _scalarDrafts = [];

    public JsonTreeView(ValueDocument.ValueDocument document, float width = 240f)
        : this(new ValueTreeController(document ?? throw new ArgumentNullException(nameof(document))), width)
    {
    }

    public JsonTreeView(ValueTreeController controller, float width = 240f)
    {
        Controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _width = MathF.Max(120, width);
    }

    public ValueTreeController Controller { get; }
    public ValueDocument.ValueDocument Document => Controller.Document;
    public bool IsReadOnly => Controller.IsReadOnly;

    public ValueApplyResult EditScalar(NodeId id, string jsonScalar)
    {
        JsonValueParseResult parsed = JsonValueCodec.Parse(jsonScalar);
        if (!parsed.Success || parsed.Root is not ValueScalarNode scalar)
            return new ValueApplyResult(ValueApplyStatus.ParseFailed);
        return RefreshAfter(Controller.ReplaceScalar(id, scalar));
    }

    public ValueApplyResult AddObjectProperty(NodeId id, string name, ValueNode value)
        => RefreshAfter(Controller.AddObjectProperty(id, name, value));
    public ValueApplyResult RemoveObjectProperty(NodeId id, string name)
        => RefreshAfter(Controller.RemoveObjectProperty(id, name));
    public ValueApplyResult RenameObjectProperty(NodeId id, string oldName, string newName)
        => RefreshAfter(Controller.RenameObjectProperty(id, oldName, newName));
    public ValueApplyResult InsertArrayItem(NodeId id, int index, ValueNode value)
        => RefreshAfter(Controller.InsertArrayItem(id, index, value));
    public ValueApplyResult RemoveArrayItem(NodeId id, int index)
        => RefreshAfter(Controller.RemoveArrayItem(id, index));
    public ValueApplyResult MoveArrayItem(NodeId id, int fromIndex, int toIndex)
        => RefreshAfter(Controller.MoveArrayItem(id, fromIndex, toIndex));
    public ValueApplyResult Undo() => RefreshAfter(Document.Undo());
    public ValueApplyResult Redo() => RefreshAfter(Document.Redo());

    public void Refresh() => _version.Value++;

    protected override Widget Build()
    {
        _ = _version.Value;
        var widgets = new List<Widget>
        {
            HStack(4)[
                Button(_ => Undo(), "Undo", fontSize: 10),
                Button(_ => Redo(), "Redo", fontSize: 10)],
        };
        if (IsReadOnly)
            widgets.Add(Text("Unapplied raw draft: Tree is read-only.", 10,
                color: Bind.From(() => UiTheme.T.Warning), width: _width, wrap: TextWrap.Word));

        foreach (ValueTreeRow row in Controller.EnumerateRows()) widgets.Add(BuildRow(row));
        return VStack(3, width: _width)[widgets.ToArray()];
    }

    private Widget BuildRow(ValueTreeRow row)
    {
        float indent = row.Depth * 14;
        string label = row.Key ?? (row.Index?.ToString() ?? "$root");
        Widget prefix = row.HasChildren
            ? Button(_ => { Controller.ToggleExpanded(row.NodeId); _version.Value++; }, row.IsExpanded ? "▾" : "▸", fontSize: 10)
            : Text(" ", 10, width: 22);

        if (row.Kind != ValueNodeKind.Scalar)
            return HStack(4, width: _width)[
                Border(width: indent), prefix,
                Text($"{label}: {row.ValueSummary}", 11, color: Bind.From(() => UiTheme.T.Text))];

        if (IsReadOnly)
            return HStack(4, width: _width)[
                Border(width: indent), prefix,
                Text($"{label}: {row.ValueSummary}", 11, color: Bind.From(() => UiTheme.T.TextMuted))];

        Signal<string> draft = ScalarDraft(row.NodeId, row.ValueSummary);
        TextField field = TextField(draft, width: MathF.Max(70, _width - indent - 88), fontSize: 11);
        field.ExtraKeys = key => false;
        return HStack(4, width: _width)[
            Border(width: indent), prefix,
            Text(label + ":", 11, width: 54, color: Bind.From(() => UiTheme.T.TextMuted)),
            field,
            Button(_ => EditScalar(row.NodeId, draft.Value), "✓", fontSize: 10)];
    }

    private Signal<string> ScalarDraft(NodeId id, string value)
    {
        if (_scalarDrafts.TryGetValue(id, out Signal<string>? draft)) return draft;
        draft = new Signal<string>(value);
        _scalarDrafts.Add(id, draft);
        return draft;
    }

    private ValueApplyResult RefreshAfter(ValueApplyResult result)
    {
        if (result.Success)
        {
            _scalarDrafts.Clear();
            _version.Value++;
        }
        return result;
    }
}
