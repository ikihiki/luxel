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
    private readonly float _w, _h;
    private readonly Action<Scene2D>? _draw;
    private readonly Action<Scene2D, float>? _animate;
    private float _t;   // 累積時間 (再実体化をまたいで継続)

    [UiCtor]
    internal Canvas2D(float width, float height,
                      Action<Scene2D>? draw = null, Action<Scene2D, float>? animate = null)
    {
        _w = MathF.Max(1, width);
        _h = MathF.Max(1, height);
        _draw = draw;
        _animate = animate;
    }

    public override string? DebugDetail => _animate is not null ? "animated" : "static";

    protected override void PerformLayout(Constraints c, LayoutContext ctx)
        => Size = c.Constrain(new Size(_w, _h));

    public override float MaxIntrinsicWidth(float height, LayoutContext ctx) => _w;

    protected override void RealizeCore(UiBuildContext ctx, UiNode parent, Point worldOrigin)
    {
        UiNode node = CreateRoot(ctx, parent, worldOrigin);
        node.ContentColors = true;   // 形状ごとの色を保持 (1 ノード 1 色に畳まない)

        Scene2D Encode(float t)
        {
            var s = new Scene2D();
            _draw?.Invoke(s);
            _animate?.Invoke(s, t);
            return s;
        }

        node.Content = Encode(_t);
        if (_animate is not null)
            ctx.AddAnimation(dt =>
            {
                _t += dt;
                node.Content = Encode(_t);
                return false;
            });
    }
}
