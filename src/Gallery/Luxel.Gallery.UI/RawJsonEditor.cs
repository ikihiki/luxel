using System.Text.Json;
using Luxel.Controls;
using Luxel.Typography;
using Luxel.UI;
using static Luxel.Controls.Kit;

namespace Luxel.Gallery.UI;

/// <summary>
/// Native multiline raw JSON editor for Gallery args. Draft text is isolated in
/// <see cref="StoryJsonArgDocument"/> until the user explicitly applies it.
/// </summary>
public sealed class RawJsonEditor : CompositeControl
{
    private readonly float _width;
    private readonly Signal<string> _draft;
    private readonly Signal<string> _status;
    private readonly Signal<string> _diagnostic;
    private readonly TextEditorView _editor;

    public RawJsonEditor(StoryArgDefinition definition, JsonElement acceptedValue, Action<string> commit,
        float width = 180f, float height = 112f)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(commit);

        _width = MathF.Max(120, width);
        Document = new StoryJsonArgDocument(definition, acceptedValue,
            accepted => commit(accepted.GetRawText()));
        _draft = new Signal<string>(Document.Text);
        _status = new Signal<string>(Status);
        _diagnostic = new Signal<string>(DiagnosticText());
        _editor = TextEditorView(_draft, editorHeight: MathF.Max(72, height), editorWidth: _width);
        _editor.WrapText = true;
        _editor.OnEdit = _ => DraftChanged();
    }

    public StoryJsonArgDocument Document { get; }
    public Signal<string> Draft => _draft;
    public TextEditorView EditorView => _editor;
    public string Status => Document.IsInvalid
        ? "JSONにエラーがあります"
        : Document.IsDirty ? "未適用の変更があります" : "有効なJSONです";

    public bool Apply()
    {
        Document.SetRawDraft(_draft.Value);
        bool applied = Document.Apply();
        SyncDraft();
        UpdateStatus();
        return applied;
    }

    public bool Format()
    {
        Document.SetRawDraft(_draft.Value);
        bool formatted = Document.Format(indented: true);
        SyncDraft();
        UpdateStatus();
        return formatted;
    }

    public bool Compact()
    {
        Document.SetRawDraft(_draft.Value);
        bool formatted = Document.Format(indented: false);
        SyncDraft();
        UpdateStatus();
        return formatted;
    }

    public void Discard()
    {
        Document.Discard();
        SyncDraft();
        UpdateStatus();
    }

    protected override Widget Build()
        => VStack(5, width: _width)[
            _editor,
            HStack(4)[
                Button(_ => Apply(), "適用", fontSize: 10),
                Button(_ => Format(), "整形", fontSize: 10),
                Button(_ => Compact(), "圧縮", fontSize: 10),
                Button(_ => Discard(), "破棄", fontSize: 10)],
            Text(_status, 10, color: Bind.From(() =>
            {
                _ = _status.Value;
                return Document.IsInvalid ? UiTheme.T.Danger
                    : Document.IsDirty ? UiTheme.T.Warning : UiTheme.T.Success;
            }), width: _width, wrap: TextWrap.Word),
            Text(_diagnostic, 10, color: Bind.From(() => UiTheme.T.Danger),
                width: _width, wrap: TextWrap.Word)];

    private void DraftChanged()
    {
        Document.SetRawDraft(_draft.Value);
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        _status.Value = Status;
        _diagnostic.Value = DiagnosticText();
    }

    private string DiagnosticText() => Document.Diagnostic is { } diagnostic
        ? $"行 {diagnostic.Line}、列 {diagnostic.Column}: {diagnostic.Message}"
        : string.Empty;

    private void SyncDraft()
    {
        string text = Document.Text;
        if (!string.Equals(_draft.Peek(), text, StringComparison.Ordinal))
            _draft.Value = text;
    }
}
