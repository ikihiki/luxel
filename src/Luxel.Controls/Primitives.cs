using System.Numerics;
using Luxel.Graphics.TwoD;
using Luxel.UI;

namespace Luxel.Controls;

/// <summary>角丸塗りボックス (子なし)。サイズは共通引数 (width/height)、伸縮は hAlign/vAlign: Stretch。</summary>
[UiComponent]
public sealed partial class Box : Widget
{
    /// <summary>塗り色。未設定 → テーマ SurfaceAlt。</summary>
    [UiParam] private readonly Bindable<uint> _background = new();
    /// <summary>角丸半径 (px)。</summary>
    [UiParam] private readonly Bindable<float> _rounded = new();

    private static float Fin(float v) => float.IsInfinity(v) ? 0 : v;

    protected override void PerformLayout(Constraints c, LayoutContext ctx)
    {
        float w = HAlign.Get() == Align.Stretch ? Fin(c.MaxW) : ResolveW(c, ctx, 0);
        float h = VAlign.Get() == Align.Stretch ? Fin(c.MaxH) : ResolveH(c, ctx, 0);
        Size = c.Constrain(new Size(w, h));
    }

    public override float MaxIntrinsicWidth(float height, LayoutContext ctx) => ResolveWIntrinsic(ctx, 0);

    protected override void RealizeCore(UiBuildContext ctx, UiNode parent, Point worldOrigin)
    {
        UiNode node = CreateRoot(ctx, parent, worldOrigin);
        var s = new Scene2D();
        float r = Rounded.Get();
        if (r > 0) s.FillRoundedRect(Color2D.White, 0, 0, Size.Width, Size.Height, MathF.Min(r, MathF.Min(Size.Width, Size.Height) / 2));
        else s.FillRect(Color2D.White, 0, 0, Size.Width, Size.Height);
        node.Content = s;
        ctx.Effect(() => node.Color = Background.Or(ctx.Theme.Value.SurfaceAlt));
    }
}

public enum IconKind { Check, Close, ChevronDown, ChevronRight, ChevronLeft, Plus, Minus, Dot, Circle }

/// <summary>ベクターアイコン (24x24 ビューボックスのパスを size に拡大)。色はテーマ束縛可。</summary>
[UiComponent]
public sealed partial class Icon : Widget
{
    /// <summary>アイコン種別。</summary>
    [UiParam] private readonly Bindable<IconKind> _kind = new();
    /// <summary>表示サイズ (px、正方形)。基底 <see cref="Widget.Size"/> と衝突するため iconSize。</summary>
    [UiParam] private readonly Bindable<float> _iconSize = 20f;
    /// <summary>線の太さ (px)。</summary>
    [UiParam] private readonly Bindable<float> _stroke = 2f;

    /// <summary>クリック (任意)。ハンドラがあるときだけヒット登録する — ツリーのシェブロン等、
    /// アイコン単体をボタンにしたい場面用 (Hand カーソル、当たりは少し広め)。</summary>
    [UiEvent] public UiEvent<Icon> OnClick;

    /// <summary>色。未設定 → テーマ Text。</summary>
    [UiParam] private readonly Bindable<uint> _color = new();

    protected override void PerformLayout(Constraints c, LayoutContext ctx)
    {
        float size = IconSize.Get();
        Size = c.Constrain(new Size(size, size));
    }

    public override float MaxIntrinsicWidth(float height, LayoutContext ctx) => IconSize.Get();

    protected override void RealizeCore(UiBuildContext ctx, UiNode parent, Point worldOrigin)
    {
        float size = IconSize.Get();
        UiNode node = CreateRoot(ctx, parent, worldOrigin);
        var s = new Scene2D();
        Draw(s, Kind.Get(), size, Stroke.Get());
        node.Content = s;
        ctx.Effect(() => node.Color = Color.Or(ctx.Theme.Value.Text));
        if (OnClick.HasHandler)
            ctx.AddHit(node, new Rect(-2, -2, size + 4, size + 4),
                onClick: () => OnClick.Invoke(this), cursor: CursorKind.Hand);
    }

    private static void Draw(Scene2D s, IconKind k, float z, float w)
    {
        Vector2 P(float x, float y) => new(x * z, y * z);   // 0..1 正規化座標
        switch (k)
        {
            case IconKind.Check: s.StrokePolyline(Color2D.White, w, P(0.20f, 0.55f), P(0.43f, 0.78f), P(0.80f, 0.26f)); break;
            case IconKind.Close:
                s.StrokeLine(Color2D.White, w, 0.24f * z, 0.24f * z, 0.76f * z, 0.76f * z);
                s.StrokeLine(Color2D.White, w, 0.76f * z, 0.24f * z, 0.24f * z, 0.76f * z); break;
            case IconKind.ChevronDown: s.StrokePolyline(Color2D.White, w, P(0.25f, 0.38f), P(0.5f, 0.64f), P(0.75f, 0.38f)); break;
            case IconKind.ChevronRight: s.StrokePolyline(Color2D.White, w, P(0.40f, 0.25f), P(0.66f, 0.5f), P(0.40f, 0.75f)); break;
            case IconKind.ChevronLeft: s.StrokePolyline(Color2D.White, w, P(0.60f, 0.25f), P(0.34f, 0.5f), P(0.60f, 0.75f)); break;
            case IconKind.Plus:
                s.StrokeLine(Color2D.White, w, 0.5f * z, 0.22f * z, 0.5f * z, 0.78f * z);
                s.StrokeLine(Color2D.White, w, 0.22f * z, 0.5f * z, 0.78f * z, 0.5f * z); break;
            case IconKind.Minus: s.StrokeLine(Color2D.White, w, 0.22f * z, 0.5f * z, 0.78f * z, 0.5f * z); break;
            case IconKind.Dot: s.FillCircle(Color2D.White, 0.5f * z, 0.5f * z, 0.18f * z); break;
            case IconKind.Circle: s.FillCircle(Color2D.White, 0.5f * z, 0.5f * z, 0.42f * z); break;
        }
    }
}
