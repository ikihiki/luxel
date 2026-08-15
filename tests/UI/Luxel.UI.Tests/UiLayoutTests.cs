using Luxel.Controls;
using Luxel.Graphics.TwoD;
using Luxel.Typography;
using Luxel.UI;
using Xunit;
using static Luxel.Controls.Kit;

namespace Luxel.Tests;

public class UiLayoutTests
{
    private static LayoutContext Ctx() => new() { Font = VectorFont.LoadSystem() };
    private static void Close(float expected, float actual, float tol = 0.6f)
        => Assert.True(MathF.Abs(expected - actual) <= tol, $"expected≈{expected}, actual={actual}");

    [Fact]
    public void DocumentTabs_AllowsViewSwitcherChrome()
    {
        LayoutContext ctx = Ctx();
        IReadOnlyList<DocTab> items = [new("args", "Args"), new("output", "Output")];
        DocumentTabs documentTabs = DocumentTabs(items);
        DocumentTabs viewTabs = DocumentTabs(items, showClose: false, stripHeight: 36,
            activeBackground: false);

        documentTabs.Layout(new Constraints(0, float.PositiveInfinity, 0, 100), ctx);
        viewTabs.Layout(new Constraints(0, float.PositiveInfinity, 0, 100), ctx);

        Close(Luxel.Controls.DocumentTabs.StripH, documentTabs.Size.Height);
        Close(36, viewTabs.Size.Height);
        Assert.True(viewTabs.Size.Width < documentTabs.Size.Width);
        Assert.Null(viewTabs.CloseCenterOf("args"));
    }

    [Fact]
    public void TextField_LeadingAndTrailingSlots_AreLaidOutInsideControl()
    {
        LayoutContext ctx = Ctx();
        TextField field = TextField(new Signal<string>("query"), width: 200)[
            TextFieldSlot.Leading(() => Icon(IconKind.Search, iconSize: 16)),
            TextFieldSlot.Trailing(() => Icon(IconKind.Close, iconSize: 14))];

        field.Layout(new Constraints(0, 300, 0, 100), ctx);
        Widget[] slots = field.DebugChildren().ToArray();

        Close(200, field.Size.Width);
        Assert.Equal(2, slots.Length);
        Assert.True(slots[0].Offset.X < slots[1].Offset.X);
        Assert.True(slots[0].Offset.Y >= 0);
        Assert.True(slots[1].Offset.X + slots[1].Size.Width < field.Size.Width);
    }

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
    public void HStack_VAlignCenter_CentersWithinRow_NotWithinAvailableHeight()
    {
        // 回帰: Button/Counter ストーリー — HStack 内の vAlign:Center Text が、親の空き高さ
        // (crossAvail) 基準で整列されて行 (ボタンの並び) の下へはみ出していた。
        // 整列基準はスタック自身の cross サイズ (= 最大子高さ) であること。
        LayoutContext ctx = Ctx();
        Button minus = Button(_ => { }, "-");
        Text label = Text(" 0 ", 22, vAlign: Align.Center);
        Button plus = Button(_ => { }, "+");
        StackPanel st = HStack(8)[minus, label, plus];

        st.Layout(new Constraints(0, 432, 0, 112), ctx);   // Frame(padding:24) 相当の loose 制約

        Close(st.Size.Height, MathF.Max(minus.Size.Height, label.Size.Height));   // 行高 = 最大子高
        Close((st.Size.Height - label.Size.Height) / 2, label.Offset.Y);          // 行内センタリング
        Assert.True(label.Offset.Y + label.Size.Height <= st.Size.Height + 0.6f,
            $"text ({label.Offset.Y}+{label.Size.Height}) がスタック ({st.Size.Height}) の外にある");
        Close(minus.Size.Width + 8, label.Offset.X);                              // 主軸は従来どおり
    }

    [Fact]
    public void HStack_VAlignCenter_WorksWithUnboundedCross()
    {
        // 旧実装は cross が ∞ のとき整列を諦めて ca=0 だった — 自分サイズ基準なら常に整列できる
        LayoutContext ctx = Ctx();
        Button a = Button(_ => { }, "A");
        Text t = Text("x", 10, vAlign: Align.Center);
        StackPanel st = HStack()[a, t];

        st.Layout(new Constraints(0, 400, 0, float.PositiveInfinity), ctx);

        Close((st.Size.Height - t.Size.Height) / 2, t.Offset.Y);
    }

    [Fact]
    public void VStack_HAlignEnd_AlignsToStackWidth()
    {
        LayoutContext ctx = Ctx();
        Button wide = Button(_ => { }, "Wide button");
        Button narrow = Button(_ => { }, "N", hAlign: Align.End);
        StackPanel st = VStack()[wide, narrow];

        st.Layout(new Constraints(0, 500, 0, 300), ctx);

        Close(st.Size.Width, wide.Size.Width);                       // 幅 = 最大子幅 (shrink-wrap)
        Close(st.Size.Width - narrow.Size.Width, narrow.Offset.X);   // End は自分の右端基準
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
