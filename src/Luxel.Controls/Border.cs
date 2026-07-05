using Luxel.TwoD;
using Luxel.UI.Styling;

using Luxel.UI;

namespace Luxel.Controls;

/// <summary>
/// 背景色 + padding + (任意で) クリップを持つ単一子コンテナ。
/// <para>スタイルはすべて [UiParam] な <see cref="Bindable{T}"/> フィールド。状態別の上書きは
/// 生成された <c>.When(WidgetState.Hover, background: ...)</c> が <see cref="Bindable{T}.SetState"/> で積み、
/// fluent <c>.Transition(...)</c> を宣言すると状態変化で自動補間される。</para>
/// </summary>
[UiComponent]
public sealed partial class Border : Widget
{
    internal Widget? Child;

    /// <summary>true で子を自身の矩形でクリップする。</summary>
    [UiParam] private readonly Bindable<bool> _clip = false;
    /// <summary>背景色。値直接 (<c>Tw.Slate100</c>) / Signal / <c>Bind.From(() => theme.Value.Surface)</c>。未設定=透明。</summary>
    [UiParam] private readonly Bindable<uint> _background = new();
    [UiParam] private readonly Bindable<float> _opacity = 1f;
    [UiParam] private readonly Bindable<float> _scale = 1f;
    /// <summary>角丸半径 (px)。</summary>
    [UiParam] private readonly Bindable<float> _rounded = new();
    [UiParam] private readonly Bindable<Thickness> _padding = new();

    /// <summary>子要素の宣言: <c>Border(...) [ child ]</c>。</summary>
    public Border this[Widget child]
    {
        get { AddChild(child); return this; }
    }

    private void AddChild(Widget child) => Child = child;

    public override IEnumerable<Widget> DebugChildren() => Child is null ? [] : [Child];

    protected override void PerformLayout(Constraints c, LayoutContext ctx)
    {
        Thickness pad = Padding.Get();
        if (Child is null)
        {
            float w = ResolveW(c, ctx, pad.Horizontal);
            float h = ResolveH(c, ctx, pad.Vertical);
            Size = c.Constrain(new Size(w, h));
            return;
        }
        // 明示 Width/Height があれば子の利用可能サイズをそれで縛る (CSS の content box 相当 —
        // % の基準もこの幅になる)。未指定は従来どおり入ってきた制約 − padding。
        Length lw = Width.Get(), lh = Height.Get();
        Constraints cc = c.Deflate(pad);
        if (lw.IsSet)
        {
            float inner = MathF.Max(0, lw.Resolve(c.MaxW, ctx, 0) - pad.Horizontal);
            cc = cc with { MinW = MathF.Min(cc.MinW, inner), MaxW = inner };
        }
        if (lh.IsSet)
        {
            float inner = MathF.Max(0, lh.Resolve(c.MaxH, ctx, 0) - pad.Vertical);
            cc = cc with { MinH = MathF.Min(cc.MinH, inner), MaxH = inner };
        }
        Size cs = Child.Layout(cc, ctx, parentUsesSize: true);
        Child.Offset = new Point(pad.Left, pad.Top);
        float fw = ResolveW(c, ctx, cs.Width + pad.Horizontal);
        float fh = ResolveH(c, ctx, cs.Height + pad.Vertical);
        Size = c.Constrain(new Size(fw, fh));
    }

    public override float MaxIntrinsicWidth(float height, LayoutContext ctx)
        => (Child?.MaxIntrinsicWidth(height, ctx) ?? 0) + Padding.Get().Horizontal;

    protected override void RealizeCore(UiBuildContext ctx, UiNode parent, Point worldOrigin)
    {
        UiNode node = CreateRoot(ctx, parent, worldOrigin, out TransformHandle tf);
        Point world = WorldPos;

        var s = new Scene2D();
        float r = Rounded.Get();
        if (r > 0) s.FillRoundedRect(Color2D.White, 0, 0, Size.Width, Size.Height, r);
        else s.FillRect(Color2D.White, 0, 0, Size.Width, Size.Height);
        node.Content = s;
        if (Clip.Get()) node.Clip = new RectClip(0, 0, Size.Width, Size.Height);

        // 配色: 状態変化 (signal) / テーマ変化で recolor を部分更新。
        // transform 成分 (TF) は CreateRoot が配線済み、一様 Scale はその handle へ合成。
        uint currentBg = 0x00000000u;
        float currentOpacity = 1f;

        void ApplyAll() => node.Color = StyleApply.MultiplyAlpha(currentBg, currentOpacity);

        Action<uint> bgSet = WrapSetter<uint>("Background", v => { currentBg = v; ApplyAll(); });
        Action<float> scSet = WrapSetter<float>("Scale", tf.SetExtraScale);
        Action<float> opSet = WrapSetter<float>("Opacity", v => { currentOpacity = v; ApplyAll(); });

        ctx.Effect(() =>
        {
            bgSet(Background.Get());
            scSet(Scale.Get());
            opSet(Opacity.Get());
        });

        Child?.Realize(ctx, node, world);
    }
}
