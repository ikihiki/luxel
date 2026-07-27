using Luxel.Graphics.TwoD;
using Luxel.Typography;
using Luxel.UI;
using TAlign = Luxel.Typography.TextAlign;

namespace Luxel.Controls;

/// <summary>
/// リッチテキスト表示 (フォント/サイズ/色が混在するスパン列)。レイアウトは <see cref="TextLayout"/> —
/// 折り返し/禁則/整列/Justify、行内ベースラインは最大 ascent、グリフ未収載は <see cref="Fonts"/> で
/// フォールバック。保持型の「1 ノード 1 色」制約は**色ごとにノードを分割**して満たす
/// (パスは白で描き、node.Color が実色を乗せる)。
/// </summary>
[UiComponent]
public sealed partial class RichTextView : Widget
{
    /// <summary>表示するスパン列 (フォント/サイズ/色の混在可)。</summary>
    [UiParam] private readonly Bindable<TextSpan[]> _spans = new([]);

    /// <summary>既定の文字サイズ (スパンの Size 未指定分)。</summary>
    [UiParam] private readonly Bindable<float> _fontSize = 16f;
    /// <summary>折り返し (既定 None)。</summary>
    [UiParam] private readonly Bindable<TextWrap> _wrap = new();
    /// <summary>行の水平整列 (既定 Left)。</summary>
    [UiParam] private readonly Bindable<TAlign> _textAlign = new();
    /// <summary>行送り倍率 (既定 1.2)。</summary>
    [UiParam] private readonly Bindable<float> _lineHeight = 1.2f;

    /// <summary>フォールバック用フォント列 (省略 = レイアウト時の ctx.Font のみ)。</summary>
    public FontCollection? Fonts { get; set; }

    private TextLayout? _layout;

    public override string? DebugDetail
    {
        get
        {
            string t = string.Concat(Spans.Get().Select(s => s.Text));
            return t.Length <= 24 ? t : t[..24] + "…";
        }
    }

    protected override void PerformLayout(Constraints c, LayoutContext ctx)
    {
        Length lw = Width.Get();
        float maxW = lw.IsSet ? lw.Resolve(c.MaxW, ctx, float.PositiveInfinity)
                   : float.IsInfinity(c.MaxW) ? float.PositiveInfinity : c.MaxW;
        FontCollection fonts = Fonts ?? new FontCollection(ctx.Font);
        _layout = new TextLayout(fonts, Spans.Get(), FontSize.Get(), new TextLayoutOptions
        {
            MaxWidth = maxW,
            Wrap = Wrap.Get(),
            Align = TextAlign.Get(),
            LineHeight = LineHeight.Get(),
        });
        Size = c.Constrain(new Size(ResolveW(c, ctx, _layout.Width), ResolveH(c, ctx, _layout.Height)));
    }

    protected override void RealizeCore(UiBuildContext ctx, UiNode parent, Point worldOrigin)
    {
        UiNode node = CreateRoot(ctx, parent, worldOrigin);
        if (_layout is null) return;

        // 色ごとに 1 ノード (保持型はノード色がシーン色を上書きするため)
        foreach (uint color in _layout.Colors)
        {
            UiNode cn = ctx.Canvas.AddChild(node);
            var s = new Scene2D();
            _layout.DrawColorRuns(s, 0, 0, color);
            cn.Content = s;
            cn.Color = color;
        }
    }
}
