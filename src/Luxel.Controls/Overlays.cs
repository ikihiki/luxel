using Luxel.Animation;
using Luxel.TwoD;
using Luxel.UI;
using static Luxel.Controls.Kit;

namespace Luxel.Controls;

/// <summary>モーダルダイアログ (中央, scrim, Esc/外側で閉じる)。インラインは 0 サイズの portal。</summary>
[UiComponent]
public sealed partial class Dialog : Widget
{
    /// <summary>開閉 signal (true で表示)。</summary>
    [UiParam] private readonly Bindable<Signal<bool>> _open = new();
    /// <summary>ダイアログ本体のパネル。</summary>
    [UiParam] private readonly Bindable<Widget> _panel = new();

    protected override void PerformLayout(Constraints c, LayoutContext ctx) => Size = Size.Zero;
    protected override void RealizeCore(UiBuildContext ctx, UiNode parent, Point worldOrigin)
        => ctx.RegisterOverlay(new OverlayEntry { Open = Open.Get(), Content = Panel.Get(), Placement = OverlayPlacement.Center, Modal = true });
}

/// <summary>コーナートースト (右下)。portal。</summary>
[UiComponent]
public sealed partial class Toast : Widget
{
    /// <summary>開閉 signal (true で表示)。</summary>
    [UiParam] private readonly Bindable<Signal<bool>> _open = new();
    /// <summary>トーストの中身。</summary>
    [UiParam] private readonly Bindable<Widget> _content = new();

    protected override void PerformLayout(Constraints c, LayoutContext ctx) => Size = Size.Zero;
    protected override void RealizeCore(UiBuildContext ctx, UiNode parent, Point worldOrigin)
        => ctx.RegisterOverlay(new OverlayEntry { Open = Open.Get(), Content = Content.Get(), Placement = OverlayPlacement.CornerBottomRight, DismissOnOutside = false });
}

/// <summary>右端ドロワー (モーダル)。portal。</summary>
[UiComponent]
public sealed partial class Drawer : Widget
{
    /// <summary>開閉 signal (true で表示)。</summary>
    [UiParam] private readonly Bindable<Signal<bool>> _open = new();
    /// <summary>ドロワー本体のパネル。</summary>
    [UiParam] private readonly Bindable<Widget> _panel = new();

    protected override void PerformLayout(Constraints c, LayoutContext ctx) => Size = Size.Zero;
    protected override void RealizeCore(UiBuildContext ctx, UiNode parent, Point worldOrigin)
        => ctx.RegisterOverlay(new OverlayEntry { Open = Open.Get(), Content = Panel.Get(), Placement = OverlayPlacement.RightEdge, Modal = true });
}

/// <summary>ボタン + ドロップダウンメニュー (アンカー下, 外側クリック/Esc で閉じる)。</summary>
[UiComponent]
public sealed partial class Dropdown : Widget
{
    /// <summary>トリガーボタンのラベル。</summary>
    [UiParam] private readonly Bindable<string> _label = "";
    /// <summary>メニュー項目 (ラベル + クリック時アクション) の列。</summary>
    [UiParam] private readonly Bindable<(string label, Action onClick)[]> _items = new([]);

    private readonly Signal<bool> _open = new(false);
    private Button? _trigger;

    /// <summary>開閉状態 (検査/外部制御用)。</summary>
    public Signal<bool> Opened => _open;

    protected override void PerformLayout(Constraints c, LayoutContext ctx)
    {
        _trigger ??= Button(_ => _open.Value = !_open.Value, Label.Get(), variant: Luxel.UI.Variant.Tonal, intent: Luxel.UI.Intent.Neutral);
        Size = _trigger.Layout(Constraints.LooseW(float.PositiveInfinity, float.PositiveInfinity), ctx, true);
        _trigger.Offset = new Point(0, 0);
    }

    public override float MaxIntrinsicWidth(float height, LayoutContext ctx)
        => (_trigger ??= Button(_ => { }, Label.Get(), variant: Luxel.UI.Variant.Tonal, intent: Luxel.UI.Intent.Neutral)).MaxIntrinsicWidth(height, ctx);

    protected override void RealizeCore(UiBuildContext ctx, UiNode parent, Point worldOrigin)
    {
        UiNode node = CreateRoot(ctx, parent, worldOrigin);
        Point world = WorldPos;
        _trigger!.Realize(ctx, node, world);

        var rows = Items.Get().Select(it =>
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
    /// <summary>行ラベル。</summary>
    [UiParam] private readonly Bindable<string> _label = "";

    /// <summary>行クリック (EV: 第一引数は発火元の MenuRow 自身)。</summary>
    [UiEvent] public UiEvent<MenuRow> OnClick;

    [UiParam] private readonly Bindable<float> _fontSize = 15f;
    /// <summary>行の地色。未設定 → hover ? SurfaceAlt : Surface。</summary>
    [UiParam(Stateable = true)] private readonly Bindable<uint> _background = new();
    /// <summary>ラベル色。未設定 → テーマ Text。</summary>
    [UiParam(Stateable = true)] private readonly Bindable<uint> _foreground = new();

    public override string? DebugDetail => Label.Get();

    protected override void PerformLayout(Constraints c, LayoutContext ctx)
    {
        (float tw, float th) = ctx.Font.Measure(Label.Get(), FontSize.Get());
        float w = float.IsInfinity(c.MaxW) ? tw + 24 : MathF.Max(c.MaxW, tw + 24);
        Size = new Size(w, th + 12);
    }

    public override float MaxIntrinsicWidth(float height, LayoutContext ctx) => ctx.Font.Measure(Label.Get(), FontSize.Get()).width + 24;

    protected override void RealizeCore(UiBuildContext ctx, UiNode parent, Point worldOrigin)
    {
        UiNode node = CreateRoot(ctx, parent, worldOrigin);
        Point world = WorldPos;

        string label = Label.Get();
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
        (float _, float th) = ctx.Font.Measure(label, fontSize);
        lbl.Transform = Affine2D.Translate(12, (Size.Height - th) / 2);
        var ls = new Scene2D(); ctx.Font.AppendText(ls, label, 0, ctx.Font.Ascent(fontSize), fontSize, Color2D.White);
        lbl.Content = ls;
        ctx.Effect(() => lbl.Color = Foreground.Or(ctx.Theme.Value.Text));

        ctx.AddHit(node, new Rect(0, 0, Size.Width, Size.Height), onClick: () => OnClick.Invoke(this), onHover: h => Hovered.Value = h);
    }
}

/// <summary>子をラップし、hover で上にツールチップを表示。</summary>
[UiComponent]
public sealed partial class Tooltip : Widget
{
    /// <summary>ラップする子 widget (ホバー判定の対象)。</summary>
    [UiParam] private readonly Bindable<Widget> _child = new();
    /// <summary>ツールチップ本文。</summary>
    [UiParam] private readonly Bindable<string> _text = "";

    private readonly Signal<bool> _open = new(false);

    protected override void PerformLayout(Constraints c, LayoutContext ctx)
    {
        Widget child = Child.Get();
        Size = child.Layout(c, ctx, true);
        child.Offset = new Point(0, 0);
    }

    protected override void RealizeCore(UiBuildContext ctx, UiNode parent, Point worldOrigin)
    {
        UiNode node = CreateRoot(ctx, parent, worldOrigin);
        Point world = WorldPos;
        Child.Get().Realize(ctx, node, world);

        // Kit.Text で完全修飾 — 生成プロパティ Text と名前が衝突するため
        Widget bubble = Border(background: Bind.From(() => ctx.Theme.Value.Text), rounded: 5, padding: new Thickness(8, 5))
            [Kit.Text(Text.Get(), 13, color: Bind.From(() => ctx.Theme.Value.OnAccent))];

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
