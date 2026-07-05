using Luxel.TwoD;
using Luxel.UI;

namespace Luxel.Controls;

/// <summary>
/// 2D システム (Scene2D) を直接描く widget — 2D 描画結果をストーリー/docs に載せる最小の器。
/// <code>
/// Canvas2D(360, 240, draw: s => s.FillRoundedRect(Tw.Blue500, 20, 20, 120, 80, 12))   // 静的
/// Canvas2D(360, 240, animate: (s, t) => s.FillCircle(...))                            // 毎フレーム (t = 累積秒)
/// </code>
/// 描画は UI と同じ保持型キャンバスの 1 ノード (Content = Scene2D) — クリップ/transform/スクロール/
/// MDX 埋め込みがそのまま効く。animate は毎フレーム再エンコード → Content 差し替え
/// (容量スラック内なら in-place 部分更新)。時間は Tick の累積 (wall-clock 禁止) — snap の固定
/// ステップで決定的になる。draw/animate はコールバックだが「描画内容そのもの」なので
/// ctor 引数で受ける (EV の移行対象外: Dropdown items と同じ整理)。
/// </summary>
[UiComponent]
public sealed partial class Canvas2D : Widget
{
    /// <summary>描画領域の幅 (px、最小 1)。</summary>
    [UiParam] private readonly Bindable<float> _canvasWidth = new();
    /// <summary>描画領域の高さ (px、最小 1)。</summary>
    [UiParam] private readonly Bindable<float> _canvasHeight = new();
    /// <summary>静的描画コールバック。</summary>
    [UiParam] private readonly Bindable<Action<Scene2D>> _draw = new();
    /// <summary>毎フレーム描画コールバック (t = 累積秒)。</summary>
    [UiParam] private readonly Bindable<Action<Scene2D, float>> _animate = new();

    private float _t;   // 累積時間 (再実体化をまたいで継続)

    private float W1 => MathF.Max(1, CanvasWidth.Get());
    private float H1 => MathF.Max(1, CanvasHeight.Get());

    public override string? DebugDetail => Animate.Get() is not null ? "animated" : "static";

    protected override void PerformLayout(Constraints c, LayoutContext ctx)
        => Size = c.Constrain(new Size(W1, H1));

    public override float MaxIntrinsicWidth(float height, LayoutContext ctx) => W1;

    protected override void RealizeCore(UiBuildContext ctx, UiNode parent, Point worldOrigin)
    {
        Action<Scene2D>? draw = Draw.Get();
        Action<Scene2D, float>? animate = Animate.Get();
        UiNode node = CreateRoot(ctx, parent, worldOrigin);
        node.ContentColors = true;   // 形状ごとの色を保持 (1 ノード 1 色に畳まない)

        Scene2D Encode(float t)
        {
            var s = new Scene2D();
            draw?.Invoke(s);
            animate?.Invoke(s, t);
            return s;
        }

        node.Content = Encode(_t);
        if (animate is not null)
            ctx.AddAnimation(dt =>
            {
                _t += dt;
                node.Content = Encode(_t);
                return false;
            });
    }
}
