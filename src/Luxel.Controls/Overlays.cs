using Luxel.Animation;
using Luxel.TwoD;
using Luxel.UI;
using static Luxel.Controls.Kit;

namespace Luxel.Controls;

/// <summary>モーダルダイアログ (中央, scrim, Esc/外側で閉じる)。インラインは 0 サイズの portal。</summary>
[UiComponent]
public sealed partial class Dialog : Widget
{
    private readonly Signal<bool> _open;
    private readonly Widget _panel;
    internal Dialog(Signal<bool> open, Widget panel) { _open = open; _panel = panel; }

    protected override void PerformLayout(Constraints c, LayoutContext ctx) => Size = Size.Zero;
    protected override void RealizeCore(UiBuildContext ctx, UiNode parent, Point worldOrigin)
        => ctx.RegisterOverlay(new OverlayEntry { Open = _open, Content = _panel, Placement = OverlayPlacement.Center, Modal = true });
}

/// <summary>コーナートースト (右下)。portal。</summary>
[UiComponent]
public sealed partial class Toast : Widget
{
    private readonly Signal<bool> _open;
    private readonly Widget _content;
    internal Toast(Signal<bool> open, Widget content) { _open = open; _content = content; }

    protected override void PerformLayout(Constraints c, LayoutContext ctx) => Size = Size.Zero;
    protected override void RealizeCore(UiBuildContext ctx, UiNode parent, Point worldOrigin)
        => ctx.RegisterOverlay(new OverlayEntry { Open = _open, Content = _content, Placement = OverlayPlacement.CornerBottomRight, DismissOnOutside = false });
}

/// <summary>右端ドロワー (モーダル)。portal。</summary>
[UiComponent]
public sealed partial class Drawer : Widget
{
    private readonly Signal<bool> _open;
    private readonly Widget _panel;
    internal Drawer(Signal<bool> open, Widget panel) { _open = open; _panel = panel; }

    protected override void PerformLayout(Constraints c, LayoutContext ctx) => Size = Size.Zero;
    protected override void RealizeCore(UiBuildContext ctx, UiNode parent, Point worldOrigin)
        => ctx.RegisterOverlay(new OverlayEntry { Open = _open, Content = _panel, Placement = OverlayPlacement.RightEdge, Modal = true });
}

/// <summary>ボタン + ドロップダウンメニュー (アンカー下, 外側クリック/Esc で閉じる)。</summary>
[UiComponent]
public sealed partial class Dropdown : Widget
{
    private readonly string _label;
    private readonly (string label, Action onClick)[] _items;
    private readonly Signal<bool> _open = new(false);
    private Button? _trigger;

    internal Dropdown(string label, params (string label, Action onClick)[] items) { _label = label; _items = items; }

    /// <summary>開閉状態 (検査/外部制御用)。</summary>
    public Signal<bool> Opened => _open;

    protected override void PerformLayout(Constraints c, LayoutContext ctx)
    {
        _trigger ??= Button(_ => _open.Value = !_open.Value, _label, variant: Luxel.UI.Variant.Tonal, intent: Luxel.UI.Intent.Neutral);
        Size = _trigger.Layout(Constraints.LooseW(float.PositiveInfinity, float.PositiveInfinity), ctx, true);
        _trigger.Offset = new Point(0, 0);
    }

    public override float MaxIntrinsicWidth(float height, LayoutContext ctx)
        => (_trigger ??= Button(_ => { }, _label, variant: Luxel.UI.Variant.Tonal, intent: Luxel.UI.Intent.Neutral)).MaxIntrinsicWidth(height, ctx);

    protected override void RealizeCore(UiBuildContext ctx, UiNode parent, Point worldOrigin)
    {
        UiNode node = CreateRoot(ctx, parent, worldOrigin);
        Point world = WorldPos;
        _trigger!.Realize(ctx, node, world);

        var rows = _items.Select(it =>
            (Widget)MenuRow(it.label, _ => { it.onClick(); _open.Value = false; }, hAlign: Align.Stretch));
        Widget menu = Border(background: Bind.From(() => ctx.Theme.Value.Surface), rounded: ctx.Theme.Value.Radius, padding: new Thickness(6))
            [VStack(spacing: 2)[rows.ToArray()]];

        ctx.RegisterOverlay(new OverlayEntry
        {
            Open = _open,
            Content = menu,
            Placement = OverlayPlacement.Below,
            Anchor = () => new Rect(WorldPos.X, WorldPos.Y, Size.Width, Size.Height),
        });
    }
}

/// <summary>メニュー行 (hover ハイライト + クリック)。</summary>
[UiComponent]
public sealed partial class MenuRow : Widget
{
    private readonly string _label;

    /// <summary>行クリック (EV: 第一引数は発火元の MenuRow 自身)。</summary>
    [UiEvent] public UiEvent<MenuRow> OnClick;

    [UiParam] public readonly Bindable<float> FontSize = 15f;
    /// <summary>行の地色。未設定 → hover ? SurfaceAlt : Surface。</summary>
    [UiParam(Stateable = true)] public readonly Bindable<uint> Background = new();
    /// <summary>ラベル色。未設定 → テーマ Text。</summary>
    [UiParam(Stateable = true)] public readonly Bindable<uint> Foreground = new();

    internal MenuRow(string label) => _label = label;

    public override string? DebugDetail => _label;

    protected override void PerformLayout(Constraints c, LayoutContext ctx)
    {
        (float tw, float th) = ctx.Font.Measure(_label, FontSize.Get());
        float w = float.IsInfinity(c.MaxW) ? tw + 24 : MathF.Max(c.MaxW, tw + 24);
        Size = new Size(w, th + 12);
    }

    public override float MaxIntrinsicWidth(float height, LayoutContext ctx) => ctx.Font.Measure(_label, FontSize.Get()).width + 24;

    protected override void RealizeCore(UiBuildContext ctx, UiNode parent, Point worldOrigin)
    {
        UiNode node = CreateRoot(ctx, parent, worldOrigin);
        Point world = WorldPos;

        float fontSize = FontSize.Get();
        var bg = new Scene2D(); bg.FillRoundedRect(Color2D.White, 0, 0, Size.Width, Size.Height, 5);
        node.Content = bg;
        // hover 80ms フェードの状態遷移 (AS-M3)
        UiStates hover = ctx.States(new TransitionTable().Default(new TransitionSpec(0.08f)))
            .AddState("normal", ("t", 0f))
            .AddState("hover", ("t", 1f));
        hover.Start(Hovered.Peek() ? "hover" : "normal");
        ctx.Effect(() => hover.Goto(Hovered.Value ? "hover" : "normal"));
        ctx.Effect(() => node.Color = Background.Or(
            new RgbaTween(ctx.Theme.Value.Surface, ctx.Theme.Value.SurfaceAlt).Lerp(hover.Float("t"))));

        UiNode lbl = ctx.Canvas.AddChild(node); lbl.Z = 1;
        (float _, float th) = ctx.Font.Measure(_label, fontSize);
        lbl.Transform = Affine2D.Translate(12, (Size.Height - th) / 2);
        var ls = new Scene2D(); ctx.Font.AppendText(ls, _label, 0, ctx.Font.Ascent(fontSize), fontSize, Color2D.White);
        lbl.Content = ls;
        ctx.Effect(() => lbl.Color = Foreground.Or(ctx.Theme.Value.Text));

        ctx.AddHit(node, new Rect(0, 0, Size.Width, Size.Height), onClick: () => OnClick.Invoke(this), onHover: h => Hovered.Value = h);
    }
}

/// <summary>子をラップし、hover で上にツールチップを表示。</summary>
[UiComponent]
public sealed partial class Tooltip : Widget
{
    private readonly Widget _child;
    private readonly string _text;
    private readonly Signal<bool> _open = new(false);

    internal Tooltip(Widget child, string text) { _child = child; _text = text; }

    protected override void PerformLayout(Constraints c, LayoutContext ctx)
    {
        Size = _child.Layout(c, ctx, true);
        _child.Offset = new Point(0, 0);
    }

    protected override void RealizeCore(UiBuildContext ctx, UiNode parent, Point worldOrigin)
    {
        UiNode node = CreateRoot(ctx, parent, worldOrigin);
        Point world = WorldPos;
        _child.Realize(ctx, node, world);

        Widget bubble = Border(background: Bind.From(() => ctx.Theme.Value.Text), rounded: 5, padding: new Thickness(8, 5))
            [Text(_text, 13, color: Bind.From(() => ctx.Theme.Value.OnAccent))];

        ctx.AddHit(node, new Rect(0, 0, Size.Width, Size.Height), onHover: h => _open.Value = h);
        ctx.RegisterOverlay(new OverlayEntry
        {
            Open = _open,
            Content = bubble,
            Placement = OverlayPlacement.Above,
            Anchor = () => new Rect(world.X, world.Y, Size.Width, Size.Height),
            DismissOnOutside = false,
        });
    }
}
