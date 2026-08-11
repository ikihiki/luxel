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
        w.SetAttached(new Attached(GridKeys.Column, column));
        return w;
    }

    public static T GridRow<T>(this T w, int row) where T : Widget
    {
        w.SetAttached(new Attached(GridKeys.Row, row));
        return w;
    }

    public static T GridColumnSpan<T>(this T w, int span) where T : Widget
    {
        w.SetAttached(new Attached(GridKeys.ColumnSpan, span));
        return w;
    }

    public static T GridRowSpan<T>(this T w, int span) where T : Widget
    {
        w.SetAttached(new Attached(GridKeys.RowSpan, span));
        return w;
    }

    /// <summary>セル指定のまとめ書き (列, 行, スパン)。</summary>
    public static T GridCell<T>(this T w, int column, int row, int columnSpan = 1, int rowSpan = 1) where T : Widget
    {
        w.SetAttached(new Attached(GridKeys.Column, column));
        w.SetAttached(new Attached(GridKeys.Row, row));
        if (columnSpan != 1) w.SetAttached(new Attached(GridKeys.ColumnSpan, columnSpan));
        if (rowSpan != 1) w.SetAttached(new Attached(GridKeys.RowSpan, rowSpan));
        return w;
    }
}
