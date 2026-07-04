using Luxel.Typography;
using Luxel.TwoD;
using Luxel.UI;
using Luxel.Controls;
using Xunit;
using static Luxel.Controls.Kit;

namespace Luxel.Tests;

public class UiLayoutTests
{
    private static LayoutContext Ctx() => new() { Font = VectorFont.LoadSystem() };
    private static void Close(float expected, float actual, float tol = 0.6f)
        => Assert.True(MathF.Abs(expected - actual) <= tol, $"expected≈{expected}, actual={actual}");

    [Fact]
    public void GridLength_IntImplicitlyStar()
    {
        GridLength g = 2;                       // [1,2] の各要素はこの暗黙変換で star になる
        Assert.Equal(GridUnit.Star, g.Unit);
        Assert.Equal(2f, g.Value);
    }

    [Fact]
    public void Constraints_ConstrainAndDeflate()
    {
        var c = new Constraints(0, 100, 0, 50);
        Size s = c.Constrain(new Size(200, 10));
        Close(100, s.Width); Close(10, s.Height);
        Constraints d = c.Deflate(new Thickness(10));
        Close(80, d.MaxW); Close(30, d.MaxH);
    }

    [Fact]
    public void Grid_StarColumns_SplitProportionally()
    {
        LayoutContext ctx = Ctx();
        Text a = Text("A").GridColumn(0);           // セル指定は fluent 添付
        Text b = Text("B").GridColumn(1);
        Grid grid = Grid(columns: [1, 2])[a, b];    // 1:2 配分
        grid.Layout(Constraints.Tight(new Size(300, 100)), ctx);
        Close(300, grid.Size.Width);
        Close(0, a.Offset.X);                       // col0 は 0 から
        Close(100, b.Offset.X);                     // col1 は 300*1/3 = 100 から
    }

    [Fact]
    public void Grid_FixedThenStar()
    {
        LayoutContext ctx = Ctx();
        Text a = Text("A").GridColumn(1);
        Grid grid = Grid(columns: [GridLength.Px(50), GridLength.Star(1)])[a];
        grid.Layout(Constraints.Tight(new Size(200, 100)), ctx);
        Close(50, a.Offset.X);                      // 固定 50px の後ろから star 列
    }

    [Fact]
    public void StackPanel_Vertical_StacksWithSpacing()
    {
        LayoutContext ctx = Ctx();
        Button a = Button(_ => { }, "A");
        Button b = Button(_ => { }, "B");
        StackPanel st = VStack(spacing: 10)[a, b];
        st.Layout(new Constraints(0, 300, 0, 300), ctx);
        Close(0, a.Offset.Y);
        Close(a.Size.Height + 10, b.Offset.Y);      // a の下 + spacing
    }

    [Fact]
    public void Border_Padding_OffsetsChildAndGrows()
    {
        LayoutContext ctx = Ctx();
        Text t = Text("X");
        Border bd = Border(padding: new Thickness(8))[t];
        bd.Layout(new Constraints(0, 200, 0, 200), ctx);
        Close(8, t.Offset.X); Close(8, t.Offset.Y);
        Close(t.Size.Width + 16, bd.Size.Width);
    }

    [Fact]
    public void VectorFont_Measure_WidthGrowsWithText()
    {
        using var f = VectorFont.LoadSystem();
        (float w1, float h) = f.Measure("i", 24);
        (float w2, float _) = f.Measure("iiiiii", 24);
        Assert.True(h > 0);
        Assert.True(w2 > w1);
    }

    [Fact]
    public void Rect_Contains()
    {
        var r = new Rect(10, 10, 20, 20);
        Assert.True(r.Contains(15, 15));
        Assert.False(r.Contains(5, 5));
        Assert.False(r.Contains(35, 15));
    }
}
