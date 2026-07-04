using System.Globalization;

namespace Luxel.Typography;

/// <summary>char 境界の折り返し可否 (位置 i = 「text[i] の後で折れるか」)。</summary>
public enum LineBreakKind : byte
{
    /// <summary>禁止 (サロゲート内・禁則)。緊急折返しでも折らない。</summary>
    Prohibited = 0,
    /// <summary>Char モード / Word の緊急折返しでのみ折れる (ラテン語中など)。</summary>
    CharAllowed = 1,
    /// <summary>語境界 (空白後・CJK 境界・ハイフン後) — Word モードの優先点。</summary>
    Allowed = 2,
    /// <summary>強制改行 (\n)。</summary>
    Mandatory = 3,
}

/// <summary>
/// テキスト分割判定の差し込み点 (UAX#14/#29 相当)。標準は <see cref="SimpleSegmenter"/> (依存ゼロの実用部分集合)。
/// 完全版が必要なアプリは icu-dotnet アダプタ (Luxel.Typography.Icu, TX-M5) を
/// <see cref="TextSegmenter.Default"/> か <c>TextLayoutOptions.Segmenter</c> に差す。
/// </summary>
public interface ITextSegmenter
{
    /// <summary>段落内の各位置の折り返し可否を書き込む (breaks.Length == text.Length)。
    /// breaks[i] は「text[i] の直後で折れるか」。</summary>
    void GetLineBreaks(ReadOnlySpan<char> text, Span<LineBreakKind> breaks);

    /// <summary>グラフェムクラスタ境界 (UAX#29)。戻り値は昇順の開始 index 列 (先頭 0、末尾 text.Length を含む)。</summary>
    int[] GetGraphemeBoundaries(string text);

    /// <summary>index を含む単語の範囲 (ダブルクリック選択用)。</summary>
    (int start, int end) GetWordAt(string text, int index);
}

/// <summary>プロセス既定の分割器。app 起動時 (UI スレッド起動前) に 1 回だけ差し替えること。</summary>
public static class TextSegmenter
{
    public static ITextSegmenter Default { get; set; } = new SimpleSegmenter();
}

/// <summary>
/// 依存ゼロの標準分割器 (UAX#14 の実用部分集合):
/// 空白の後 = 語境界 / CJK 文字境界 = 語境界 / ハイフンの後 = 語境界 /
/// 行頭禁則 (。、」等の約物・小書き) と行末禁則 (「『等)・サロゲート内 = 禁止 / それ以外 = Char のみ。
/// グラフェムは .NET の <see cref="StringInfo"/> (ICU ベースで UAX#29 準拠)。
/// </summary>
public sealed class SimpleSegmenter : ITextSegmenter
{
    // 行頭に来てはいけない文字 (次の行の先頭にしない → その手前では折らない)
    private const string NoStart = "、。，．・：；？！゛゜´`¨＾～”）〕］｝〉》」』】ゝゞーぁぃぅぇぉっゃゅょゎァィゥェォッャュョヮヵヶ!),.:;?]}»›’”";
    // 行末に来てはいけない文字 (この文字の直後では折らない)
    private const string NoEnd = "‘“（〔［｛〈《「『【([{«‹";

    public void GetLineBreaks(ReadOnlySpan<char> text, Span<LineBreakKind> breaks)
    {
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            char next = i + 1 < text.Length ? text[i + 1] : '\0';

            if (c == '\n') { breaks[i] = LineBreakKind.Mandatory; continue; }
            if (i + 1 >= text.Length) { breaks[i] = LineBreakKind.Prohibited; continue; }   // 末尾では折らない

            // サロゲートペア/結合文字の内側は禁止 (クラスタ分割はレイアウト側でも防ぐが二重に守る)
            if (char.IsHighSurrogate(c)) { breaks[i] = LineBreakKind.Prohibited; continue; }

            // 禁則: 次が行頭禁則 / 自分が行末禁則 → 禁止
            if (NoStart.Contains(next) || NoEnd.Contains(c)) { breaks[i] = LineBreakKind.Prohibited; continue; }

            // 語境界: 空白の後 / ハイフンの後 / CJK が絡む境界
            if (c is ' ' or '\t' or '　') { breaks[i] = LineBreakKind.Allowed; continue; }
            if (c == '-' && char.IsLetterOrDigit(next)) { breaks[i] = LineBreakKind.Allowed; continue; }
            if (IsCjk(c) || IsCjk(next)) { breaks[i] = LineBreakKind.Allowed; continue; }

            breaks[i] = LineBreakKind.CharAllowed;
        }
    }

    public int[] GetGraphemeBoundaries(string text)
    {
        var list = new List<int> { 0 };
        TextElementEnumerator e = StringInfo.GetTextElementEnumerator(text);
        while (e.MoveNext()) list.Add(e.ElementIndex + ((string)e.Current).Length);
        if (list[^1] != text.Length) list.Add(text.Length);
        return list.ToArray();
    }

    public (int start, int end) GetWordAt(string text, int index)
    {
        if (text.Length == 0) return (0, 0);
        index = Math.Clamp(index, 0, text.Length - 1);
        bool Same(char a, char b) => Class(a) == Class(b);
        static int Class(char c) => char.IsLetterOrDigit(c) ? (IsCjk(c) ? 2 : 1) : char.IsWhiteSpace(c) ? 3 : 4;
        int s = index, e = index + 1;
        while (s > 0 && Same(text[s - 1], text[index])) s--;
        while (e < text.Length && Same(text[e], text[index])) e++;
        return (s, e);
    }

    /// <summary>CJK (かな/カナ/漢字/全角形/CJK 約物) か。</summary>
    internal static bool IsCjk(char c) => c switch
    {
        >= '　' and <= 'ヿ' => true,   // CJK 約物 + ひらがな + カタカナ
        >= '㐀' and <= '鿿' => true,   // CJK 統合漢字 (拡張 A 含む)
        >= '豈' and <= '﫿' => true,   // 互換漢字
        >= '＀' and <= '￯' => true,   // 全角形/半角カナ
        _ => false,
    };
}
