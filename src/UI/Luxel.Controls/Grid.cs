using Luxel.Graphics.TwoD;

using Luxel.UI;

namespace Luxel.Controls;

/// <summary>Grid が子 Widget から読む型付き添付プロパティ。</summary>
public static class GridProperties
{
    public static readonly AttachedProperty<int> Column = AttachedProperty<int>.Create<Grid>("Luxel.Controls.Grid.Column", 0, static value => value >= 0);
    public static readonly AttachedProperty<int> Row = AttachedProperty<int>.Create<Grid>("Luxel.Controls.Grid.Row", 0, static value => value >= 0);
    public static readonly AttachedProperty<int> ColumnSpan = AttachedProperty<int>.Create<Grid>("Luxel.Controls.Grid.ColumnSpan", 1, static value => value >= 1);
    public static readonly AttachedProperty<int> RowSpan = AttachedProperty<int>.Create<Grid>("Luxel.Controls.Grid.RowSpan", 1, static value => value >= 1);
}

/// <summary>
/// 行列グリッド。列/行は Fixed(px)/Auto(内容)/Star(比率)。子は <c>.GridColumn(n)</c> 等の
/// fluent 添付プロパティでセルを指定。Flutter 風単一パス (auto 列のみ局所的に intrinsic を測る)。
/// </summary>
[UiComponent]
public sealed partial class Grid : Widget
{
    internal readonly List<Widget> _children = new();

    /// <summary>列定義 (Fixed/Auto/Star)。未設定・空 → Star(1) の 1 列。</summary>
    [UiParam] private readonly Bindable<GridLength[]> _columns = new([]);
    /// <summary>行定義 (Fixed/Auto/Star)。未設定・空 → Star(1) の 1 行。</summary>
    [UiParam] private readonly Bindable<GridLength[]> _rows = new([]);

    /// <summary>実効トラック: 未設定/空なら Star(1) 1 本 (旧 ctor の既定と同じ)。</summary>
    private GridLength[] EffectiveColumns() { GridLength[] t = Columns.Get(); return t.Length > 0 ? t : [GridLength.Star(1)]; }
    private GridLength[] EffectiveRows() { GridLength[] t = Rows.Get(); return t.Length > 0 ? t : [GridLength.Star(1)]; }

    private void AddChild(Widget child) => _children.Add(child);

    public override IEnumerable<Widget> DebugChildren() => _children;
    public override string? DebugDetail => $"{EffectiveColumns().Length}x{EffectiveRows().Length}";

    /// <summary>子要素の宣言: <c>Grid(columns:[1,2])[ a, b ]</c>。子 Widget のみ受け取る。
    /// セル指定は子 Widget 自身の fluent 添付 (<c>.GridColumn(0)</c> 等) で行う。</summary>
    public Grid this[params Widget[] children]
    {
        get { foreach (Widget c in children) AddChild(c); return this; }
    }

    protected override void PerformLayout(Constraints c, LayoutContext ctx)
    {
        GridLength[] columns = EffectiveColumns();
        GridLength[] rows = EffectiveRows();
        float totalW = float.IsInfinity(c.MaxW) ? 0 : c.MaxW;
        float totalH = float.IsInfinity(c.MaxH) ? 0 : c.MaxH;

        float[] colW = ResolveTracks(columns, totalW, ctx, horizontal: true);
        float[] rowH = ResolveTracks(rows, totalH, ctx, horizontal: false);
        float[] colX = Cumulative(colW);
        float[] rowY = Cumulative(rowH);

        foreach (Widget ch in _children)
        {
            int col = Math.Clamp(ch.GetAttached(GridProperties.Column), 0, columns.Length - 1);
            int row = Math.Clamp(ch.GetAttached(GridProperties.Row), 0, rows.Length - 1);
            int cspan = ch.GetAttached(GridProperties.ColumnSpan);
            int rspan = ch.GetAttached(GridProperties.RowSpan);
            float cw = SumSpan(colW, col, cspan);
            float chh = SumSpan(rowH, row, rspan);
            LayoutHelper.PlaceInBox(ch, ctx, colX[col], rowY[row], cw, chh);   // margin + HAlign/VAlign
        }

        Size = c.Constrain(new Size(Sum(colW), Sum(rowH)));
    }

    private float[] ResolveTracks(GridLength[] tracks, float total, LayoutContext ctx, bool horizontal)
    {
        var sizes = new float[tracks.Length];
        float used = 0, starSum = 0;
        for (int i = 0; i < tracks.Length; i++)
        {
            GridLength t = tracks[i];
            switch (t.Unit)
            {
                case GridUnit.Pixel: sizes[i] = t.Value; used += t.Value; break;
                case GridUnit.Auto:
                    sizes[i] = horizontal ? AutoColumnWidth(tracks, i, ctx) : 0;   // 行 Auto は v1 未対応 (0)
                    used += sizes[i]; break;
                case GridUnit.Star: starSum += t.Value; break;
            }
        }
        float remain = MathF.Max(0, total - used);
        if (starSum > 0)
            for (int i = 0; i < tracks.Length; i++)
                if (tracks[i].Unit == GridUnit.Star) sizes[i] = remain * tracks[i].Value / starSum;
        return sizes;
    }

    private float AutoColumnWidth(GridLength[] columns, int col, LayoutContext ctx)
    {
        float w = 0;
        foreach (Widget ch in _children)
            if (Math.Clamp(ch.GetAttached(GridProperties.Column), 0, columns.Length - 1) == col)
                w = MathF.Max(w, ch.MaxIntrinsicWidth(float.PositiveInfinity, ctx));
        return w;
    }

    private static float[] Cumulative(float[] sizes)
    {
        var x = new float[sizes.Length];
        float acc = 0;
        for (int i = 0; i < sizes.Length; i++) { x[i] = acc; acc += sizes[i]; }
        return x;
    }

    private static float SumSpan(float[] sizes, int start, int span)
    {
        float s = 0;
        for (int i = start; i < start + span && i < sizes.Length; i++) s += sizes[i];
        return s;
    }

    private static float Sum(float[] a) { float s = 0; foreach (float v in a) s += v; return s; }

    protected override void RealizeCore(UiBuildContext ctx, UiNode parent, Point worldOrigin)
    {
        UiNode node = CreateRoot(ctx, parent, worldOrigin);   // 透明コンテナ
        Point world = WorldPos;
        foreach (Widget ch in _children) ch.Realize(ctx, node, world);
    }
}
