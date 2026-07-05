using Luxel.TwoD;

using Luxel.UI;

namespace Luxel.Controls;

/// <summary>子を縦/横に順に並べる単純コンテナ。</summary>
[UiComponent(Name = "Stack")]
public sealed partial class StackPanel : Widget
{
    internal readonly List<Widget> Children = new();

    /// <summary>true=縦に積む (既定)。向きは <c>VStack</c>/<c>HStack</c> sugar もある。</summary>
    [UiParam] private readonly Bindable<bool> _vertical = true;
    /// <summary>子要素間の間隔 (px)。</summary>
    [UiParam] private readonly Bindable<float> _spacing = 0f;

    private void AddChild(Widget child) => Children.Add(child);

    /// <summary>子要素の宣言: <c>Stack(...)[ a, b, c ]</c> / <c>VStack()[ a, b ]</c>。</summary>
    public StackPanel this[params Widget[] children]
    {
        get { foreach (Widget c in children) AddChild(c); return this; }
    }

    /// <summary>子要素数 (テスト/検査用)。</summary>
    public int ChildCount => Children.Count;

    public override IEnumerable<Widget> DebugChildren() => Children;
    public override string? DebugDetail => $"{(Vertical.Get() ? "V" : "H")} spacing={Spacing.Get():0}";

    protected override void PerformLayout(Constraints c, LayoutContext ctx)
    {
        bool vertical = Vertical.Get();
        float spacing = Spacing.Get();
        float main = 0, cross = 0;
        float crossMax = vertical ? c.MaxW : c.MaxH;            // 交差軸の利用可能幅 (∞ もあり)
        bool crossBounded = !float.IsInfinity(crossMax);
        foreach (Widget ch in Children)
        {
            Thickness m = ch.Margin.Get();
            float crossAvail = crossBounded ? MathF.Max(0, crossMax - (vertical ? m.Horizontal : m.Vertical)) : float.PositiveInfinity;
            Align crossAlign = vertical ? ch.HAlign.Get() : ch.VAlign.Get();
            float crossMin = crossBounded && crossAlign == Align.Stretch ? crossAvail : 0;

            Constraints cc = vertical
                ? new Constraints(crossMin, crossAvail, 0, float.PositiveInfinity)
                : new Constraints(0, float.PositiveInfinity, crossMin, crossAvail);
            Size cs = ch.Layout(cc, ctx, parentUsesSize: true);

            float crossSize = vertical ? cs.Width : cs.Height;
            float ca = crossBounded ? LayoutHelper.AlignOffset(crossAlign, crossAvail, crossSize) : 0;
            if (vertical)
            {
                ch.Offset = new Point(m.Left + ca, main + m.Top);
                main += cs.Height + m.Vertical + spacing;
                cross = MathF.Max(cross, cs.Width + m.Horizontal);
            }
            else
            {
                ch.Offset = new Point(main + m.Left, m.Top + ca);
                main += cs.Width + m.Horizontal + spacing;
                cross = MathF.Max(cross, cs.Height + m.Vertical);
            }
        }
        if (Children.Count > 0) main -= spacing;
        Size = c.Constrain(vertical ? new Size(cross, main) : new Size(main, cross));
    }

    public override float MaxIntrinsicWidth(float height, LayoutContext ctx)
    {
        if (Vertical.Get())
        {
            float w = 0;
            foreach (Widget ch in Children) w = MathF.Max(w, ch.MaxIntrinsicWidth(height, ctx));
            return w;
        }
        float sum = 0;
        foreach (Widget ch in Children) sum += ch.MaxIntrinsicWidth(height, ctx);
        return sum + Spacing.Get() * Math.Max(0, Children.Count - 1);
    }

    protected override void RealizeCore(UiBuildContext ctx, UiNode parent, Point worldOrigin)
    {
        UiNode node = CreateRoot(ctx, parent, worldOrigin);   // 透明コンテナ (変換のみ)
        Point world = WorldPos;
        foreach (Widget ch in Children) ch.Realize(ctx, node, world);
    }
}
