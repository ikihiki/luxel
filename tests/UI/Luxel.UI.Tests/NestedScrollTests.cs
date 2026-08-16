using Luxel.Graphics.TwoD;
using Luxel.Typography;
using Luxel.UI;

namespace Luxel.Tests;

public sealed class NestedScrollTests
{
    [Fact]
    public void Wheel_PrefersDeepestScrollable_WhenParentRegistersLast()
    {
        int outerWheels = 0, innerWheels = 0;
        var inner = new ScrollProbe(100, 100, () => innerWheels++);
        var outer = new ScrollProbe(200, 200, () => outerWheels++, inner);
        using var font = VectorFont.LoadSystem();
        using var host = new UiHost(new RetainedCanvas(), font, 200, 200);
        host.SetRoot(outer);

        host.Wheel(50, 50, -120);

        Assert.Equal(1, innerWheels);
        Assert.Equal(0, outerWheels);
    }

    private sealed class ScrollProbe(float width, float height, Action onWheel, Widget? child = null) : Widget
    {
        public override IEnumerable<Widget> DebugChildren() => child is null ? [] : [child];

        protected override void PerformLayout(Constraints constraints, LayoutContext context)
        {
            Size = constraints.Constrain(new Size(width, height));
            if (child is null) return;
            child.Layout(new Constraints(0, Size.Width, 0, Size.Height), context, parentUsesSize: true);
            child.Offset = new Point(0, 0);
        }

        protected override void RealizeCore(UiBuildContext context, UiNode parent, Point worldOrigin)
        {
            UiNode node = CreateRoot(context, parent, worldOrigin);
            child?.Realize(context, node, WorldPos);
            context.AddScroll(node, new Rect(0, 0, Size.Width, Size.Height), _ => onWheel());
        }
    }
}
