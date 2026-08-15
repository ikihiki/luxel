using Luxel.Controls;
using Xunit;

namespace Luxel.Tests;

public sealed class TextEditorReadOnlyTests
{
    [Fact]
    public void ReadOnlyEditor_HidesSelectionToolbar()
    {
        var editor = new TextEditorView
        {
            ReadOnly = true,
            SelectionActions = [new("bold", "Bold", "**", "**")],
        };

        Assert.False(editor.CanShowSelectionToolbar(hasSelection: true));
    }

    [Fact]
    public void EditableEditor_ShowsSelectionToolbarForSelection()
    {
        var editor = new TextEditorView
        {
            SelectionActions = [new("bold", "Bold", "**", "**")],
        };

        Assert.True(editor.CanShowSelectionToolbar(hasSelection: true));
        Assert.False(editor.CanShowSelectionToolbar(hasSelection: false));
    }
}
