using Luxel.TwoD;
using Luxel.UI;

namespace Luxel.Controls;

/// <summary>
/// ペイン境界のリサイズバー。ドラッグ中は自身がゴーストとして追従し (transform 部分更新)、
/// 離した時点で移動量を <c>onResized(delta)</c> に通知する — 呼び出し側はそこでレイアウトを
/// 作り直す (単一パスレイアウトのため、ドラッグ中の連続リレイアウトはしない設計)。
/// vertical=true は縦バー (左右分割、横ドラッグ)。
/// </summary>
[UiComponent]
public sealed partial class Splitter : Widget
{
    public const float Thickness = 6f;

    /// <summary>true = 縦バー (左右分割、横ドラッグ)。</summary>
    [UiParam] private readonly Bindable<bool> _vertical = new();

    /// <summary>ドラッグ終了時の移動量 (EV: 第一引数は発火元の Splitter 自身)。</summary>
    [UiEvent] public UiEvent<Splitter, float> OnResized;

    public override string? DebugDetail => Vertical.Get() ? "V" : "H";

    private static float Fin(float v, float fallback) => float.IsInfinity(v) ? fallback : v;

    protected override void PerformLayout(Constraints c, LayoutContext ctx)
        => Size = c.Constrain(Vertical.Get()
            ? new Size(Thickness, Fin(c.MaxH, 100))
            : new Size(Fin(c.MaxW, 100), Thickness));

    public override float MaxIntrinsicWidth(float height, LayoutContext ctx) => Vertical.Get() ? Thickness : 0;

    protected override void RealizeCore(UiBuildContext ctx, UiNode parent, Point worldOrigin)
    {
        bool vertical = Vertical.Get();
        UiNode node = CreateRoot(ctx, parent, worldOrigin);

        // 中央の細いバー (hover で強調)
        var s = new Scene2D();
        if (vertical) s.FillRoundedRect(Color2D.White, (Thickness - 2) / 2, 4, 2, Size.Height - 8, 1);
        else s.FillRoundedRect(Color2D.White, 4, (Thickness - 2) / 2, Size.Width - 8, 2, 1);
        node.Content = s;
        ctx.Effect(() => node.Color = Hovered.Value || Pressed.Value ? ctx.Theme.Value.Primary : ctx.Theme.Value.BorderColor);

        ctx.AddHit(node, new Rect(0, 0, Size.Width, Size.Height),
            cursor: vertical ? CursorKind.ResizeH : CursorKind.ResizeV,
            onHover: h => Hovered.Value = h,
            onDragStart: _ => Pressed.Value = true,
            onDrag: e =>
            {
                // 移動量は画面絶対基準の Delta — ゴーストで自ノードを動かしてもローカル座標のように
                // 巻き戻らず発振しない。ゴースト: 自ノードだけ transform で追従 (リレイアウトなし)
                float delta = vertical ? e.DeltaX : e.DeltaY;
                node.Transform = vertical
                    ? Affine2D.Translate(Offset.X + delta, Offset.Y)
                    : Affine2D.Translate(Offset.X, Offset.Y + delta);
            },
            onDragEnd: e =>
            {
                Pressed.Value = false;
                node.Transform = Affine2D.Translate(Offset.X, Offset.Y);
                float delta = vertical ? e.DeltaX : e.DeltaY;
                if (delta != 0) OnResized.Invoke(this, delta);
            });
    }
}
