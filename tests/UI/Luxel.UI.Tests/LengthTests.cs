using Luxel.Controls;
using Luxel.Graphics.TwoD;
using Luxel.Typography;
using Luxel.UI;
using Xunit;
using static Luxel.Controls.Kit;

namespace Luxel.Tests;

public class LengthTests
{
    private static LayoutContext Ctx(float vw = 800, float vh = 480)
        => new() { Font = VectorFont.LoadSystem(), ViewportW = vw, ViewportH = vh };
    private static void Close(float expected, float actual, float tol = 0.6f)
        => Assert.True(MathF.Abs(expected - actual) <= tol, $"expected≈{expected}, actual={actual}");

    [Fact]
    public void Parse_CssForms()
    {
        Assert.Equal(new Length(50, LengthUnit.Percent), (Length)"50%");
        Assert.Equal(new Length(1.5f, LengthUnit.Em), (Length)"1.5em");
        Assert.Equal(new Length(40, LengthUnit.Vw), (Length)"40vw");
        Assert.Equal(new Length(30, LengthUnit.Vh), (Length)"30vh");
        Assert.Equal(new Length(12, LengthUnit.Px), (Length)"12px");
        Assert.Equal(new Length(380, LengthUnit.Px), (Length)"380");
        Assert.Equal(default, (Length)"");                       // 空 = 未指定
        Assert.Equal(new Length(380, LengthUnit.Px), (Length)380);   // 数値 = px
        Assert.False(Length.TryParse("abc", null, out _));
    }

    [Fact]
    public void Resolve_EachUnit()
    {
        LayoutContext ctx = Ctx();
        Close(100, Length.Percent(50).Resolve(200, ctx));
        Close(7, Length.Percent(50).Resolve(float.PositiveInfinity, ctx, fallback: 7));   // 無限基準 → fallback
        Close(ctx.Theme.Font * 1.5f, Length.Em(1.5f).Resolve(0, ctx));
        Close(80, Length.Vw(10).Resolve(0, ctx));
        Close(48, Length.Vh(10).Resolve(0, ctx));
        Close(42, default(Length).Resolve(200, ctx, fallback: 42));                        // 未指定 → fallback
        Close(380, ((Length)380).Resolve(0, ctx));
    }

    [Fact]
    public void Layout_PercentAndViewportWidths()
    {
        LayoutContext ctx = Ctx(vw: 800);
        Box half = Box(width: "50%", height: 10);
        half.Layout(new Constraints(0, 300, 0, 100), ctx);
        Close(150, half.Size.Width);

        Box tenVw = Box(width: "10vw", height: 10);
        tenVw.Layout(new Constraints(0, 300, 0, 100), ctx);
        Close(80, tenVw.Size.Width);

        Box em = Box(width: "10em", height: 10);
        em.Layout(new Constraints(0, 500, 0, 100), ctx);
        Close(ctx.Theme.Font * 10, em.Size.Width);
    }

    [Fact]
    public void Layout_PercentInsideBorder_UsesDeflatedAvail()
    {
        LayoutContext ctx = Ctx();
        Box b = Box(width: "100%", height: 10);
        Border bd = Border(padding: new Thickness(8), width: 200)[b];
        bd.Layout(new Constraints(0, 400, 0, 100), ctx);
        Close(184, b.Size.Width);   // 200 - 8*2
    }
}
