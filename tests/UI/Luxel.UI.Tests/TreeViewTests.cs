using Luxel.Controls;
using Xunit;
using Luxel.Typography;
using Luxel.UI;

namespace Luxel.Tests;

/// <summary>TV: TreeView の可視行列挙 (Flatten) — 展開状態に従う階層の平坦化。</summary>
public class TreeViewTests
{
    private static TreeNode[] Sample() =>
    [
        new("a", "A", [
            new("a/1", "A1"),
            new("a/2", "A2", [new("a/2/x", "A2X")]),
        ]),
        new("b", "B", [new("b/1", "B1")]),
        new("c", "C"),
    ];

    private static List<(TreeNode Node, int Depth)> Flat(ISet<string> expanded)
    {
        var into = new List<(TreeNode, int)>();
        TreeView.Flatten(Sample(), expanded, 0, into);
        return into;
    }

    [Fact]
    public void Collapsed_ShowsOnlyRoots()
    {
        var flat = Flat(new HashSet<string>());
        Assert.Equal(["a", "b", "c"], flat.Select(f => f.Node.Key));
        Assert.All(flat, f => Assert.Equal(0, f.Depth));
    }

    [Fact]
    public void Expanded_ShowsChildrenWithDepth()
    {
        var flat = Flat(new HashSet<string> { "a" });
        Assert.Equal(["a", "a/1", "a/2", "b", "c"], flat.Select(f => f.Node.Key));
        Assert.Equal(1, flat.First(f => f.Node.Key == "a/1").Depth);
    }

    [Fact]
    public void NestedExpansion_RequiresAllAncestorsOpen()
    {
        // a/2 だけ開いても親 a が閉じていれば見えない
        var flat = Flat(new HashSet<string> { "a/2" });
        Assert.DoesNotContain(flat, f => f.Node.Key == "a/2/x");

        flat = Flat(new HashSet<string> { "a", "a/2" });
        Assert.Contains(flat, f => f.Node.Key == "a/2/x" && f.Depth == 2);
    }

    [Fact]
    public void LeafInExpandedSet_IsHarmless()
    {
        // 葉 (子なし) のキーが展開セットにあっても何も起きない
        var flat = Flat(new HashSet<string> { "c" });
        Assert.Equal(3, flat.Count);
    }

    [Fact]
    public void Appearance_UsesConfiguredRowMetrics()
    {
        var appearance = new TreeViewAppearance(RowHeight: 31, Indent: 16, LeafFontSize: 13);
        var row = new TreeViewRow("Leaf", depth: 2, hasChildren: false, open: false,
            selected: false, appearance, activate: () => { }, toggle: null);
        var ctx = new LayoutContext { Font = VectorFont.LoadSystem() };

        row.Layout(new Constraints(0, 240, 0, 100), ctx);

        Assert.Equal(31, row.Size.Height);
        Assert.Equal(240, row.Size.Width);
        Assert.Equal("Leaf", row.DebugDetail);
    }
}
