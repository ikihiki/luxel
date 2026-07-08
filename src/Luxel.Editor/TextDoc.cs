namespace Luxel.Editor;

/// <summary>
/// エディタ新スタックのテキスト文書 — **不変スナップショット**。全文文字列 + 行開始オフセットの索引を持ち、
/// フラットオフセット ↔ (行, 桁) の相互変換と行アクセスを O(log 行数) で提供する。
/// 行境界は <c>\n</c> のみ (<c>\r</c> は行の内容の一部として扱う)。編集は <see cref="ApplyChange"/> が
/// 新しい <see cref="TextDoc"/> を返す (元は変えない)。v1 は素朴な string 実装 — 大規模ファイルの rope 化は将来。
/// </summary>
public sealed class TextDoc
{
    /// <summary>空文書 (1 行・長さ 0)。</summary>
    public static readonly TextDoc Empty = new("");

    private readonly int[] _lineStarts;   // 各行の開始オフセット (先頭は必ず 0)。長さ = 行数

    private TextDoc(string text)
    {
        Text = text;
        // 行開始索引: 0 と、各 '\n' の直後
        var starts = new List<int>(text.Length / 24 + 1) { 0 };
        for (int i = 0; i < text.Length; i++)
            if (text[i] == '\n') starts.Add(i + 1);
        _lineStarts = starts.ToArray();
    }

    /// <summary>文字列から文書を作る (null は空)。</summary>
    public static TextDoc Of(string? text) => string.IsNullOrEmpty(text) ? Empty : new TextDoc(text);

    /// <summary>全文。</summary>
    public string Text { get; }

    /// <summary>全文の文字数。</summary>
    public int Length => Text.Length;

    /// <summary>行数 (常に 1 以上)。</summary>
    public int LineCount => _lineStarts.Length;

    /// <summary>行 (0 始まり) の開始オフセット。</summary>
    public int LineStart(int line) => _lineStarts[Math.Clamp(line, 0, LineCount - 1)];

    /// <summary>行の終端オフセット (行末の <c>\n</c> は含まない。最終行は文書末)。</summary>
    public int LineEnd(int line)
    {
        line = Math.Clamp(line, 0, LineCount - 1);
        return line + 1 < LineCount ? _lineStarts[line + 1] - 1 : Length;
    }

    /// <summary>行の本文 (改行を含まない)。</summary>
    public string LineText(int line)
    {
        int s = LineStart(line);
        return Text.Substring(s, LineEnd(line) - s);
    }

    /// <summary>オフセットが属する行 (0 始まり)。</summary>
    public int LineOf(int offset)
    {
        offset = Math.Clamp(offset, 0, Length);
        // _lineStarts 内で offset 以下の最大の行 (二分探索)
        int lo = 0, hi = _lineStarts.Length - 1;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) >> 1;
            if (_lineStarts[mid] <= offset) lo = mid; else hi = mid - 1;
        }
        return lo;
    }

    /// <summary>フラットオフセット → (行, 桁)。桁は行頭からの文字数。</summary>
    public (int Line, int Col) CoordAt(int offset)
    {
        offset = Math.Clamp(offset, 0, Length);
        int line = LineOf(offset);
        return (line, offset - _lineStarts[line]);
    }

    /// <summary>(行, 桁) → フラットオフセット (行内・文書内にクランプ)。</summary>
    public int OffsetAt(int line, int col)
    {
        line = Math.Clamp(line, 0, LineCount - 1);
        int start = _lineStarts[line];
        int max = LineEnd(line);
        return Math.Clamp(start + Math.Max(0, col), start, max);
    }

    /// <summary>部分文字列 [from, to)。</summary>
    public string Slice(int from, int to)
    {
        from = Math.Clamp(from, 0, Length);
        to = Math.Clamp(to, from, Length);
        return Text.Substring(from, to - from);
    }

    /// <summary>変更セットを適用した新しい文書を返す (元は不変)。</summary>
    public TextDoc ApplyChange(ChangeSet change) => Of(change.Apply(Text));

    /// <summary>[from, to) を <paramref name="insert"/> で置換した新しい文書 (単発編集の便宜)。</summary>
    public TextDoc Replace(int from, int to, string insert)
        => ApplyChange(ChangeSet.Of(Length, [new ChangeSpec(from, to, insert)]));

    /// <inheritdoc/>
    public override string ToString() => Text;
}
