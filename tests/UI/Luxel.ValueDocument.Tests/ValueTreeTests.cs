using Luxel.ValueDocument;

namespace Luxel.ValueDocument.Tests;

public sealed class ValueTreeTests
{
    [Fact]
    public void Structural_edits_preserve_unaffected_ids_and_use_one_tree_transaction()
    {
        var nodes = new ValueNodeFactory();
        ValueScalarNode kept = nodes.Number("1");
        ValueScalarNode edited = nodes.String("old");
        ValueObjectNode root = nodes.Object([new("kept", kept), new("edited", edited)]);
        var document = new ValueDocument(root);

        ValueApplyResult result = document.ReplaceScalar(edited.Id, nodes.String("new"));

        Assert.True(result.Success);
        Assert.Equal(ValueTransactionOrigin.Tree, result.Transaction!.Origin);
        Assert.Equal(1, document.Revision);
        Assert.Equal(1, document.History.UndoDepth);
        Assert.True(document.TryGetNode(kept.Id, out ValueNode? keptAfter));
        Assert.Same(kept, keptAfter);
        Assert.True(document.TryGetNode(edited.Id, out ValueNode? editedAfter));
        Assert.Equal("new", Assert.IsType<ValueScalarNode>(editedAfter).Text);
    }

    [Fact]
    public void Object_commands_preserve_children_and_deletion_selects_neighbour()
    {
        var nodes = new ValueNodeFactory();
        ValueScalarNode first = nodes.Number("1");
        ValueScalarNode second = nodes.Number("2");
        ValueObjectNode root = nodes.Object([new("a", first), new("b", second)]);
        var document = new ValueDocument(root);
        ValueScalarNode third = nodes.Number("3");

        Assert.True(document.AddObjectProperty(root.Id, "c", third).Success);
        Assert.True(document.RenameObjectProperty(root.Id, "c", "renamed").Success);
        Assert.Equal("/renamed", document.Selection.Pointer);
        Assert.True(document.RemoveObjectProperty(root.Id, "renamed").Success);

        Assert.Equal(second.Id, document.Selection.NodeId);
        Assert.Equal("/b", document.Selection.Pointer);
        Assert.True(document.TryGetNode(first.Id, out ValueNode? kept));
        Assert.Same(first, kept);
    }

    [Fact]
    public void Array_move_preserves_identity_and_pointer_resolution()
    {
        var nodes = new ValueNodeFactory();
        ValueScalarNode a = nodes.String("a");
        ValueScalarNode b = nodes.String("b");
        ValueScalarNode c = nodes.String("c");
        ValueArrayNode root = nodes.Array([a, b, c]);
        var document = new ValueDocument(root);

        Assert.True(document.MoveArrayItem(root.Id, 0, 2).Success);

        Assert.Equal(a.Id, document.Selection.NodeId);
        Assert.Equal("/2", document.Selection.Pointer);
        Assert.True(JsonPointer.TryResolve(document.AcceptedRoot, "/2", out ValueNode? resolved));
        Assert.Same(a, resolved);
        Assert.Equal([b.Id, c.Id, a.Id], ((ValueArrayNode)document.AcceptedRoot).Items.Select(item => item.Id));
    }

    [Fact]
    public void Raw_then_tree_edit_undoes_and_redoes_across_origins()
    {
        var document = new ValueDocument(Parse("{\"value\":1}"));
        document.SetRawDraft("{\"value\":2}");
        Assert.True(document.ApplyRawDraft().Success);
        var root = Assert.IsType<ValueObjectNode>(document.AcceptedRoot);
        ValueScalarNode scalar = Assert.IsType<ValueScalarNode>(root.Properties[0].Value);

        Assert.True(document.ReplaceScalar(scalar.Id, new ValueNodeFactory().Number("3")).Success);
        Assert.Equal("{\"value\":3}", JsonValueCodec.Serialize(document.AcceptedRoot));
        Assert.True(document.Undo().Success);
        Assert.Equal("{\"value\":2}", JsonValueCodec.Serialize(document.AcceptedRoot));
        Assert.True(document.Undo().Success);
        Assert.Equal("{\"value\":1}", JsonValueCodec.Serialize(document.AcceptedRoot));
        Assert.True(document.Redo().Success);
        Assert.True(document.Redo().Success);
        Assert.Equal("{\"value\":3}", JsonValueCodec.Serialize(document.AcceptedRoot));
    }

    [Fact]
    public void Invalid_dirty_raw_blocks_structured_commands_and_is_preserved()
    {
        var nodes = new ValueNodeFactory();
        ValueScalarNode scalar = nodes.Number("1");
        var document = new ValueDocument(scalar);
        const string draft = "{\"broken\":";
        document.SetRawDraft(draft);

        ValueApplyResult result = document.ReplaceScalar(scalar.Id, nodes.Number("2"));

        Assert.Equal(ValueApplyStatus.InvalidRawDraft, result.Status);
        Assert.Equal(draft, document.RawDraft!.Text);
        Assert.Equal("1", JsonValueCodec.Serialize(document.AcceptedRoot));
        Assert.Equal(0, document.Revision);
    }

    [Fact]
    public void Array_enumeration_is_bounded_and_chunkable()
    {
        var nodes = new ValueNodeFactory();
        ValueArrayNode root = nodes.Array(Enumerable.Range(0, 100).Select(i => nodes.Number(i.ToString())));
        var controller = new ValueTreeController(new ValueDocument(root));

        IReadOnlyList<ValueTreeRow> visible = controller.EnumerateRows(maxRows: 10, arrayItemLimit: 100);
        IReadOnlyList<ValueTreeRow> chunk = controller.EnumerateArrayRows(root.Id, 40, 5);

        Assert.Equal(10, visible.Count);
        Assert.Equal([40, 41, 42, 43, 44], chunk.Select(row => row.Index));
        Assert.Equal(["/40", "/41", "/42", "/43", "/44"], chunk.Select(row => row.Pointer));
    }

    private static ValueNode Parse(string json)
    {
        JsonValueParseResult result = JsonValueCodec.Parse(json);
        Assert.True(result.Success);
        return result.Root!;
    }
}
