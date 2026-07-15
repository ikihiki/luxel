using Luxel.TwoD;
using Luxel.Typography;
using Luxel.UI;
using Xunit;

namespace Luxel.Tests;

public class UiFrameDemandTests
{
    [Fact]
    public void RetainedCanvasChange_RaisesHostFrameRequest()
    {
        using var canvas = new RetainedCanvas();
        using VectorFont font = VectorFont.LoadSystem();
        using var host = new UiHost(canvas, font, 100, 100);
        int requests = 0;
        host.FrameRequested += () => requests++;

        UiNode node = canvas.AddChild(canvas.Root);
        node.Opacity = 0.5f;

        Assert.True(requests >= 2);
        Assert.True(host.NeedsFrame);
    }

    [Fact]
    public void MarkNeedsRealize_RaisesFrameRequest()
    {
        using var canvas = new RetainedCanvas();
        using VectorFont font = VectorFont.LoadSystem();
        using var host = new UiHost(canvas, font, 100, 100);
        var root = new AnimationWidget();
        host.SetRoot(root);
        int requests = 0;
        host.FrameRequested += () => requests++;

        root.MarkNeedsRealize();

        Assert.Equal(1, requests);
        Assert.True(host.HasPendingRealize);
    }

    [Fact]
    public void AnimationActivityPredicate_SleepsResidentTickerWhenIdle()
    {
        using var canvas = new RetainedCanvas();
        using VectorFont font = VectorFont.LoadSystem();
        using var host = new UiHost(canvas, font, 100, 100);
        var root = new AnimationWidget();
        host.SetRoot(root);

        Assert.False(host.HasActiveAnimations);
        host.AdvanceAnimations(1f / 60);
        Assert.Equal(0, root.Steps);

        root.Active = true;
        Assert.True(host.HasActiveAnimations);
        host.AdvanceAnimations(1f / 60);
        Assert.Equal(1, root.Steps);
    }

    private sealed class AnimationWidget : Widget
    {
        public bool Active;
        public int Steps;

        protected override void PerformLayout(Constraints constraints, LayoutContext context)
            => Size = constraints.Constrain(new Size(10, 10));

        protected override void RealizeCore(UiBuildContext context, UiNode parent, Point worldOrigin)
            => context.AddAnimation(_ => { Steps++; return false; }, () => Active);
    }
}
