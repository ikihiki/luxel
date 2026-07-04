using Luxel.Animation;
using Luxel.TwoD;

using Luxel.UI;

namespace Luxel.Controls;

/// <summary>
/// 縦スクロール領域。子を無限高で測ってクリップし、スクロールオフセットを content ノードの
/// transform に反映 (オフセット変更 = transform のみ部分更新)。wheel/キーで操作。
/// </summary>
[UiComponent(Name = "Scroll")]
public sealed partial class ScrollViewer : Widget
{
    internal Widget? Child;
    private readonly float _height;
    private readonly Signal<float> _offset = new(0);
    private float _viewW, _viewH, _contentH;

    /// <summary>スクロールバー (thumb) の色。未設定 → テーマ BorderColor。</summary>
    [UiParam(Stateable = true)] public readonly Bindable<uint> ThumbColor = new();

    [UiCtor]
    internal ScrollViewer(float height) { _height = height; }

    private void AddChild(Widget c) => Child = c;

    /// <summary>子要素の宣言: <c>Scroll(130)[ content ]</c>。</summary>
    public ScrollViewer this[Widget child]
    {
        get { AddChild(child); return this; }
    }

    public override IEnumerable<Widget> DebugChildren() => Child is null ? [] : [Child];

    private float MaxScroll => MathF.Max(0, _contentH - _viewH);
    private float Clamped() => Math.Clamp(_offset.Value, 0, MaxScroll);

    protected override void PerformLayout(Constraints c, LayoutContext ctx)
    {
        // Width 指定を優先 (HStack 等の無限幅コンテキストでも成立)。未指定は親の制約幅いっぱい。
        _viewW = ResolveW(c, ctx, float.IsInfinity(c.MaxW) ? 0 : c.MaxW);
        _viewH = ResolveH(c, ctx, _height);
        Size = c.Constrain(new Size(_viewW, _viewH));
        if (Child != null)
        {
            Size cs = Child.Layout(new Constraints(0, _viewW, 0, float.PositiveInfinity), ctx, parentUsesSize: true);
            _contentH = cs.Height;
            Child.Offset = new Point(0, 0);
        }
    }

    protected override void RealizeCore(UiBuildContext ctx, UiNode parent, Point worldOrigin)
    {
        UiNode node = CreateRoot(ctx, parent, worldOrigin);
        Point world = WorldPos;
        node.Clip = new RectClip(0, 0, _viewW, _viewH);

        // スムーズスクロール (AS-M3): _offset は目標、表示は状態機械の動的状態で追従。
        // 状態名 = 入力チャネル — ホイールは平滑、サムドラッグ ("drag") は table で 0ms = 即時
        UiStates scroll = ctx.States(new TransitionTable()
            .On("offset", new TransitionSpec(0.12f))
            .To("drag", new TransitionSpec(0f)));
        scroll.Start("idle", ("offset", Clamped()));
        string src = "wheel";
        ctx.Effect(() => scroll.Goto(src, ("offset", Clamped())));

        UiNode content = ctx.Canvas.AddChild(node);
        ctx.Effect(() => content.Transform = Affine2D.Translate(0, -scroll.Float("offset")));
        Child?.Realize(ctx, content, world);

        if (MaxScroll > 0)
        {
            const float trackW = 6, pad = 2;
            float thumbH = MathF.Max(28, _viewH * _viewH / _contentH);
            UiNode bar = ctx.Canvas.AddChild(node);
            bar.Z = 2;
            var bs = new Scene2D();
            bs.FillRoundedRect(Color2D.White, _viewW - trackW - pad, 0, trackW, thumbH, trackW / 2);
            bar.Content = bs;
            ctx.Effect(() => bar.Color = ThumbColor.Or(ctx.Theme.Value.BorderColor));
            ctx.Effect(() =>
            {
                float frac = MaxScroll > 0 ? scroll.Float("offset") / MaxScroll : 0;
                bar.Transform = Affine2D.Translate(0, frac * (_viewH - thumbH));
            });

            // スクロールバーのドラッグ (サム掴み / トラック押下はジャンプしてそのまま掴む)。
            // 掴み判定は描画幅より広め。行のヒットより後に登録するので前面が勝つ。
            const float grabW = trackW + pad * 2 + 6;
            float grabOffset = 0;
            void SetFromThumbTop(float top)
            {
                src = "drag";   // 直接操作 — table の To("drag") = 0ms で即時
                _offset.Value = Math.Clamp(top / (_viewH - thumbH), 0, 1) * MaxScroll;
            }
            ctx.AddHit(node, new Rect(_viewW - grabW, 0, grabW, _viewH),
                onDragStart: (_, ly) =>
                {
                    float thumbTop = (Clamped() / MaxScroll) * (_viewH - thumbH);
                    bool onThumb = ly >= thumbTop && ly <= thumbTop + thumbH;
                    grabOffset = onThumb ? ly - thumbTop : thumbH / 2;
                    SetFromThumbTop(ly - grabOffset);
                },
                onDrag: (_, ly) => SetFromThumbTop(ly - grabOffset));
        }

        ctx.AddScroll(node, new Rect(0, 0, _viewW, _viewH),
            d => { src = "wheel"; _offset.Value = Math.Clamp(_offset.Value - d, 0, MaxScroll); });
    }
}
