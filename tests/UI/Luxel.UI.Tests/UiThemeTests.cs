using Luxel.Controls;
using Luxel.Graphics.TwoD;
using Luxel.Typography;
using Luxel.UI;
using Xunit;
using static Luxel.Controls.Kit;

namespace Luxel.Tests;

public class UiThemeTests
{
    private static LayoutContext Ctx() => new() { Font = VectorFont.LoadSystem() };
    private static void Close(float e, float a, float tol = 0.8f)
        => Assert.True(MathF.Abs(e - a) <= tol, $"expected≈{e}, actual={a}");

    [Fact]
    public void Theme_LightAndDark_Differ()
    {
        Assert.NotEqual(Theme.Light.Background, Theme.Dark.Background);
        Assert.NotEqual(Theme.Light.Text, Theme.Dark.Text);
    }

    [Fact]
    public void Theme_UsesWebTypographyScale()
    {
        Theme theme = Theme.Light;

        Assert.Equal(16f, theme.Font);
        Assert.Equal(14f, theme.FontSm);
        Assert.Equal(24f, theme.FontLg);
        Assert.Equal(32f, theme.FontHeading);
    }

    [Fact]
    public void ThemeSnapshotsUseReplaceOnlySemantics()
    {
        Theme original = Theme.Light;
        Theme replacement = original with { Primary = Color2D.Rgba(1, 2, 3) };

        Assert.NotSame(original, replacement);
        Assert.NotEqual(original.Primary, replacement.Primary);
        Assert.Equal(original.Background, replacement.Background);
    }

    [Fact]
    public void CompactTheme_PreservesReadableTypography()
    {
        Theme compact = Theme.Light.Compact();

        Assert.Equal(Theme.Light.Font, compact.Font);
        Assert.Equal(Theme.Light.FontSm, compact.FontSm);
        Assert.Equal(Theme.Light.FontLg, compact.FontLg);
        Assert.Equal(Theme.Light.FontHeading, compact.FontHeading);
    }

    [Fact]
    public void Styles_FilledPrimary_UsesAccentBg()
    {
        VisualStyle vs = Styles.Resolve(Theme.Light, Variant.Filled, Intent.Primary, ControlState.Normal);
        Assert.Equal(Theme.Light.Primary, vs.Bg);
        Assert.Equal(Theme.Light.OnAccent, vs.Fg);
    }

    [Fact]
    public void Styles_OutlineGhost_TransparentBg()
    {
        Assert.Equal(0u, Styles.Resolve(Theme.Light, Variant.Outline, Intent.Primary, ControlState.Normal).Bg);
        Assert.Equal(0u, Styles.Resolve(Theme.Light, Variant.Ghost, Intent.Neutral, ControlState.Normal).Bg);
    }

    [Fact]
    public void Styles_Mix_Midpoint()
    {
        uint mid = Styles.Mix(Color2D.Rgba(0, 0, 0), Color2D.Rgba(200, 100, 50), 0.5f);
        Assert.Equal(Color2D.Rgba(100, 50, 25), mid);
    }

    [Fact]
    public void Grid_HAlignCenter_CentersChild()
    {
        LayoutContext ctx = Ctx();
        Text t = Text("Hi");
        t.HAlign.SetBase(Align.Center);
        Grid g = Grid(columns: [1])[t];
        g.Layout(Constraints.Tight(new Size(200, 60)), ctx);
        float tw = t.Size.Width;
        Close((200 - tw) / 2f, t.Offset.X);
    }

    [Fact]
    public void Margin_OffsetsChildInGrid()
    {
        LayoutContext ctx = Ctx();
        Text t = Text("Hi");
        t.Margin.SetBase(new Thickness(20, 8, 0, 0));    // 左20 上8
        Grid g = Grid(columns: [1])[t];           // 既定 HAlign=Start
        g.Layout(Constraints.Tight(new Size(200, 60)), ctx);
        Close(20, t.Offset.X);
        Close(8, t.Offset.Y);
    }

    [Fact]
    public void Center_CentersChild()
    {
        LayoutContext ctx = Ctx();
        Text t = Text("X");
        var c = Center()[t];
        c.Layout(Constraints.Tight(new Size(100, 100)), ctx);
        Close((100 - t.Size.Width) / 2f, t.Offset.X);
        Close((100 - t.Size.Height) / 2f, t.Offset.Y);
    }
}
