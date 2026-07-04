using Icu;

namespace Luxel.Typography;

/// <summary>
/// icu-dotnet (icu.net) の BreakIterator による <see cref="ITextSegmenter"/> 実装 —
/// **完全な UAX#14 (LINE) / UAX#29 (CHARACTER/WORD)**。NBSP・数値と単位・絵文字 ZWJ 列などの
/// 規則が SimpleSegmenter の部分集合より正確になる。
/// 差し込み方: app 起動時 (UI スレッド起動前) に
/// <c>if (IcuSegmenter.IsAvailable) TextSegmenter.Default = new IcuSegmenter();</c>
/// ネイティブ ICU (icuuc/icuin) が必要 — このアダプタプロジェクトだけが依存し、コアは依存ゼロのまま。
/// snap/golden は SimpleSegmenter で固定する (ICU バージョンで折返し位置が変わり環境依存になるため)。
/// </summary>
public sealed class IcuSegmenter : ITextSegmenter
{
    /// <summary>ネイティブ ICU が解決できるか (失敗時は SimpleSegmenter のまま運用する)。</summary>
    public static bool IsAvailable { get; } = Probe();

    /// <summary>解決失敗時の理由 (診断用)。成功時は null。</summary>
    public static string? UnavailableReason { get; private set; }

    private static bool Probe()
    {
        try
        {
            Wrapper.Init();
            bool ok = BreakIterator.GetBoundaries(BreakIterator.UBreakIteratorType.CHARACTER, new Locale("en"), "a").Any();
            if (!ok) UnavailableReason = "BreakIterator が境界を返しませんでした";
            return ok;
        }
        catch (Exception e)
        {
            UnavailableReason = e.GetBaseException().Message;
            return false;
        }
    }

    private readonly Locale _locale;

    public IcuSegmenter(string locale = "ja")
    {
        _locale = new Locale(locale);
        Wrapper.Init();
    }

    public void GetLineBreaks(ReadOnlySpan<char> text, Span<LineBreakKind> breaks)
    {
        breaks.Clear();   // 既定 Prohibited — ICU が機会と言った位置だけ許可する
        if (text.IsEmpty) return;
        string s = text.ToString();

        // CHARACTER (UAX#29) 境界 = 緊急/Char モードで折ってよい位置 (クラスタ安全)
        foreach (Boundary b in BreakIterator.GetBoundaries(BreakIterator.UBreakIteratorType.CHARACTER, _locale, s))
            if (b.End > 0 && b.End < s.Length)
                breaks[b.End - 1] = LineBreakKind.CharAllowed;

        // LINE (UAX#14) 境界 = 語境界 (禁則・NBSP・数値+単位・ZWJ 等を含む完全規則)
        foreach (Boundary b in BreakIterator.GetBoundaries(BreakIterator.UBreakIteratorType.LINE, _locale, s))
            if (b.End > 0 && b.End < s.Length)
                breaks[b.End - 1] = LineBreakKind.Allowed;

        for (int i = 0; i < s.Length; i++)
            if (s[i] == '\n') breaks[i] = LineBreakKind.Mandatory;
    }

    public int[] GetGraphemeBoundaries(string text)
    {
        var list = new List<int> { 0 };
        foreach (Boundary b in BreakIterator.GetBoundaries(BreakIterator.UBreakIteratorType.CHARACTER, _locale, text))
            list.Add(b.End);
        if (list[^1] != text.Length) list.Add(text.Length);
        return list.ToArray();
    }

    public (int start, int end) GetWordAt(string text, int index)
    {
        if (text.Length == 0) return (0, 0);
        index = Math.Clamp(index, 0, text.Length - 1);
        foreach (Boundary b in BreakIterator.GetBoundaries(BreakIterator.UBreakIteratorType.WORD, _locale, text))
            if (index >= b.Start && index < b.End)
                return (b.Start, b.End);
        return (index, index + 1);
    }
}
