using Luxel.Controls;
using Luxel.Graphics.TwoD;
using Luxel.Typography;
using Luxel.UI;
using Luxel.UI.Styling;
using Xunit;
using static Luxel.Controls.Kit;

namespace Luxel.Tests;

public sealed class SlotLifecycleTests
{
    private sealed class ProbeWidget : Widget
    {
        public Theme? LayoutTheme { get; private set; }

        protected override void PerformLayout(Constraints constraints, LayoutContext context)
        {
            LayoutTheme = context.Theme;
            Size = constraints.Constrain(new Size(12, 12));
        }

        protected override void RealizeCore(UiBuildContext context, UiNode parent, Point worldOrigin) { }
    }

    [Fact]
    public void TextFieldSlot_UsesOwnerThemeContext()
    {
        Theme ownerTheme = Theme.Dark.Compact() with { Space = 3 };
        var probe = new ProbeWidget();
        TextField field = TextField(new Signal<string>("query"))[
            TextFieldSlot.Leading(() => probe)];

        field.Layout(new Constraints(0, 300, 0, 100), new LayoutContext
        {
            Font = VectorFont.LoadSystem(),
            Theme = ownerTheme,
        });

        Assert.Same(ownerTheme, probe.LayoutTheme);
    }

    [Fact]
    public void TextFieldSlot_ReplacementDisposesOwnedSubtree()
    {
        var first = new ProbeWidget();
        var replacement = new ProbeWidget();
        TextField field = TextField(new Signal<string>());
        field.SetSlot(TextFieldSlotKey.Leading, () => first);
        var context = new UiBuildContext { Canvas = null!, Font = null! };
        first.Realize(context, new UiNode(null!), default);

        field.SetSlot(TextFieldSlotKey.Leading, () => replacement);

        Assert.True(first.Scope!.IsDisposed);
        Assert.Same(replacement, Assert.Single(field.DebugChildren()));
    }

    [Fact]
    public void SlotPart_TargetMismatchThrowsInsteadOfBeingIgnored()
    {
        TextField field = TextField(new Signal<string>());
        ISlotPart sliderSlot = SliderSlot.Knob(() => new ProbeWidget());

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => sliderSlot.ApplyTo(field));

        Assert.Contains(nameof(SliderSlotKey), error.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(TextField), error.Message, StringComparison.Ordinal);
    }
}
