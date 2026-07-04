using Luxel.TwoD;
using Luxel.UI;

namespace Luxel.Controls;

/// <summary>
/// 子を左→右に並べ、幅を超えたら次の行へ折り返すコンテナ (CSS の flex-wrap / WPF WrapPanel 相当)。
/// 行の高さはその行で最も高い子。利用可能幅は制約から取る (無限幅なら <see cref="Widget.Width"/> 指定が必要)。
/// カードダッシュボード等の可変個パネルの整列向け。
/// </summary>
[UiComponent(Name = "Wrap")]
public sealed partial class WrapPanel : Widget
{
    internal readonly List<Widget> Children = new();
    private readonly float _hgap, _vgap;

    [UiCtor]
    internal WrapPanel(float hgap = 8f, float vgap = 8f)
    {
        _hgap = hgap;
        _vgap = vgap;
    }

    private void AddChild(Widget child) => Children.Add(child);

    /// <summary>子要素の宣言: <c>Wrap(8, 8)[ a, b, c ]</c>。</summary>
    public WrapPanel this[params Widget[] children]
    {
        get { foreach (Widget c in children) AddChild(c); return this; }
    }

    public override IEnumerable<Widget> DebugChildren() => Children;
    public override string? DebugDetail => $"{Children.Count} 子";

    protected override void PerformLayout(Constraints c, LayoutContext ctx)
    {
        float avail = ResolveW(c, ctx, float.IsInfinity(c.MaxW) ? 0 : c.MaxW);
        if (avail <= 0) avail = float.PositiveInfinity;   // 幅不明 → 折り返しなしの 1 行

        float x = 0, y = 0, lineH = 0, maxX = 0;
        foreach (Widget ch in Children)
        {
            Thickness m = ch.Margin.Get();
            Size cs = ch.Layout(new Constraints(0, float.IsInfinity(avail) ? avail : MathF.Max(0, avail - m.Horizontal),
                                                0, float.PositiveInfinity), ctx, parentUsesSize: true);
            float w = cs.Width + m.Horizontal, h = cs.Height + m.Vertical;
            if (x > 0 && x + w > avail)   // 折り返し
            {
                x = 0;
                y += lineH + _vgap;
                lineH = 0;
            }
            ch.Offset = new Point(x + m.Left, y + m.Top);
            x += w + _hgap;
            lineH = MathF.Max(lineH, h);
            maxX = MathF.Max(maxX, x - _hgap);
        }
        float totalH = Children.Count > 0 ? y + lineH : 0;
        float totalW = float.IsInfinity(avail) ? maxX : avail;
        Size = c.Constrain(new Size(totalW, totalH));
    }

    public override float MaxIntrinsicWidth(float height, LayoutContext ctx)
    {
        float sum = 0;
        foreach (Widget ch in Children) sum += ch.MaxIntrinsicWidth(height, ctx) + _hgap;
        return MathF.Max(0, sum - _hgap);
    }

    protected override void RealizeCore(UiBuildContext ctx, UiNode parent, Point worldOrigin)
    {
        UiNode node = CreateRoot(ctx, parent, worldOrigin);   // 透明コンテナ (変換のみ)
        Point world = WorldPos;
        foreach (Widget ch in Children) ch.Realize(ctx, node, world);
    }
}
