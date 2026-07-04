using System.Numerics;
using Luxel.Animation;
using Luxel.TwoD;
using Luxel.UI;

namespace Luxel.Controls;

/// <summary>回転スピナー (アニメ ティックで transform 回転 = 部分更新)。</summary>
[UiComponent]
public sealed partial class Spinner : Widget
{
    private readonly float _size;
    private float _angle;

    /// <summary>色。未設定 → テーマ Primary。</summary>
    [UiParam(Stateable = true)] public readonly Bindable<uint> Color = new();

    [UiCtor]
    internal Spinner(float size = 28) => _size = size;

    protected override void PerformLayout(Constraints c, LayoutContext ctx) => Size = c.Constrain(new Size(_size, _size));
    public override float MaxIntrinsicWidth(float height, LayoutContext ctx) => _size;

    protected override void RealizeCore(UiBuildContext ctx, UiNode parent, Point worldOrigin)
    {
        UiNode node = ctx.Canvas.AddChild(parent);
        float c = _size / 2, r = _size / 2 - 3;
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
    private readonly string _title;
    private readonly Widget _content;
    private readonly Signal<bool> _expanded;
    private const float HeaderH = 40;
    private float _contentH, _w;

    [UiParam] public readonly Bindable<float> FontSize = 16f;
    /// <summary>ヘッダ地色。未設定 → hover ? SurfaceAlt : Surface。</summary>
    [UiParam(Stateable = true)] public readonly Bindable<uint> Background = new();
    /// <summary>タイトル色。未設定 → テーマ Text。</summary>
    [UiParam(Stateable = true)] public readonly Bindable<uint> Foreground = new();

    internal Accordion(string title, Widget content, Signal<bool>? expanded = null)
    { _title = title; _content = content; _expanded = expanded ?? new Signal<bool>(false); }

    public override string? DebugDetail => _title;

    protected override void PerformLayout(Constraints c, LayoutContext ctx)
    {
        _w = float.IsInfinity(c.MaxW) ? 300 : c.MaxW;
        Size cs = _content.Layout(new Constraints(0, _w, 0, float.PositiveInfinity), ctx, true);
        _contentH = cs.Height;
        _content.Offset = new Point(0, 0);
        Size = new Size(_w, HeaderH);       // ヘッダ高のみ (コンテンツは下にオーバーフロー)
    }

    protected override void RealizeCore(UiBuildContext ctx, UiNode parent, Point worldOrigin)
    {
        UiNode node = CreateRoot(ctx, parent, worldOrigin);
        Point world = WorldPos;

        // ヘッダ
        float fontSize = FontSize.Get();
        var hs = new Scene2D(); hs.FillRoundedRect(Color2D.White, 0, 0, _w, HeaderH, 6);
        node.Content = hs;
        ctx.Effect(() => node.Color = Background.Or(Hovered.Value ? ctx.Theme.Value.SurfaceAlt : ctx.Theme.Value.Surface));
        UiNode title = ctx.Canvas.AddChild(node); title.Z = 1;
        (float _, float th) = ctx.Font.Measure(_title, fontSize);
        title.Transform = Affine2D.Translate(12, (HeaderH - th) / 2);
        var ts = new Scene2D(); ctx.Font.AppendText(ts, _title, 0, ctx.Font.Ascent(fontSize), fontSize, Color2D.White);
        title.Content = ts;
        ctx.Effect(() => title.Color = Foreground.Or(ctx.Theme.Value.Text));

        ctx.AddHit(node, new Rect(0, 0, _w, HeaderH),
            onClick: () => _expanded.Value = !_expanded.Value, onHover: h => Hovered.Value = h);

        // コンテンツ (ヘッダ下)。region は固定クリップ、slider を transform でスライドさせて reveal。
        // (祖先クリップを動かすと子の合成クリップが部分更新されないため、クリップは固定し移動で表現)
        UiNode region = ctx.Canvas.AddChild(node);
        region.Transform = Affine2D.Translate(0, HeaderH + 4);
        region.Clip = new RectClip(0, 0, _w, _contentH);          // 固定クリップ
        UiNode slider = ctx.Canvas.AddChild(region);
        _content.Realize(ctx, slider, world + new Point(0, HeaderH + 4));

        // 開度 0..1 — expanded/collapsed の状態遷移 (AS-M3。初回は瞬時、静定後は書き込みゼロ)
        UiStates st = ctx.States(new TransitionTable().Default(new TransitionSpec(0.25f)))
            .AddState("collapsed", ("t", 0f))
            .AddState("expanded", ("t", 1f));
        st.Start(_expanded.Peek() ? "expanded" : "collapsed");
        ctx.Effect(() => st.Goto(_expanded.Value ? "expanded" : "collapsed"));
        ctx.Effect(() => slider.Transform = Affine2D.Translate(0, (st.Float("t") - 1f) * _contentH));   // 0:上に隠れる, 1:見える
    }
}
