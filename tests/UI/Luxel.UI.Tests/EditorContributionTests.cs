using Luxel.Document;

namespace Luxel.Tests;

public class EditorContributionTests
{
    [Fact]
    public void InsertItem_ReplacesSelection_AndPlacesCaretInsideTemplate()
    {
        EditorState state = EditorState.Create("old", EditorSelection.Single(0, 3));
        var item = new EditorInsertItem("link", "Link", "[text](url)", CaretBack: 1);

        Transaction transaction = EditorContributionCommands.Insert(state, item);

        Assert.Equal("[text](url)", transaction.State.Doc.Text);
        Assert.Equal(10, transaction.State.Selection.Main.Head);
    }

    [Fact]
    public void SelectionAction_WrapsSelection_AndKeepsInnerTextSelected()
    {
        EditorState state = EditorState.Create("hello", EditorSelection.Single(1, 4));
        var action = new EditorSelectionAction("bold", "Bold", "**", "**");

        Transaction transaction = EditorContributionCommands.Apply(state, action);

        Assert.Equal("h**ell**o", transaction.State.Doc.Text);
        Assert.Equal((3, 6), (transaction.State.Selection.Main.From, transaction.State.Selection.Main.To));
    }

    [Fact]
    public void SelectionAction_UsesSelectablePlaceholderAtCaret()
    {
        EditorState state = EditorState.Create("", EditorSelection.Cursor(0));
        var action = new EditorSelectionAction("link", "Link", "[", "](url)", "label");

        Transaction transaction = EditorContributionCommands.Apply(state, action);

        Assert.Equal("[label](url)", transaction.State.Doc.Text);
        Assert.Equal((1, 6), (transaction.State.Selection.Main.From, transaction.State.Selection.Main.To));
    }
}
