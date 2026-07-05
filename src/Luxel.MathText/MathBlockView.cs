using Luxel.TwoD;
using Luxel.UI;

namespace Luxel.MathText;

/// <summary>
/// ブロック数式 (<c>$$...$$</c>) の描画 widget。<see cref="TexParser"/> → <see cref="MathLayoutEngine"/>
/// で組版し、グリフ (VectorFont) + ストローク (分数線/根号/括弧) を 1 つの Scene2D に描く。
/// 色はテーマ Text の recolor のみ。使える幅に収まらないときは等比縮小。
/// </summary>
[UiComponent]
public sealed partial class MathBlockView : Widget
{
    /// <summary>TeX ソース ($$ の内側)。</summary>
    [UiParam] private readonly Bindable<string> _source = "";
    /// <summary>基準文字サイズ (px)。</summary>
    [UiParam] private readonly Bindable<float> _fontSize = 20f;
    /// <summary>使える最大幅 (0 = 制約幅)。超えると等比縮小。</summary>
    [UiParam] private readonly Bindable<float> _maxWidth = new();

    private MathNode? _node;
    private MathBox _box;
    private float _scale = 1f;

    private string Src => Source.Get();
    private float Px => FontSize.Get();

    /// <summary>デバッグ表示の補足 (ソース先頭 24 文字)。</summary>
    public override string? DebugDetail => Src.Length <= 24 ? Src : Src[..24] + "…";

    private MathLayoutEngine Engine(LayoutContext ctx) =>
        new((t, px) => ctx.Font.Measure(t, px), px => ctx.Font.Ascent(px));

    protected override void PerformLayout(Constraints c, LayoutContext ctx)
    {
        _node ??= TexParser.Parse(Src.Replace("\n", " ").Trim());
        _box = Engine(ctx).Measure(_node, Px);
        float maxW = MaxWidth.Get();
        float availW = maxW > 0 ? maxW : c.MaxW;
        _scale = !float.IsInfinity(availW) && availW > 0 && _box.W > availW ? availW / _box.W : 1f;
        Size = c.Constrain(new Size(_box.W * _scale, _box.H * _scale));
    }

    /// <summary>縮小前の自然幅 (組版結果の幅)。</summary>
    public override float MaxIntrinsicWidth(float height, LayoutContext ctx) => _box.W;

    protected override void RealizeCore(UiBuildContext ctx, UiNode parent, Point worldOrigin)
    {
        UiNode root = CreateRoot(ctx, parent, worldOrigin);
        if (_node is null) return;
        float s = _scale;
        var scene = new Scene2D();
        var engine = new MathLayoutEngine(
            (t, px) => ctx.Font.Measure(t, px),
            px => ctx.Font.Ascent(px));
        engine.Draw(_node, 0, _box.Base * s, Px * s,
            text: (t, x, baseY, px) => ctx.Font.AppendText(scene, t, x, baseY, px, Color2D.White),
            line: (x1, y1, x2, y2, w) => scene.StrokeLine(Color2D.White, w * s, x1, y1, x2, y2));
        root.Content = scene;
        ctx.Effect(() => root.Color = ctx.Theme.Value.Text);
    }
}
