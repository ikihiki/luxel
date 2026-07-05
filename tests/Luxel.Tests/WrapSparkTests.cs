using Luxel.Controls;
using Luxel.TwoD;
using Luxel.Typography;
using Luxel.UI;
using Xunit;
using static Luxel.Controls.Kit;

namespace Luxel.Tests;

public class WrapSparkTests
{
    private static LayoutContext Ctx() => new() { Font = VectorFont.LoadSystem() };
    private static void Close(float expected, float actual, float tol = 0.6f)
        => Assert.True(MathF.Abs(expected - actual) <= tol, $"expected≈{expected}, actual={actual}");

    [Fact]
    public void WrapPanel_WrapsAtWidth()
    {
        LayoutContext ctx = Ctx();
        // 100px 子 ×5、幅 320、gap 8 → 1 行 3 個 (100*3+8*2=316)、2 行目へ折り返し
        var boxes = Enumerable.Range(0, 5).Select(_ => (Widget)Box(width: 100, height: 20)).ToArray();
        WrapPanel wrap = Wrap(8, 6, width: 320f)[boxes];
        wrap.Layout(new Constraints(0, 400, 0, 400), ctx);

        Close(320, wrap.Size.Width);
        Close(46, wrap.Size.Height);          // 2 行: 20 + 6 + 20
        Close(0, boxes[0].Offset.X);
        Close(108, boxes[1].Offset.X);
        Close(216, boxes[2].Offset.X);
        Close(0, boxes[3].Offset.X);          // 折り返し
        Close(26, boxes[3].Offset.Y);
        Close(108, boxes[4].Offset.X);
    }

    [Fact]
    public void WrapPanel_UsesConstraintWidthWhenUnset()
    {
        LayoutContext ctx = Ctx();
        var boxes = Enumerable.Range(0, 3).Select(_ => (Widget)Box(width: 90, height: 10)).ToArray();
        WrapPanel wrap = Wrap(10, 4)[boxes];
        wrap.Layout(new Constraints(0, 200, 0, 400), ctx);   // 幅未設定 → MaxW=200 で折り返し
        Close(200, wrap.Size.Width);
        Close(24, wrap.Size.Height);          // 2 行: 10 + 4 + 10
        Close(0, boxes[2].Offset.X);
        Close(14, boxes[2].Offset.Y);
    }

    [Fact]
    public void Sparkline_LayoutUsesCtorSize()
    {
        LayoutContext ctx = Ctx();
        Sparkline sp = Sparkline(260, 64);
        sp.SetValues([1f, 2f, 3f]);           // 実体化前の SetValues も安全
        sp.Layout(new Constraints(0, 400, 0, 400), ctx);
        Close(260, sp.Size.Width);
        Close(64, sp.Size.Height);
    }

    [Fact]
    public void Card_BorderGrowsToImageView()
    {
        LayoutContext ctx = Ctx();
        // DevTools の Frame カード構造の再現: Border[VStack[title, VStack[buttons, ImageView(700×460)]]]
        ImageView img = ImageView(700, 460);
        Border card = Border(padding: new Thickness(10, 8))[
            VStack(6)[
                Text("Frame", 12),
                VStack(4)[
                    HStack(6)[Button(_ => { }, "◀"), Text("main", 12, width: 220), Button(_ => { }, "▶")],
                    img]]];
        card.Layout(new Constraints(0, 860, 0, 550), ctx);
        Close(460, img.Size.Height);
        Assert.True(card.Size.Height > 470, $"card too short: {card.Size.Height}");
    }
}
