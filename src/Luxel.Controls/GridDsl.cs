using Luxel.UI;

namespace Luxel.Controls;

/// <summary>グリッド添付プロパティ (子に付ける)。<c>P.Grid.Column(1)</c> 等で <see cref="INodePart"/> を返す。</summary>
public readonly struct GridDecl
{
    public IConfigPart Column(int v) => new AttachedPart(new(GridKeys.Column, v));
    public IConfigPart Row(int v) => new AttachedPart(new(GridKeys.Row, v));
    public IConfigPart ColumnSpan(int v) => new AttachedPart(new(GridKeys.ColumnSpan, v));
    public IConfigPart RowSpan(int v) => new AttachedPart(new(GridKeys.RowSpan, v));
}

/// <summary><c>P</c> へ Grid 添付プロパティのファサードを生やす拡張プロパティ (C# 14 拡張メンバー)。
/// 使用側は <c>using static Luxel.UI.Decl;</c> + <c>using Luxel.Controls;</c>。</summary>
public static class PExtensions
{
    extension(PRoot p)
    {
        public GridDecl Grid => default;
    }
}
