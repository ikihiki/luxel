using Luxel.Controls;
using Luxel.UI;
using static Luxel.Controls.Kit;

namespace Luxel.Tests;

public sealed class TreeListQualityTests
{
    [Fact]
    public void TreeKeyboardNavigationExpandsAndSupportsAdditiveSelection()
    {
        TreeView tree = TreeView([new TreeNode("root", "Root", [new TreeNode("child", "Child")])], expanded: new HashSet<string>());
        Assert.Equal("root", tree.MoveFocus(Key.Home));
        Assert.Equal("root", tree.MoveFocus(Key.Right));
        Assert.Equal("child", tree.MoveFocus(Key.Down, additive: true));
        Assert.Equal(2, tree.SelectedKeys.Count);
    }

    [Fact]
    public void ListKeyboardNavigationSupportsMultiSelectionWithVirtualizedDataContract()
    {
        var items = new Signal<IReadOnlyList<string>>(["a", "b", "c"]);
        ListView list = ListView(height: 60, items: items);
        Assert.Equal(0, list.MoveSelection(Key.Home));
        Assert.Equal(1, list.MoveSelection(Key.Down, additive: true));
        Assert.Equal([0, 1], list.SelectedIndices.Order());
        Assert.Equal(2, list.MoveSelection(Key.End));
    }
}
