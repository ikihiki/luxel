namespace Luxel.Controls;

/// <summary>画像ブロック (<see cref="ImageBlock"/>) のデータ。markdown では <c>![alt](src)</c>。
/// デコード/表示は Resource システム経由。</summary>
public sealed record ImagePayload(string Src, string Alt = "");

/// <summary>テーブルのセル整列。</summary>
public enum TableAlign : byte { None, Left, Center, Right }

/// <summary>テーブルブロック (<see cref="TableBlock"/>, GFM pipe table) のデータ。Rows[0] = ヘッダ行。
/// セルは v1 プレーン文字列。</summary>
public sealed class TablePayload
{
    public List<string[]> Rows { get; }
    public TableAlign[] Aligns { get; }

    public TablePayload(List<string[]> rows, TableAlign[]? aligns = null)
    {
        Rows = rows;
        int cols = rows.Count > 0 ? rows.Max(r => r.Length) : 0;
        Aligns = aligns is { } a && a.Length == cols ? a : new TableAlign[cols];
    }

    public int Columns => Aligns.Length;
    public string Cell(int row, int col)
        => row < Rows.Count && col < Rows[row].Length ? Rows[row][col] : "";

    /// <summary>undo スナップショット用 (行/整列を deep copy)。</summary>
    public TablePayload Clone() => new(Rows.Select(r => (string[])r.Clone()).ToList(), (TableAlign[])Aligns.Clone());
}
