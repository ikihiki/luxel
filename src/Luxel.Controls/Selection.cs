using System.Numerics;
using Luxel.Animation;
using Luxel.Graphics.TwoD;
using Luxel.UI;
using Luxel.UI.Styling;

namespace Luxel.Controls;

/// <summary>等幅セグメントの単一選択。選択ハイライトは transform スライド (部分更新)。←→ で移動。</summary>
[UiComponent(Name = "Segmented")]
public sealed partial class SegmentedControl : Widget
{
    private const float HH = 34, Pad = 16;
    private float _segW;

    /// <summary>セグメントのラベル一覧。</summary>
    [UiParam] private readonly Bindable<string[]> _options = new([]);
    /// <summary>選択 index の signal (クリック/←→ で書き戻す)。</summary>
    [UiParam] private readonly Bindable<Signal<int>> _selected = new();
    [UiParam] private readonly Bindable<float> _fontSize = 15f;
    /// <summary>トラック色。未設定 → テーマ SurfaceAlt。</summary>
    [UiParam(Stateable = true)] private readonly Bindable<uint> _background = new();
    /// <summary>選択ハイライト色。未設定 → テーマ Primary。</summary>
    [UiParam(Stateable = true)] private readonly Bindable<uint> _foreground = new();

    private float SegW(LayoutContext ctx)
    {
        float m = 0;
        foreach (string o in Options.Get()) m = MathF.Max(m, ctx.Font.Measure(o, FontSize.Get()).width);
        return m + Pad * 2;
    }

    protected override void PerformLayout(Constraints c, LayoutContext ctx)
    {
        _segW = SegW(ctx);
        Size = c.Constrain(new Size(_segW * Options.Get().Length, HH));
    }

    public override float MaxIntrinsicWidth(float height, LayoutContext ctx) => SegW(ctx) * Options.Get().Length;

    protected override void RealizeCore(UiBuildContext ctx, UiNode parent, Point worldOrigin)
    {
        UiNode node = CreateRoot(ctx, parent, worldOrigin);
        Point world = WorldPos;
        string[] opts = Options.Get();
        Signal<int> sel = Selected.Get();
        float total = _segW * opts.Length;

        FocusRing.Add(ctx, node, -3, -3, total + 6, HH + 6, (HH + 6) / 2, Focused);

        var bg = new Scene2D(); bg.FillRoundedRect(Color2D.White, 0, 0, total, HH, HH / 2);
        node.Content = bg;
        ctx.Effect(() => node.Color = Background.Or(ctx.Theme.Value.SurfaceAlt));

        UiNode hl = ctx.Canvas.AddChild(node); hl.Z = 1;
        var hs = new Scene2D(); hs.FillRoundedRect(Color2D.White, 2, 2, _segW - 4, HH - 4, (HH - 4) / 2);
        hl.Content = hs;
        ctx.Effect(() => hl.Color = Foreground.Or(ctx.Theme.Value.Primary));
        // 選択ハイライトのスライド — index の動的状態 (AS-M3)
        UiStates st = ctx.States(new TransitionTable().Default(new TransitionSpec(0.16f)));
        st.Start($"s{sel.Peek()}", ("x", sel.Peek() * _segW));
        ctx.Effect(() => { int s = sel.Value; st.Goto($"s{s}", ("x", s * _segW)); });
        ctx.Effect(() => hl.Transform = Affine2D.Translate(st.Float("x"), 0));

        float fontSize = FontSize.Get();
        for (int i = 0; i < opts.Length; i++)
        {
            int idx = i;
            (float tw, float th) = ctx.Font.Measure(opts[i], fontSize);
            UiNode lbl = ctx.Canvas.AddChild(node); lbl.Z = 2;
            lbl.Transform = Affine2D.Translate(i * _segW + (_segW - tw) / 2, (HH - th) / 2);
            var ls = new Scene2D(); ctx.Font.AppendText(ls, opts[i], 0, ctx.Font.Ascent(fontSize), fontSize, Color2D.White);
            lbl.Content = ls;
            ctx.Effect(() => lbl.Color = sel.Value == idx ? ctx.Theme.Value.OnAccent : ctx.Theme.Value.Text);
            ctx.AddHit(node, new Rect(i * _segW, 0, _segW, HH), onClick: () => sel.Value = idx);
        }

        ctx.AddFocusable(onFocus: f => Focused.Value = f, onKey: ev =>
        {
            if (ev.Key == Key.Left) { sel.Value = Math.Max(0, sel.Value - 1); return true; }
            if (ev.Key == Key.Right) { sel.Value = Math.Min(opts.Length - 1, sel.Value + 1); return true; }
            return false;
        });
    }
}

/// <summary>縦並びのラジオ。単一選択 signal。↑↓ で移動、クリックで選択。</summary>
[UiComponent(Name = "Radios")]
public sealed partial class RadioGroup : Widget
{
    private const float Dot = 20, Gap = 9, RowGap = 8;
    private float _rowH;

    /// <summary>選択肢のラベル一覧。</summary>
    [UiParam] private readonly Bindable<string[]> _options = new([]);
    /// <summary>選択 index の signal (クリック/↑↓ で書き戻す)。</summary>
    [UiParam] private readonly Bindable<Signal<int>> _selected = new();
    [UiParam] private readonly Bindable<float> _fontSize = 15f;
    /// <summary>非選択リング色。未設定 → テーマ SurfaceAlt。</summary>
    [UiParam(Stateable = true)] private readonly Bindable<uint> _background = new();
    /// <summary>選択リング色。未設定 → テーマ Primary。</summary>
    [UiParam(Stateable = true)] private readonly Bindable<uint> _foreground = new();

    protected override void PerformLayout(Constraints c, LayoutContext ctx)
    {
        string[] opts = Options.Get();
        (float _, float th) = ctx.Font.Measure("Mg", FontSize.Get());
        _rowH = MathF.Max(Dot, th);
        float maxW = 0;
        foreach (string o in opts) maxW = MathF.Max(maxW, ctx.Font.Measure(o, FontSize.Get()).width);
        Size = c.Constrain(new Size(Dot + Gap + maxW, opts.Length * _rowH + (opts.Length - 1) * RowGap));
    }

    protected override void RealizeCore(UiBuildContext ctx, UiNode parent, Point worldOrigin)
    {
        UiNode node = CreateRoot(ctx, parent, worldOrigin);
        Point world = WorldPos;

        string[] opts = Options.Get();
        Signal<int> sel = Selected.Get();
        FocusRing.Add(ctx, node, -4, -4, Size.Width + 8, Size.Height + 8, 8, Focused);

        float fontSize = FontSize.Get();
        for (int i = 0; i < opts.Length; i++)
        {
            int idx = i;
            float ry = i * (_rowH + RowGap);
            float cy = ry + _rowH / 2;

            UiNode ring = ctx.Canvas.AddChild(node);
            var rs = new Scene2D(); rs.FillCircle(Color2D.White, Dot / 2, cy, Dot / 2);
            ring.Content = rs;
            // 選択切替はリング色クロスフェード + ドットのフェード — on/off 状態遷移 (AS-M3)
            UiStates st = ctx.States(new TransitionTable().Default(new TransitionSpec(0.12f)))
                .AddState("off", ("t", 0f))
                .AddState("on", ("t", 1f));
            st.Start(sel.Peek() == idx ? "on" : "off");
            ctx.Effect(() => st.Goto(sel.Value == idx ? "on" : "off"));
            ctx.Effect(() => ring.Color = new RgbaTween(
                Background.Or(ctx.Theme.Value.SurfaceAlt), Foreground.Or(ctx.Theme.Value.Primary)).Lerp(st.Float("t")));

            UiNode dot = ctx.Canvas.AddChild(node); dot.Z = 1;
            var ds = new Scene2D(); ds.FillCircle(Color2D.White, Dot / 2, cy, Dot / 4);
            dot.Content = ds;
            ctx.Effect(() => dot.Color = ctx.Theme.Value.OnAccent);
            ctx.Effect(() => dot.Opacity = st.Float("t"));

            (float _, float th) = ctx.Font.Measure(opts[i], fontSize);
            UiNode lbl = ctx.Canvas.AddChild(node);
            lbl.Transform = Affine2D.Translate(Dot + Gap, ry + (_rowH - th) / 2);
            var ls = new Scene2D(); ctx.Font.AppendText(ls, opts[i], 0, ctx.Font.Ascent(fontSize), fontSize, Color2D.White);
            lbl.Content = ls;
            ctx.Effect(() => lbl.Color = ctx.Theme.Value.Text);

            ctx.AddHit(node, new Rect(0, ry, Size.Width, _rowH), onClick: () => sel.Value = idx);
        }

        ctx.AddFocusable(onFocus: f => Focused.Value = f, onKey: ev =>
        {
            if (ev.Key == Key.Up) { sel.Value = Math.Max(0, sel.Value - 1); return true; }
            if (ev.Key == Key.Down) { sel.Value = Math.Min(opts.Length - 1, sel.Value + 1); return true; }
            return false;
        });
    }
}

/// <summary>タブストリップ + 切替コンテンツ。コンテンツは重ねて opacity 切替 (部分更新)。←→ で移動。</summary>
[UiComponent]
public sealed partial class Tabs : Widget
{
    private const float StripH = 40, TabPad = 16;
    private float[] _tabX = [];
    private float _stripW;

    /// <summary>タブのラベル一覧。</summary>
    [UiParam] private readonly Bindable<string[]> _labels = new([]);
    /// <summary>各タブのコンテンツ (labels と同数)。</summary>
    [UiParam] private readonly Bindable<Widget[]> _contents = new([]);
    /// <summary>選択 index の signal (クリック/←→ で書き戻す)。</summary>
    [UiParam] private readonly Bindable<Signal<int>> _selected = new();
    [UiParam] private readonly Bindable<float> _fontSize = 15f;
    /// <summary>選択タブ下線色。未設定 → テーマ Primary。</summary>
    [UiParam(Stateable = true)] private readonly Bindable<uint> _foreground = new();

    protected override void PerformLayout(Constraints c, LayoutContext ctx)
    {
        string[] labels = Labels.Get();
        _tabX = new float[labels.Length + 1];
        float x = 0;
        for (int i = 0; i < labels.Length; i++)
        { _tabX[i] = x; x += ctx.Font.Measure(labels[i], FontSize.Get()).width + TabPad * 2; }
        _tabX[labels.Length] = x;
        _stripW = x;

        // Width/Height 指定を尊重 (未指定は従来どおり制約いっぱい / 無限ならフォールバック)
        float w = ResolveW(c, ctx, float.IsInfinity(c.MaxW) ? x : c.MaxW);
        float h = ResolveH(c, ctx, float.IsInfinity(c.MaxH) ? StripH + 120 : c.MaxH);
        Size = c.Constrain(new Size(w, h));
        w = Size.Width; h = Size.Height;
        foreach (Widget ct in Contents.Get())
        {
            ct.Layout(new Constraints(0, w, 0, h - StripH), ctx, parentUsesSize: true);
            ct.Offset = new Point(0, StripH);
        }
    }

    protected override void RealizeCore(UiBuildContext ctx, UiNode parent, Point worldOrigin)
    {
        UiNode node = CreateRoot(ctx, parent, worldOrigin);
        Point world = WorldPos;

        string[] labels = Labels.Get();
        Widget[] contents = Contents.Get();
        Signal<int> sel = Selected.Get();
        FocusRing.Add(ctx, node, -3, -3, _stripW + 6, StripH + 6, 8, Focused);

        // 下線インジケータ (単位幅の矩形を transform で選択タブ幅・位置に)
        UiNode underline = ctx.Canvas.AddChild(node); underline.Z = 2;
        var us = new Scene2D(); us.FillRect(Color2D.White, 0, StripH - 3, 1, 3);   // 単位幅、transform で拡縮移動
        underline.Content = us;
        ctx.Effect(() => underline.Color = Foreground.Or(ctx.Theme.Value.Primary));
        // 下線の位置と幅を 1 つの状態機械で並走遷移 (AS-M3 — タブ幅が違っても滑らかに追従)
        int s0 = Math.Clamp(sel.Peek(), 0, labels.Length - 1);
        UiStates st = ctx.States(new TransitionTable().Default(new TransitionSpec(0.16f)));
        st.Start($"s{s0}", ("x", _tabX[s0]), ("w", _tabX[s0 + 1] - _tabX[s0]));
        ctx.Effect(() =>
        {
            int s = Math.Clamp(sel.Value, 0, labels.Length - 1);
            st.Goto($"s{s}", ("x", _tabX[s]), ("w", _tabX[s + 1] - _tabX[s]));
        });
        ctx.Effect(() => underline.Transform = Affine2D.Mul(Affine2D.Translate(st.Float("x"), 0), Affine2D.Scale(st.Float("w"), 1)));

        float fontSize = FontSize.Get();
        for (int i = 0; i < labels.Length; i++)
        {
            int idx = i;
            (float tw, float th) = ctx.Font.Measure(labels[i], fontSize);
            UiNode lbl = ctx.Canvas.AddChild(node); lbl.Z = 3;
            lbl.Transform = Affine2D.Translate(_tabX[i] + TabPad, (StripH - th) / 2);
            var ls = new Scene2D(); ctx.Font.AppendText(ls, labels[i], 0, ctx.Font.Ascent(fontSize), fontSize, Color2D.White);
            lbl.Content = ls;
            ctx.Effect(() => lbl.Color = sel.Value == idx ? ctx.Theme.Value.Primary : ctx.Theme.Value.TextMuted);
            ctx.AddHit(node, new Rect(_tabX[i], 0, _tabX[i + 1] - _tabX[i], StripH), onClick: () => sel.Value = idx);
        }

        // コンテンツ (重ねて Visible 切替 = Flutter IndexedStack 相当)。
        // Visible はサブツリー継承で描画順 (Order) から除外され、order バッファのみの部分更新で切り替わる。
        // ヒットも IsShown 判定で自動的に非選択タブは不発になる (個別ゲート不要)。
        for (int i = 0; i < contents.Length; i++)
        {
            int idx = i;
            UiNode holder = ctx.Canvas.AddChild(node);
            contents[i].Realize(ctx, holder, world);
            ctx.Effect(() => holder.Visible = sel.Value == idx);
        }

        ctx.AddFocusable(onFocus: f => Focused.Value = f, onKey: ev =>
        {
            if (ev.Key == Key.Left) { sel.Value = Math.Max(0, sel.Value - 1); return true; }
            if (ev.Key == Key.Right) { sel.Value = Math.Min(labels.Length - 1, sel.Value + 1); return true; }
            return false;
        });
    }
}
