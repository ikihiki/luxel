using Luxel.UI;

namespace Luxel.Controls;

/// <summary><c>U.Grid.*</c> utility の名前空間スコープ。</summary>
public readonly struct GridUtilityScope;

public static class GridUtilityExtensions
{
    extension(U)
    {
        public static GridUtilityScope Grid => default;
    }

    extension(GridUtilityScope scope)
    {
        public U Column(int value) => U.Attached(GridProperties.Column, value);
        public U Row(int value) => U.Attached(GridProperties.Row, value);
        public U ColumnSpan(int value) => U.Attached(GridProperties.ColumnSpan, value);
        public U RowSpan(int value) => U.Attached(GridProperties.RowSpan, value);

        public U Cell(int column, int row, int columnSpan = 1, int rowSpan = 1)
            => U.Custom<Widget>("Grid.Cell", UtilityKind.Attached, (target, _) =>
            {
                target.SetAttached(GridProperties.Column, column);
                target.SetAttached(GridProperties.Row, row);
                target.SetAttached(GridProperties.ColumnSpan, columnSpan);
                target.SetAttached(GridProperties.RowSpan, rowSpan);
            });
    }
}
