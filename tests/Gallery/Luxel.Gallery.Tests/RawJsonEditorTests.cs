using System.Reflection;
using System.Text.Json;
using Luxel.Gallery;
using Luxel.Gallery.UI;
using Luxel.UI;
using Luxel.ValueDocument;
using Xunit;
using static Luxel.Gallery.UI.Kit;

namespace Luxel.Tests;

public sealed class RawJsonEditorTests
{
    [Fact]
    public void Multiline_invalid_draft_stays_visible_and_does_not_commit()
    {
        var commits = new List<string>();
        var editor = CreateEditor(commits.Add);
        const string draft = "{\n  \"nodes\": [1,\n}";

        editor.Draft.Value = draft;

        Assert.Same(editor.Draft, editor.EditorView.Value.Get());
        Assert.False(editor.Apply());
        Assert.Equal(draft, editor.Draft.Value);
        Assert.True(editor.Document.IsInvalid);
        Assert.Empty(commits);
        Assert.NotNull(editor.Document.Diagnostic);
    }

    [Fact]
    public void Format_compact_discard_and_valid_apply_share_document_semantics()
    {
        var commits = new List<string>();
        var editor = CreateEditor(commits.Add);
        editor.Draft.Value = "{\"nodes\":[1,2]}";

        Assert.True(editor.Format());
        Assert.Contains('\n', editor.Draft.Value);
        Assert.Empty(commits);

        Assert.True(editor.Compact());
        Assert.Equal("{\"nodes\":[1,2]}", editor.Draft.Value);
        Assert.Empty(commits);

        Assert.True(editor.Apply());
        Assert.True(editor.Apply());
        Assert.Equal("{\"nodes\":[1,2]}", Assert.Single(commits));

        editor.Draft.Value = "{\"nodes\":[}";
        Assert.False(editor.Apply());
        editor.Discard();
        Assert.Equal("{\"nodes\":[1,2]}", editor.Draft.Value);
        Assert.False(editor.Document.IsDirty);
        Assert.False(editor.Document.IsInvalid);
    }

    [Fact]
    public void Knobs_table_caches_raw_editor_and_retains_draft_across_rebuilds()
    {
        var context = new StoryContext(args: StoryArgs.Parse("{\"config\":{\"nodes\":[1]}}"));
        using JsonDocument initial = JsonDocument.Parse("{\"nodes\":[]}");
        context.Arg("config", initial.RootElement.Clone(), new StoryArgOptions<JsonElement>
        {
            Editor = StoryArgEditorKind.Json,
        });
        StoryKnob knob = Assert.Single(context.Knobs);
        var edits = new List<(StoryKnob Knob, string Value)>();
        KnobsTable table = KnobsTable(context.Knobs, onEdit: (_, edited, value) => edits.Add((edited, value)));
        MethodInfo editorMethod = typeof(KnobsTable).GetMethod("Editor", BindingFlags.Instance | BindingFlags.NonPublic)!;

        var first = Assert.IsType<RawJsonEditor>(editorMethod.Invoke(table, [knob, 180f]));
        first.Draft.Value = "{\n  \"nodes\": [2]\n}";
        var rebuilt = Assert.IsType<RawJsonEditor>(editorMethod.Invoke(table, [knob, 180f]));

        Assert.Same(first, rebuilt);
        Assert.Equal("{\n  \"nodes\": [2]\n}", rebuilt.Draft.Value);
        Assert.Empty(edits);

        Assert.True(rebuilt.Apply());
        (StoryKnob editedKnob, string editedValue) = Assert.Single(edits);
        Assert.Same(knob, editedKnob);
        Assert.Equal("{\"nodes\":[2]}", editedValue);
    }

    [Fact]
    public void Mode_switch_is_revision_neutral_and_invalid_draft_roundtrips_exactly()
    {
        var commits = new List<string>();
        var editor = CreateEditor(commits.Add);

        editor.SwitchMode(JsonEditorMode.Tree);
        editor.SwitchMode(JsonEditorMode.Raw);

        Assert.Equal(0, editor.Document.Revision);
        Assert.Empty(commits);

        const string invalid = "{\n  \"nodes\": [1,\n}";
        editor.Draft.Value = invalid;
        editor.SwitchMode(JsonEditorMode.Tree);

        Assert.Equal(JsonEditorMode.Tree, editor.Mode);
        Assert.True(editor.Document.IsInvalid);
        Assert.True(editor.TreeView.IsReadOnly);
        Assert.Empty(commits);

        editor.SwitchMode(JsonEditorMode.Raw);
        Assert.Equal(invalid, editor.Draft.Value);
        Assert.Equal(0, editor.Document.Revision);
    }

    [Fact]
    public void Native_tree_widget_edits_shared_document_and_commits_once()
    {
        var commits = new List<string>();
        var editor = CreateEditor(commits.Add);
        editor.SwitchMode(JsonEditorMode.Tree);
        ValueNode root = editor.Document.ValueDocument.AcceptedRoot;
        ValueScalarNode scalar = Assert.IsType<ValueScalarNode>(
            Assert.IsType<ValueArrayNode>(Assert.IsType<ValueObjectNode>(root).Properties[0].Value).Items[0]);

        ValueApplyResult result = editor.TreeView.EditScalar(scalar.Id, "2");

        Assert.True(result.Success);
        Assert.Equal("{\"nodes\":[2]}", Assert.Single(commits));
        Assert.Equal(1, editor.Document.Revision);
        Assert.True(editor.Document.ValueDocument.History.CanUndo);
        Assert.True(editor.TreeView.Undo().Success);
        Assert.Equal("{\"nodes\":[1]}", commits[^1]);
    }

    private static RawJsonEditor CreateEditor(Action<string> commit)
    {
        using JsonDocument accepted = JsonDocument.Parse("{\"nodes\":[1]}");
        var definition = new StoryArgDefinition("config", "json", accepted.RootElement.Clone(),
            Editor: StoryArgEditorKind.Json);
        return new RawJsonEditor(definition, accepted.RootElement, commit, width: 200, height: 100);
    }
}
