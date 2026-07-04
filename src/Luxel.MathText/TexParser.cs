namespace Luxel.MathText;

/// <summary>数式ノード (TeX サブセットのパース結果)。</summary>
public abstract record MathNode;

/// <summary>水平の並び。</summary>
public sealed record MathRow(IReadOnlyList<MathNode> Items) : MathNode;

/// <summary>文字列アトム (識別子/数字/演算子)。<see cref="Italic"/> = 変数風の字体ヒント。</summary>
public sealed record MathSymbol(string Text, bool Italic) : MathNode;

/// <summary>上付き/下付き (<c>x^2</c> / <c>a_i</c> / 両方)。</summary>
public sealed record MathScript(MathNode Base, MathNode? Sup, MathNode? Sub) : MathNode;

/// <summary>分数 <c>\frac{分子}{分母}</c>。</summary>
public sealed record MathFrac(MathNode Num, MathNode Den) : MathNode;

/// <summary>根号 <c>\sqrt{...}</c>。</summary>
public sealed record MathSqrt(MathNode Body) : MathNode;

/// <summary>ベクトル矢印 <c>\vec{x}</c>。</summary>
public sealed record MathVec(MathNode Body) : MathNode;

/// <summary>行列 <c>\begin{matrix|pmatrix|bmatrix} a &amp; b \\ c &amp; d \end{...}</c>。
/// <see cref="Bracket"/>: null = なし / '(' / '['。</summary>
public sealed record MathMatrix(IReadOnlyList<IReadOnlyList<MathNode>> Rows, char? Bracket) : MathNode;

/// <summary>
/// TeX **サブセット**パーサ。対応: <c>^ _ {}</c>、<c>\frac \sqrt \vec</c>、
/// <c>\begin{matrix/pmatrix/bmatrix}</c> (<c>&amp;</c> / <c>\\</c>)、ギリシャ文字・演算子コマンド
/// (置換表は <see cref="Luxel.Document.TexText"/> と同じ)、関数名 (\sin 等 = 立体)。
/// 未知のコマンドは <c>\cmd</c> のまま立体で出す (豆腐にしない)。
/// </summary>
public static class TexParser
{
    /// <summary>TeX 文字列をパースして数式ノードを返す (要素 1 つなら <see cref="MathRow"/> に包まない)。</summary>
    public static MathNode Parse(string tex)
    {
        int pos = 0;
        return ParseRow(tex ?? "", ref pos, stopAt: null);
    }

    private static MathNode ParseRow(string s, ref int pos, string? stopAt)
    {
        var items = new List<MathNode>();
        while (pos < s.Length)
        {
            char c = s[pos];
            if (c is '}' or '&') break;
            if (stopAt is not null && s.AsSpan(pos).StartsWith(stopAt)) break;
            if (c == '\\' && pos + 1 < s.Length && s[pos + 1] == '\\') break;   // 行区切り (行列)

            MathNode? atom = ParseAtom(s, ref pos, stopAt);
            if (atom is null) continue;

            // 直後の ^/_ (両方も可 — x_i^2)
            MathNode? sup = null, sub = null;
            while (pos < s.Length && s[pos] is '^' or '_')
            {
                char kind = s[pos++];
                MathNode arg = ParseAtom(s, ref pos, stopAt) ?? new MathSymbol("", false);
                if (kind == '^') sup = arg; else sub = arg;
            }
            items.Add(sup is null && sub is null ? atom : new MathScript(atom, sup, sub));
        }
        return items.Count == 1 ? items[0] : new MathRow(items);
    }

    private static MathNode? ParseAtom(string s, ref int pos, string? stopAt)
    {
        char c = s[pos];
        if (char.IsWhiteSpace(c)) { pos++; return null; }
        if (c == '{')
        {
            pos++;
            MathNode inner = ParseRow(s, ref pos, stopAt);
            if (pos < s.Length && s[pos] == '}') pos++;
            return inner;
        }
        if (c == '\\')
        {
            int j = pos + 1;
            while (j < s.Length && char.IsAsciiLetter(s[j])) j++;
            string cmd = s[(pos + 1)..j];
            pos = j;
            switch (cmd)
            {
                case "frac": return new MathFrac(ParseGroupOrAtom(s, ref pos), ParseGroupOrAtom(s, ref pos));
                case "sqrt": return new MathSqrt(ParseGroupOrAtom(s, ref pos));
                case "vec": return new MathVec(ParseGroupOrAtom(s, ref pos));
                case "begin": return ParseMatrix(s, ref pos);
                case "": pos++; return null;   // 単独の \ (未知) は読み飛ばす
                default:
                    // 置換表 (ギリシャ/演算子/関数名) — TexText と共有。関数名は立体
                    string uni = Luxel.Document.TexText.ToUnicode("\\" + cmd);
                    return uni != "\\" + cmd
                        ? new MathSymbol(uni, Italic: false)
                        : new MathSymbol("\\" + cmd, Italic: false);   // 未知はそのまま
            }
        }
        // 連続する同種文字を 1 アトムに (数字列、識別子 1 文字ずつはイタリック)
        pos++;
        if (char.IsAsciiDigit(c))
        {
            int start = pos - 1;
            while (pos < s.Length && (char.IsAsciiDigit(s[pos]) || s[pos] == '.')) pos++;
            return new MathSymbol(s[start..pos], Italic: false);
        }
        bool italic = char.IsLetter(c);
        return new MathSymbol(c.ToString(), italic);
    }

    private static MathNode ParseGroupOrAtom(string s, ref int pos)
    {
        while (pos < s.Length && char.IsWhiteSpace(s[pos])) pos++;
        if (pos >= s.Length) return new MathSymbol("", false);
        MathNode? a = ParseAtom(s, ref pos, null);
        return a ?? new MathSymbol("", false);
    }

    private static MathNode ParseMatrix(string s, ref int pos)
    {
        // \begin{matrix} ... \end{matrix} — env 名から括弧を決める
        string env = ReadBrace(s, ref pos);
        char? bracket = env switch { "pmatrix" => '(', "bmatrix" => '[', _ => null };
        string end = "\\end";
        var rows = new List<IReadOnlyList<MathNode>>();
        var row = new List<MathNode>();
        while (pos < s.Length && !s.AsSpan(pos).StartsWith(end))
        {
            row.Add(ParseRow(s, ref pos, end));
            if (pos < s.Length && s[pos] == '&') { pos++; continue; }
            if (pos + 1 < s.Length && s[pos] == '\\' && s[pos + 1] == '\\')
            {
                pos += 2;
                rows.Add(row);
                row = new List<MathNode>();
            }
        }
        if (row.Count > 0) rows.Add(row);
        if (s.AsSpan(pos).StartsWith(end)) { pos += end.Length; ReadBrace(s, ref pos); }
        return new MathMatrix(rows, bracket);
    }

    private static string ReadBrace(string s, ref int pos)
    {
        while (pos < s.Length && char.IsWhiteSpace(s[pos])) pos++;
        if (pos >= s.Length || s[pos] != '{') return "";
        int close = s.IndexOf('}', pos);
        if (close < 0) { pos = s.Length; return ""; }
        string body = s[(pos + 1)..close];
        pos = close + 1;
        return body;
    }
}
