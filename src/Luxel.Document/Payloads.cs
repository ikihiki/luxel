namespace Luxel.Document;

/// <summary>
/// 埋め込みブロック (BlockKind.Embed) のデータ。Document 層はデータのみ (widget を持たない) —
/// 表示は Controls 層の BlockWidgetRegistry が TypeId から widget を生成する。
/// </summary>
public interface IBlockPayload
{
    /// <summary>種別 ("image" / "table" / アプリ定義 "strudel" 等)。widget ファクトリの解決キー。</summary>
    string TypeId { get; }

    /// <summary>フェンス表現 (info, body)。フェンスを持つフォーマット (markdown 等) が
    /// 専用記法のない payload の書き出しに使う — 他ツールではコードブロックに退化する (安全)。</summary>
    (string Info, string Body) ToFence();

    /// <summary>undo スナップショット用 (payload は immutable 運用が原則)。</summary>
    IBlockPayload Clone();
}

/// <summary>フェンス → embed の判定 (markdown 等フェンスを持つフォーマットのパース時に呼ばれる)。
/// アプリが `MarkdownFormat.FenceResolvers` へ登録する — 記法はエディタが固定しない。</summary>
public interface IFenceResolver
{
    /// <summary>info 文字列 (```` ```strudel ```` の "strudel") と本文から payload を作る。null = 昇格しない。</summary>
    IBlockPayload? Resolve(string info, string body);
}

/// <summary>汎用 payload の canonical 形 — フェンスの info とソースコード本文をそのまま保持する。
/// strudel/MDX 型ではソースコードそのものが第一級のデータで、解釈/実行は widget 側の責務。</summary>
public sealed class FencePayload(string info, string body) : IBlockPayload
{
    public string Info { get; } = info ?? "";
    public string Body { get; } = body ?? "";

    /// <summary>TypeId は info の先頭語 ("csharp live" → "csharp")。</summary>
    public string TypeId => Info.Split(' ', 2)[0];

    public (string Info, string Body) ToFence() => (Info, Body);
    public IBlockPayload Clone() => new FencePayload(Info, Body);
}

/// <summary>画像ブロック。markdown では `![alt](src)`。デコード/表示は IImageSource (Controls 層) の責務。</summary>
public sealed class ImagePayload(string src, string alt = "") : IBlockPayload
{
    public string Src { get; } = src ?? "";
    public string Alt { get; } = alt ?? "";

    public string TypeId => "image";
    public (string Info, string Body) ToFence() => ("image", Src);
    public IBlockPayload Clone() => new ImagePayload(Src, Alt);
}

public enum TableAlign : byte { None, Left, Center, Right }

/// <summary>テーブルブロック (GFM pipe table)。Rows[0] = ヘッダ行。セルは v1 プレーン文字列
/// (セル内インライン記法はリテラル保持 — 書き出しで不変)。</summary>
public sealed class TablePayload : IBlockPayload
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

    public string TypeId => "table";

    public (string Info, string Body) ToFence() => ("table", SerializePipe());
    public IBlockPayload Clone() => new TablePayload(Rows.Select(r => (string[])r.Clone()).ToList(), (TableAlign[])Aligns.Clone());

    /// <summary>正規形 pipe table (整列パディングなし)。セル内の | はエスケープ。</summary>
    public string SerializePipe()
    {
        var sb = new System.Text.StringBuilder();
        AppendRow(0);
        sb.Append('\n');
        for (int c = 0; c < Columns; c++)
            sb.Append("| ").Append(Aligns[c] switch
            {
                TableAlign.Left => ":---",
                TableAlign.Center => ":---:",
                TableAlign.Right => "---:",
                _ => "---",
            }).Append(' ');
        sb.Append('|');
        for (int r = 1; r < Rows.Count; r++) { sb.Append('\n'); AppendRow(r); }
        return sb.ToString();

        void AppendRow(int r)
        {
            for (int c = 0; c < Columns; c++)
                sb.Append("| ").Append(Cell(r, c).Replace("|", "\\|")).Append(' ');
            sb.Append('|');
        }
    }
}

/// <summary>ブロック数式 (<c>$$...$$</c>)。<see cref="Source"/> は TeX ソースそのまま —
/// 表示 (レイアウト/描画) は Luxel.MathText 層の責務。シリアライズは <c>$$</c> 記法へ戻る
/// (フェンス化は他ツール向けの退化表現)。</summary>
public sealed class MathPayload(string source) : IBlockPayload
{
    public string Source { get; } = source ?? "";
    public string TypeId => "math";
    public (string Info, string Body) ToFence() => ("math", Source);
    public IBlockPayload Clone() => new MathPayload(Source);
}
