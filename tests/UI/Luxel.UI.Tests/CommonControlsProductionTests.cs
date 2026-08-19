using Luxel.Controls;
using Luxel.Graphics.TwoD;
using Luxel.Typography;
using Luxel.UI;
using static Luxel.Controls.Kit;

namespace Luxel.Tests;

public sealed class CommonControlsProductionTests
{
    private static UiHost Host(Widget root, float width, float height)
    {
        var host = new UiHost(new RetainedCanvas(), VectorFont.LoadSystem(), width, height);
        host.SetRoot(root);
        return host;
    }

    [Fact]
    public void TreeView_RealizedPointerAndKeyboardShareFocusAndRangeSelection()
    {
        TreeView tree = TreeView([
            new TreeNode("a", "A"), new TreeNode("b", "B"), new TreeNode("c", "C")
        ], appearance: new TreeViewAppearance(RowHeight: 22, RowSpacing: 3));
        using UiHost host = Host(tree, 240, 80);

        Assert.True(host.Click(20, 11));
        Assert.True(host.Click(20, 36, mods: KeyModifiers.Ctrl));
        Assert.True(tree.IsKeyboardFocused);
        Assert.Equal(["a", "b"], tree.SelectedKeys.Order());

        Assert.True(host.KeyDown(Key.Down, shift: true));
        Assert.Equal("c", tree.FocusedKey);
        Assert.Equal(["b", "c"], tree.SelectedKeys.Order());
        Assert.Equal(SemanticRole.Tree, ((ISemanticProvider)tree).GetSemantics().Role);
    }

    [Fact]
    public void ListView_RealizedInputMaintainsMultiSelectionVirtualizationAndReveal()
    {
        var items = new Signal<IReadOnlyList<string>>(Enumerable.Range(0, 100).Select(i => $"row-{i}").ToArray());
        ListView list = ListView(height: 60, rowHeight: 20, items: items);
        using UiHost host = Host(list, 240, 60);

        host.Click(20, 10);
        host.Click(20, 30, mods: KeyModifiers.Ctrl);
        Assert.Equal([0, 1], list.SelectedIndices.Order());
        Assert.True(list.IsKeyboardFocused);

        host.KeyDown(Key.Down, shift: true);
        Assert.Equal([1, 2], list.SelectedIndices.Order());
        host.KeyDown(Key.End);
        Assert.Equal(99, list.SelectedIndex);
        Assert.True(list.ScrollOffset > 0);
        Assert.InRange(list.RealizedRowCount, 3, 5);
    }

    [Fact]
    public void TabStrip_RealizedKeyboardSkipsDisabledAndSelectedTabIsRevealed()
    {
        string? selected = null;
        string? closed = null;
        TabStrip tabs = TabStrip(
            items: [
                new("a", "Alpha long title", Tooltip: "Alpha help"),
                new("b", "Disabled", Closable: true, Disabled: true),
                new("c", "Charlie long title", Closable: false),
                new("d", "Delta long title")
            ],
            selectedKey: "d",
            onSelect: (_, key) => selected = key,
            onCloseRequest: (_, key) => closed = key);
        using UiHost host = Host(tabs, 190, 32);

        Assert.True(tabs.ScrollOffset > 0);
        Assert.True(tabs.OverflowCount > 0);
        Point d = Assert.IsType<Point>(tabs.TabCenterOf("d"));
        host.Click(d.X, d.Y);
        Assert.True(tabs.IsKeyboardFocused);

        host.KeyDown(Key.Left);
        Assert.Equal("c", selected);
        Assert.Null(tabs.CloseCenterOf("c"));
        host.KeyDown(Key.Delete);
        Assert.Null(closed);

        SemanticNode semantics = ((ISemanticProvider)tabs).GetSemantics();
        SemanticNode alpha = Assert.Single(semantics.Children!, x => x.Key == "a");
        Assert.Equal("Alpha help", alpha.Description);
        Assert.True(Assert.Single(semantics.Children!, x => x.Key == "b").Disabled);
    }

    [Fact]
    public void TabStrip_DragDropReportsSourceStripIdentity()
    {
        object channel = new();
        TabDropRequest? request = null;
        TabStrip source = TabStrip(items: [new("source", "Source")], dragChannel: channel);
        TabStrip target = TabStrip(items: [new("target", "Target")], dragChannel: channel,
            onDropRequest: (_, value) => request = value);
        Widget root = VStack()[source, target];
        using UiHost host = Host(root, 260, 64);

        Point from = Assert.IsType<Point>(source.TabCenterOf("source"));
        Point to = Assert.IsType<Point>(target.TabCenterOf("target"));
        host.PointerDown(from.X, from.Y);
        host.PointerMove(to.X, to.Y);
        host.PointerUp(to.X, to.Y);

        Assert.NotNull(request);
        Assert.Same(source, request!.SourceStrip);
        Assert.Same(target, request.TargetStrip);
        Assert.Equal("source", request.Key);
    }

    [Fact]
    public void StatusBar_ActualLayoutCollapsesByPriorityAndCreatesOverflowAndSeparators()
    {
        StatusBar bar = StatusBar(items: [
            new StatusBarItem("high", new Spacer(), Priority: 100, Separator: true, PreferredWidth: 70),
            new StatusBarItem("middle", new Spacer(), Priority: 50, PreferredWidth: 70),
            new StatusBarItem("low", new Spacer(), Priority: 0, PreferredWidth: 70)
        ]);
        using UiHost host = Host(bar, 180, Luxel.Controls.StatusBar.BarH);

        Assert.Contains("low", bar.CollapsedKeys);
        Assert.True(bar.HasOverflow);
        Assert.Equal(1, bar.SeparatorCount);
        Assert.Equal(Luxel.Controls.StatusBar.BarH, bar.Size.Height);

        host.Resize(360, Luxel.Controls.StatusBar.BarH);
        Assert.Empty(bar.CollapsedKeys);
        Assert.False(bar.HasOverflow);
        Assert.Equal(["high", "middle", "low"], bar.VisibleKeys);
    }

    [Fact]
    public void GridView_RealizedInputVirtualizesAndSynchronizesRangeSelection()
    {
        var items = new Signal<IReadOnlyList<GridViewItem>>(Enumerable.Range(0, 100)
            .Select(i => new GridViewItem(i.ToString(), $"item-{i}")).ToArray());
        GridView grid = GridView(items: items, height: 100, itemWidth: 80, itemHeight: 40);
        using UiHost host = Host(grid, 240, 100);

        host.Click(20, 20);
        host.Click(100, 20, mods: KeyModifiers.Ctrl);
        Assert.Equal(["0", "1"], grid.SelectedKeys.Order());
        host.KeyDown(Key.Down, shift: true);
        Assert.Equal(["1", "2", "3", "4"], grid.SelectedKeys.Order());
        Assert.True(grid.RealizedCellCount < 100);
        Assert.True(grid.IsKeyboardFocused);
    }

    [Fact]
    public void GridView_AndDataGrid_RealizedDragDropEmitSourceIndexedReorders()
    {
        (int from, int to)? gridMove = null;
        var gridItems = new Signal<IReadOnlyList<GridViewItem>>(Enumerable.Range(0, 12)
            .Select(i => new GridViewItem(i.ToString(), $"item-{i}")).ToArray());
        GridView grid = GridView(items: gridItems, height: 100, itemWidth: 80, itemHeight: 40,
            onReorder: (_, from, to) => gridMove = (from, to));
        grid.AllowReorder = true;
        using (UiHost host = Host(grid, 240, 100))
        {
            host.PointerDown(20, 20);
            host.PointerMove(180, 60);
            host.PointerUp(180, 60);
        }
        Assert.NotNull(gridMove);
        Assert.Equal(0, gridMove!.Value.from);
        Assert.Equal(5, gridMove.Value.to);

        (int from, int to)? rowMove = null;
        var rows = new Signal<IReadOnlyList<DataGridRow>>(Enumerable.Range(0, 8)
            .Select(i => new DataGridRow(i.ToString(), [$"row-{i}"])).ToArray());
        DataGrid data = DataGrid(items: rows, columns: [new DataGridColumn("name", "Name", 180)],
            height: 120, rowHeight: 24, onReorder: (_, from, to) => rowMove = (from, to));
        data.AllowReorder = true;
        using (UiHost host = Host(data, 220, 120))
        {
            host.PointerDown(20, 36);
            host.PointerMove(20, 84);
            host.PointerUp(20, 84);
        }
        Assert.NotNull(rowMove);
        Assert.Equal(0, rowMove!.Value.from);
        Assert.Equal(2, rowMove.Value.to);
    }

    [Fact]
    public void DataGrid_RealizedInputVirtualizesSelectsAndRevealsEnd()
    {
        var rows = new Signal<IReadOnlyList<DataGridRow>>(Enumerable.Range(0, 100)
            .Select(i => new DataGridRow(i.ToString(), [$"name-{i}", i.ToString()])).ToArray());
        DataGrid grid = DataGrid(items: rows,
            columns: [new DataGridColumn("name", "Name", 120), new DataGridColumn("value", "Value", 80)],
            height: 120, rowHeight: 24);
        using UiHost host = Host(grid, 220, 120);

        host.Click(20, 36);
        host.Click(20, 60, mods: KeyModifiers.Ctrl);
        Assert.Equal(["0", "1"], grid.SelectedKeys.Order());
        host.KeyDown(Key.End);
        Assert.Equal("99", grid.FocusedKey);
        Assert.True(grid.ScrollOffset > 0);
        Assert.True(grid.RealizedRowCount < 100);
        Assert.True(grid.IsKeyboardFocused);
        Assert.Equal(SemanticRole.Grid, ((ISemanticProvider)grid).GetSemantics().Role);
    }
}
