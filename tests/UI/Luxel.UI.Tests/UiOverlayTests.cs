using Luxel.Controls;
using Luxel.Graphics.TwoD;
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
        Assert.True(e.DismissOnEscape);
        Assert.False(e.Modal);
    }

    [Fact]
    public void RealizedOverlay_OpenCloseControlsInteractionAndScopeLifetime()
    {
        var open = new Signal<bool>(false);
        int backgroundClicks = 0, overlayClicks = 0;
        var background = new ProbeWidget(200, 120) { OnClick = () => backgroundClicks++ };
        var overlay = new ProbeWidget(80, 40) { OnClick = () => overlayClicks++ };
        using var font = VectorFont.LoadSystem();
        using var canvas = new RetainedCanvas();
        using var host = new UiHost(canvas, font, 200, 120);
        host.SetRoot(new OverlayRoot(background, overlay, open));

        Assert.True(host.Click(100, 60));
        Assert.Equal(1, backgroundClicks);
        Assert.Equal(0, overlayClicks);

        open.Value = true;
        Assert.True(host.Click(100, 60));
        Assert.Equal(1, backgroundClicks);
        Assert.Equal(1, overlayClicks);

        open.Value = false;
        Assert.True(host.Click(100, 60));
        Assert.Equal(2, backgroundClicks);
        Assert.Equal(1, overlayClicks);

        host.SetRoot(new ProbeWidget(200, 120) { OnClick = () => backgroundClicks++ });
        open.Value = true;
        Assert.True(host.Click(100, 60));
        Assert.Equal(3, backgroundClicks);
        Assert.Equal(1, overlayClicks);
    }

    [Fact]
    public void RealizedNonModalOverlay_OutsideClickDismissesBeforeBackgroundDispatch()
    {
        var open = new Signal<bool>(true);
        int backgroundClicks = 0;
        var background = new ProbeWidget(200, 120) { OnClick = () => backgroundClicks++ };
        using var font = VectorFont.LoadSystem();
        using var canvas = new RetainedCanvas();
        using var host = new UiHost(canvas, font, 200, 120);
        host.SetRoot(new OverlayRoot(background, new ProbeWidget(80, 40), open));

        Assert.True(host.PointerDown(10, 10));
        Assert.False(open.Value);
        Assert.Equal(0, backgroundClicks);

        Assert.True(host.PointerDown(10, 10));
        Assert.Equal(1, backgroundClicks);
    }

    [Fact]
    public void RealizedModalOverlay_OutsideClickDismissesBeforeBackgroundDispatch()
    {
        var open = new Signal<bool>(true);
        int backgroundClicks = 0;
        var background = new ProbeWidget(200, 120) { OnClick = () => backgroundClicks++ };
        using var font = VectorFont.LoadSystem();
        using var canvas = new RetainedCanvas();
        using var host = new UiHost(canvas, font, 200, 120);
        host.SetRoot(new OverlayRoot(background, new ProbeWidget(80, 40), open, modal: true));

        Assert.True(host.PointerDown(10, 10));
        Assert.False(open.Value);
        Assert.Equal(0, backgroundClicks);

        Assert.True(host.PointerDown(10, 10));
        Assert.Equal(1, backgroundClicks);
    }

    [Fact]
    public void Modal_TrapsFocusRestoresItAndGatesAllBackgroundInput()
    {
        var open = new Signal<bool>(false);
        int backgroundClicks = 0, contexts = 0, wheels = 0, drops = 0;
        var background = new ProbeWidget(240, 160)
        {
            Focusable = true,
            OnClick = () => backgroundClicks++,
            OnContext = () => contexts++,
            OnWheel = () => wheels++,
            OnDrop = () => drops++,
        };
        var modal = new ProbeWidget(80, 60) { Focusable = true, StartsDrag = true };
        using var font = VectorFont.LoadSystem();
        using var canvas = new RetainedCanvas();
        using var host = new UiHost(canvas, font, 240, 160);
        host.SetRoot(new OverlayRoot(background, modal, open, modal: true, dismissOnOutside: false));

        host.FocusNext();
        Assert.True(background.Focused);
        open.Value = true;
        Assert.False(background.Focused);
        Assert.True(modal.Focused);

        host.FocusNext();
        Assert.True(modal.Focused);
        Assert.True(host.PointerDown(10, 10));
        Assert.True(host.ContextClick(10, 10));
        host.Wheel(10, 10, -120);
        Assert.Equal(0, backgroundClicks);
        Assert.Equal(0, contexts);
        Assert.Equal(0, wheels);

        Assert.True(host.PointerDown(120, 80));
        host.PointerMove(10, 10);
        host.PointerUp(10, 10);
        Assert.Equal(0, drops);

        Assert.True(host.KeyDown(Key.Escape));
        Assert.False(open.Value);
        Assert.False(modal.Focused);
        Assert.True(background.Focused);
    }

    [Fact]
    public void OverlayLayout_IsConstrainedToViewportMargins()
    {
        var open = new Signal<bool>(true);
        var overlay = new ProbeWidget(500, 500);
        using var font = VectorFont.LoadSystem();
        using var canvas = new RetainedCanvas();
        using var host = new UiHost(canvas, font, 100, 80);
        host.SetRoot(new OverlayRoot(new ProbeWidget(100, 80), overlay, open));

        Assert.Equal(68, overlay.LastConstraints.MaxW, 1);
        Assert.Equal(48, overlay.LastConstraints.MaxH, 1);
        Assert.True(canvas.Root.Children.Count >= 2);
        Assert.IsType<RectClip>(canvas.Root.Children[^1].Clip);
    }

    [Fact]
    public void ContextMenu_UsesDynamicOverlayEscapeLifecycle()
    {
        var root = new ContextMenuProbe();
        using var font = VectorFont.LoadSystem();
        using var canvas = new RetainedCanvas();
        using var host = new UiHost(canvas, font, 200, 120);
        host.SetRoot(root);

        Assert.True(host.ContextClick(20, 20));
        Assert.NotNull(root.Context);
        Assert.True(ContextMenu.IsOpen(root.Context!));
        Assert.True(host.KeyDown(Key.Escape));
        Assert.False(ContextMenu.IsOpen(root.Context!));
    }

    private sealed class ContextMenuProbe : Widget
    {
        public UiBuildContext? Context { get; private set; }

        protected override void PerformLayout(Constraints constraints, LayoutContext context)
            => Size = constraints.Constrain(new Size(constraints.MaxW, constraints.MaxH));

        protected override void RealizeCore(UiBuildContext context, UiNode parent, Point worldOrigin)
        {
            Context = context;
            UiNode node = CreateRoot(context, parent, worldOrigin);
            context.AddHit(node, new Rect(0, 0, Size.Width, Size.Height),
                onContext: e => ContextMenu.Open(context, e.ScreenX, e.ScreenY, ("Action", () => { })));
        }
    }

    private sealed class OverlayRoot(
        Widget background,
        Widget overlay,
        Signal<bool> open,
        bool modal = false,
        bool dismissOnOutside = true) : Widget
    {
        public override IEnumerable<Widget> DebugChildren() => [background, overlay];

        protected override void PerformLayout(Constraints constraints, LayoutContext context)
        {
            Size = constraints.Constrain(new Size(constraints.MaxW, constraints.MaxH));
            background.Layout(Constraints.Tight(Size), context, parentUsesSize: true);
            background.Offset = default;
        }

        protected override void RealizeCore(UiBuildContext context, UiNode parent, Point worldOrigin)
        {
            UiNode node = CreateRoot(context, parent, worldOrigin);
            background.Realize(context, node, WorldPos);
            context.RegisterOverlay(new OverlayEntry
            {
                Open = open,
                Content = overlay,
                Modal = modal,
                DismissOnOutside = dismissOnOutside,
            });
        }
    }

    private sealed class ProbeWidget(float width, float height) : Widget
    {
        public Action? OnClick { get; init; }
        public Action? OnContext { get; init; }
        public Action? OnWheel { get; init; }
        public Action? OnDrop { get; init; }
        public bool Focusable { get; init; }
        public bool StartsDrag { get; init; }
        public new bool Focused { get; private set; }

        protected override void PerformLayout(Constraints constraints, LayoutContext context)
            => Size = constraints.Constrain(new Size(width, height));

        protected override void RealizeCore(UiBuildContext context, UiNode parent, Point worldOrigin)
        {
            UiNode node = CreateRoot(context, parent, worldOrigin);
            FocusTarget? focus = Focusable ? context.AddFocusable(f => Focused = f) : null;
            context.AddHit(node, new Rect(0, 0, Size.Width, Size.Height),
                onClick: OnClick,
                focus: focus,
                onDragStart: StartsDrag ? _ => context.Host!.BeginDrag(this, new Scene2D()) : null,
                onContext: OnContext is null ? null : _ => OnContext(),
                onDrop: OnDrop is null ? null : (_, _) => OnDrop());
            if (OnWheel is not null)
                context.AddScroll(node, new Rect(0, 0, Size.Width, Size.Height), _ => OnWheel());
        }
    }
}
