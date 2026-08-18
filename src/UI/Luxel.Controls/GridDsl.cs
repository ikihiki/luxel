using Luxel.UI;

namespace Luxel.Controls;

/// <summary>
/// グリッド添付プロパティの fluent 宣言 (子 Widget に付ける)。
/// <code>
/// Grid(columns: [1, 2])[
///     Text("A").GridColumn(0),
///     Button(_ => { }, "B").GridColumn(1).GridRow(0)]
/// </code>
/// ジェネリック this で具象型を返すため、When/Transition 等と自由にチェーンできる。
/// </summary>
public static class GridAttachedExtensions
{
    public static T GridColumn<T>(this T w, int column) where T : Widget
    {
        U.Grid.Column(column).ApplyTo(w);
        return w;
    }

    public static T GridRow<T>(this T w, int row) where T : Widget
    {
        U.Grid.Row(row).ApplyTo(w);
        return w;
    }

    public static T GridColumnSpan<T>(this T w, int span) where T : Widget
    {
        U.Grid.ColumnSpan(span).ApplyTo(w);
        return w;
    }

    public static T GridRowSpan<T>(this T w, int span) where T : Widget
    {
        U.Grid.RowSpan(span).ApplyTo(w);
        return w;
    }

    /// <summary>セル指定のまとめ書き (列, 行, スパン)。</summary>
    public static T GridCell<T>(this T w, int column, int row, int columnSpan = 1, int rowSpan = 1) where T : Widget
    {
        U.Grid.Cell(column, row, columnSpan, rowSpan).ApplyTo(w);
        return w;
    }
}
