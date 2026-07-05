using Luxel.TwoD;
using Luxel.UI;
using Luxel.UI.Styling;
using static Luxel.Controls.Kit;

namespace Luxel.Controls;

/// <summary>単一選択ドロップダウン。選択ラベルを表示し、クリックで候補メニューを開く (オーバーレイ)。</summary>
[UiComponent]
public sealed partial class Select : Widget
{
    /// <summary>候補ラベル一覧。</summary>
    [UiParam] private readonly Bindable<string[]> _options = new([]);
    /// <summary>選択 index の signal (選択で書き戻す)。</summary>
    [UiParam] private readonly Bindable<Signal<int>> _selected = new();

    private readonly Signal<bool> _open = new(false);
    private const float ChevW = 26;
    private float _w, _h = 38, _padX = 12, _fs = 15;   // レイアウト時にテーマから確定

    /// <summary>文字サイズ。未設定 → テーマ Font - 1。</summary>
    [UiParam] private readonly Bindable<float> _fontSize = new();

    private void CacheMetrics(Theme t)
    {
        _h = t.ControlH;
        _padX = t.PadIn + 2;
        _fs = FontSize.Or(t.Font - 1);
    }
    /// <summary>背景色。未設定 → テーマ SurfaceAlt。</summary>
    [UiParam] private readonly Bindable<uint> _background = new();

    /// <summary>背景の丸める角 (入力グループで接合面を直角にする用。LengthField 等が設定)。</summary>
    internal RectCorners BgCorners = RectCorners.All;

    public Signal<bool> Opened => _open;

    protected override void PerformLayout(Constraints c, LayoutContext ctx)
    {
        CacheMetrics(ctx.Theme);
        float maxw = 0;
        foreach (string o in Options.Get()) maxw = MathF.Max(maxw, ctx.Font.Measure(o, _fs).width);
        _w = _padX + maxw + ChevW;
        Size = c.Constrain(new Size(_w, _h));
    }
    public override float MaxIntrinsicWidth(float height, LayoutContext ctx) => _w;

    protected override void RealizeCore(UiBuildContext ctx, UiNode parent, Point worldOrigin)
    {
        UiNode node = CreateRoot(ctx, parent, worldOrigin);
        Point world = WorldPos;

        string[] options = Options.Get();
        Signal<int> sel = Selected.Get();
        Signal<Theme> theme = ctx.Theme;
        FocusRing.Add(ctx, node, -3, -3, _w + 6, _h + 6, 9, Focused);
        var bg = new Scene2D(); bg.FillRoundedRect(Color2D.White, 0, 0, _w, _h, theme.Peek().Radius + 1, BgCorners); node.Content = bg;
        ctx.Effect(() => node.Color = Background.Or(theme.Value.SurfaceAlt));

        // 選択ラベル (signal で更新)
        UiNode label = ctx.Canvas.AddChild(node); label.Z = 1;
        float fontSize = _fs;
        float ascent = ctx.Font.Ascent(fontSize), fh = ctx.Font.Measure("Mg", fontSize).height;
        ctx.Effect(() =>
        {
            var s = new Scene2D();
            ctx.Font.AppendText(s, options[Math.Clamp(sel.Value, 0, options.Length - 1)], _padX, (_h - fh) / 2 + ascent, fontSize, Color2D.White);
            label.Content = s;
        });
        ctx.Effect(() => label.Color = theme.Value.Text);

        UiNode chev = ctx.Canvas.AddChild(node); chev.Z = 1;
        chev.Transform = Affine2D.Translate(_w - ChevW, (_h - 16) / 2);
        var cs = new Scene2D();
        cs.StrokePolyline(Color2D.White, 2, new System.Numerics.Vector2(2, 5), new System.Numerics.Vector2(8, 11), new System.Numerics.Vector2(14, 5));
        chev.Content = cs;
        ctx.Effect(() => chev.Color = theme.Value.TextMuted);

        // メニュー (オーバーレイ)
        var rows = options.Select((o, i) =>
        {
            int idx = i;
            return (Widget)MenuRow(o, _ => { sel.Value = idx; _open.Value = false; }, hAlign: Align.Stretch);
        }).ToArray();
        // width を自身に合わせて固定 — オーバーレイのレイアウトは無制約幅のため、
        // Stretch な行がウィンドウ幅まで広がって clamp で左端に飛ぶのを防ぐ。
        Widget menu = Border(background: Bind.From(() => theme.Value.Surface), rounded: theme.Peek().Radius,
                             padding: new Thickness(6), width: MathF.Max(_w, 120))
            [VStack(spacing: 2)[rows]];

        FocusTarget f = ctx.AddFocusable(onFocus: on => Focused.Value = on, onKey: ev =>
        {
            if (ev.Key is Key.Enter or Key.Space) { _open.Value = !_open.Value; return true; }
            if (ev.Key == Key.Up) { sel.Value = Math.Max(0, sel.Value - 1); return true; }
            if (ev.Key == Key.Down) { sel.Value = Math.Min(options.Length - 1, sel.Value + 1); return true; }
            return false;
        });
        ctx.AddHit(node, new Rect(0, 0, _w, _h), onClick: () => _open.Value = !_open.Value, focus: f);
        ctx.RegisterOverlay(new OverlayEntry
        {
            Open = _open,
            Content = menu,
            Placement = OverlayPlacement.Below,
            Anchor = () => new Rect(WorldPos.X, WorldPos.Y, _w, _h),
        });
    }
}
