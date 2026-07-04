namespace Luxel.UI;

/// <summary>子をボックス内に margin + 整列で配置する共通処理。</summary>
public static class LayoutHelper
{
    /// <summary>(boxX,boxY,boxW,boxH) の領域に子を配置 (margin を引き、子の HAlign/VAlign で整列)。</summary>
    public static void PlaceInBox(Widget child, LayoutContext ctx, float boxX, float boxY, float boxW, float boxH)
        => PlaceInBox(child, ctx, boxX, boxY, boxW, boxH, child.HAlign.Get(), child.VAlign.Get());

    /// <summary>整列を明示して配置する。</summary>
    public static void PlaceInBox(Widget child, LayoutContext ctx, float boxX, float boxY,
        float boxW, float boxH, Align hAlign, Align vAlign)
    {
        Thickness m = child.Margin.Get();
        float availW = MathF.Max(0, boxW - m.Horizontal);
        float availH = MathF.Max(0, boxH - m.Vertical);

        // Stretch は tight 制約で領域いっぱいに、それ以外は loose で自然サイズ。
        float minW = hAlign == Align.Stretch ? availW : 0;
        float minH = vAlign == Align.Stretch ? availH : 0;
        Size cs = child.Layout(new Constraints(minW, availW, minH, availH), ctx, parentUsesSize: true);

        float ax = AlignOffset(hAlign, availW, cs.Width);
        float ay = AlignOffset(vAlign, availH, cs.Height);
        child.Offset = new Point(boxX + m.Left + ax, boxY + m.Top + ay);
    }

    public static float AlignOffset(Align a, float avail, float size) => a switch
    {
        Align.Center => (avail - size) / 2f,
        Align.End => avail - size,
        _ => 0f,   // Start / Stretch
    };
}
