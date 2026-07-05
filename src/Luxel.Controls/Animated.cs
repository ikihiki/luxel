using System.Numerics;
using Luxel.Animation;
using Luxel.TwoD;
using Luxel.UI;

namespace Luxel.Controls;

/// <summary>回転スピナー (アニメ ティックで transform 回転 = 部分更新)。</summary>
[UiComponent]
public sealed partial class Spinner : Widget
{
    private float _angle;

    /// <summary>直径 (px)。基底 <see cref="Widget.Size"/> と衝突するため spinnerSize。</summary>
    [UiParam] private readonly Bindable<float> _spinnerSize = 28f;
    /// <summary>色。未設定 → テーマ Primary。</summary>
    [UiParam(Stateable = true)] private readonly Bindable<uint> _color = new();

    protected override void PerformLayout(Constraints c, LayoutContext ctx)
    {
        float size = SpinnerSize.Get();
        Size = c.Constrain(new Size(size, size));
    }

    public override float MaxIntrinsicWidth(float height, LayoutContext ctx) => SpinnerSize.Get();

    protected override void RealizeCore(UiBuildContext ctx, UiNode parent, Point worldOrigin)
    {
        float size = SpinnerSize.Get();
        UiNode node = ctx.Canvas.AddChild(parent);
        float c = size / 2, r = size / 2 - 3;
        var s = new Scene2D();
        const int N = 24; var pts = new Vector2[N];
        for (int i = 0; i < N; i++) { float a = 0.6f + (float)i / (N - 1) * 4.9f; pts[i] = new Vector2(c + r * MathF.Cos(a), c + r * MathF.Sin(a)); }
        s.StrokePolyline(Color2D.White, 3f, pts);
        node.Content = s;
        ctx.Effect(() => node.Color = Color.Or(ctx.Theme.Value.Primary));

        SetWorldPos(worldOrigin + Offset);
        ctx.AddAnimation(dt =>
        {
            _angle += dt * 5f;
            node.Transform = Affine2D.Mul(Affine2D.Translate(Offset.X + c, Offset.Y + c),
                              Affine2D.Mul(Affine2D.Rotate(_angle), Affine2D.Translate(-c, -c)));
            return false;   // 連続
        });
    }
}

/// <summary>
/// 折りたたみ。ヘッダクリックで展開トグル。コンテンツはクリップ高さをアニメ (reveal)。
/// 注: 単一パスのため周囲の再レイアウトはしない (末尾配置向け)。Size はヘッダ高のみ。
/// </summary>
[UiComponent]
public sealed partial class Accordion : Widget
{
    private const float HeaderH = 40;
    private float _contentH, _w;
    private Signal<bool>? _expandedFallback;   // expanded 未指定時の内部状態 (旧 ctor の new Signal<bool>(false) 相当)

    /// <summary>ヘッダに表示するタイトル。</summary>
    [UiParam] private readonly Bindable<string> _title = "";
    /// <summary>展開時に表示するコンテンツ。</summary>
    [UiParam] private readonly Bindable<Widget> _content = new();
    /// <summary>展開状態の外部 Signal。未指定なら内部で保持 (初期値 false)。</summary>
    [UiParam] private readonly Bindable<Signal<bool>> _expanded = new();
    [UiParam] private readonly Bindable<float> _fontSize = 16f;
    /// <summary>ヘッダ地色。未設定 → hover ? SurfaceAlt : Surface。</summary>
    [UiParam(Stateable = true)] private readonly Bindable<uint> _background = new();
    /// <summary>タイトル色。未設定 → テーマ Text。</summary>
    [UiParam(Stateable = true)] private readonly Bindable<uint> _foreground = new();

    /// <summary>展開状態: 外部 Signal 優先、未指定なら内部 fallback (widget 生存中は同一インスタンス)。</summary>
    private Signal<bool> ExpandedSignal => Expanded.Get() ?? (_expandedFallback ??= new Signal<bool>(false));

    public override string? DebugDetail => Title.Get();

    protected override void PerformLayout(Constraints c, LayoutContext ctx)
    {
        Widget content = Content.Get();
        _w = float.IsInfinity(c.MaxW) ? 300 : c.MaxW;
        Size cs = content.Layout(new Constraints(0, _w, 0, float.PositiveInfinity), ctx, true);
        _contentH = cs.Height;
        content.Offset = new Point(0, 0);
        Size = new Size(_w, HeaderH);       // ヘッダ高のみ (コンテンツは下にオーバーフロー)
    }

    protected override void RealizeCore(UiBuildContext ctx, UiNode parent, Point worldOrigin)
    {
        UiNode node = CreateRoot(ctx, parent, worldOrigin);
        Point world = WorldPos;
        string titleText = Title.Get();
        Signal<bool> expanded = ExpandedSignal;

        // ヘッダ
        float fontSize = FontSize.Get();
        var hs = new Scene2D(); hs.FillRoundedRect(Color2D.White, 0, 0, _w, HeaderH, 6);
        node.Content = hs;
        ctx.Effect(() => node.Color = Background.Or(Hovered.Value ? ctx.Theme.Value.SurfaceAlt : ctx.Theme.Value.Surface));
        UiNode title = ctx.Canvas.AddChild(node); title.Z = 1;
        (float _, float th) = ctx.Font.Measure(titleText, fontSize);
        title.Transform = Affine2D.Translate(12, (HeaderH - th) / 2);
        var ts = new Scene2D(); ctx.Font.AppendText(ts, titleText, 0, ctx.Font.Ascent(fontSize), fontSize, Color2D.White);
        title.Content = ts;
        ctx.Effect(() => title.Color = Foreground.Or(ctx.Theme.Value.Text));

        ctx.AddHit(node, new Rect(0, 0, _w, HeaderH),
            onClick: () => expanded.Value = !expanded.Value, onHover: h => Hovered.Value = h);

        // コンテンツ (ヘッダ下)。region は固定クリップ、slider を transform でスライドさせて reveal。
        // (祖先クリップを動かすと子の合成クリップが部分更新されないため、クリップは固定し移動で表現)
        UiNode region = ctx.Canvas.AddChild(node);
        region.Transform = Affine2D.Translate(0, HeaderH + 4);
        region.Clip = new RectClip(0, 0, _w, _contentH);          // 固定クリップ
        UiNode slider = ctx.Canvas.AddChild(region);
        Content.Get().Realize(ctx, slider, world + new Point(0, HeaderH + 4));

        // 開度 0..1 — expanded/collapsed の状態遷移 (AS-M3。初回は瞬時、静定後は書き込みゼロ)
        UiStates st = ctx.States(new TransitionTable().Default(new TransitionSpec(0.25f)))
            .AddState("collapsed", ("t", 0f))
            .AddState("expanded", ("t", 1f));
        st.Start(expanded.Peek() ? "expanded" : "collapsed");
        ctx.Effect(() => st.Goto(expanded.Value ? "expanded" : "collapsed"));
        ctx.Effect(() => slider.Transform = Affine2D.Translate(0, (st.Float("t") - 1f) * _contentH));   // 0:上に隠れる, 1:見える
    }
}
