using Luxel.Controls;
using Luxel.Graphics.TwoD;
using Luxel.Typography;
using Luxel.UI;
using Xunit;
using static Luxel.Controls.Kit;

namespace Luxel.Tests;

public class UiDynamicTests
{
    private static LayoutContext Ctx() => new() { Font = VectorFont.LoadSystem() };

    [Fact]
    public void SpreadCollection_ExpandsToChildren()
    {
        StackPanel s = VStack()[new[] { "a", "b", "c" }.Select(x => Text(x)).ToArray()];
        Assert.Equal(3, s.ChildCount);
    }

    [Fact]
    public void ConditionalChild_IncludesOrSkips()
    {
        bool flag = true;
        Widget[] aChildren = (flag ? new Widget[] { Text("x") } : []).Concat(new Widget[] { Text("y") }).ToArray();
        Widget[] bChildren = (!flag ? new Widget[] { Text("x") } : []).Concat(new Widget[] { Text("y") }).ToArray();
        StackPanel a = VStack()[aChildren];
        StackPanel b = VStack()[bChildren];
        Assert.Equal(2, a.ChildCount);
        Assert.Equal(1, b.ChildCount);
    }

    [Fact]
    public void Mixed_StaticAndSpread()
    {
        StackPanel s = VStack()[Text("head"), Text("1"), Text("2"), Text("tail")];
        Assert.Equal(4, s.ChildCount);
    }

    [Fact]
    public void ScrollViewer_ClampsContentLayout()
    {
        LayoutContext ctx = Ctx();
        StackPanel content = VStack()[Enumerable.Range(0, 30).Select(i => Text($"row {i}")).ToArray<Widget>()];
        var sv = Scroll(100)[content];
        sv.Layout(Constraints.Tight(new Size(200, 100)), ctx);
        Assert.Equal(100, sv.Size.Height, 1);
        Assert.True(content.Size.Height > 100);
    }
}
