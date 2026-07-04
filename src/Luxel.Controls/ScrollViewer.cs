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
    private readonly ScrollModel _scroll = new();   // 位置はフィールド — 再実体化/リサイズをまたいで生き残る
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
        _scroll.SetLengths(_contentH, _viewH);   // 寸法変更でも位置はクランプで保たれる
    }

    protected override void RealizeCore(UiBuildContext ctx, UiNode parent, Point worldOrigin)
    {
        UiNode node = CreateRoot(ctx, parent, worldOrigin);
        Point world = WorldPos;
        node.Clip = new RectClip(0, 0, _viewW, _viewH);

        // スムーズスクロール (AS-M3): _scroll.Offset は目標、表示は状態機械の動的状態で追従。
        // 状態名 = 入力チャネル — ホイールは平滑、サムドラッグ ("drag") は table で 0ms = 即時
        UiStates scroll = ctx.States(new TransitionTable()
            .On("offset", new TransitionSpec(0.12f))
            .To("drag", new TransitionSpec(0f)));
        scroll.Start("idle", ("offset", _scroll.ClampedPeek));
        string src = "wheel";
        ctx.Effect(() => scroll.Goto(src, ("offset", _scroll.Clamped)));

        UiNode content = ctx.Canvas.AddChild(node);
        ctx.Effect(() => content.Transform = Affine2D.Translate(0, -scroll.Float("offset")));
        Child?.Realize(ctx, content, world);

        // スクロールバー (共通実装 — 表示はスムーズスクロールの動的値に追従、ドラッグは即時チャネル)
        ScrollBars.AttachVertical(ctx, node, _scroll, _viewW, _viewH,
            displayOffset: () => scroll.Float("offset"),
            onDirectChange: () => src = "drag",
            color: ThumbColor);

        ctx.AddScroll(node, new Rect(0, 0, _viewW, _viewH),
            d => { src = "wheel"; _scroll.ScrollBy(-d); });
    }
}
