using Luxel.Controls;
using Luxel.TwoD;
using Luxel.Typography;
using Luxel.UI;
using Xunit;
using static Luxel.Controls.Kit;

namespace Luxel.Tests;

public class UiOverlayTests
{
    private static LayoutContext Ctx() => new() { Font = VectorFont.LoadSystem() };

    [Fact]
    public void Dialog_IsZeroSizePortal()
    {
        var d = Dialog(new Signal<bool>(false), Text("body"));
        d.Layout(Constraints.Tight(new Size(200, 100)), Ctx());
        Assert.Equal(0, d.Size.Width, 1);
        Assert.Equal(0, d.Size.Height, 1);
    }

    [Fact]
    public void Spinner_HasFixedSize()
    {
        var s = Spinner(32);
        s.Layout(Constraints.LooseW(200, 200), Ctx());
        Assert.Equal(32, s.Size.Width, 1);
        Assert.Equal(32, s.Size.Height, 1);
    }

    [Fact]
    public void OverlayEntry_DefaultsCenterModalDismiss()
    {
        var e = new OverlayEntry { Open = new Signal<bool>(false), Content = Text("x") };
        Assert.Equal(OverlayPlacement.Center, e.Placement);
        Assert.True(e.DismissOnOutside);
        Assert.False(e.Modal);
    }
}
