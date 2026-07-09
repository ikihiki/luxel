using System.Text.RegularExpressions;
using Luxel.Editor;
using Luxel.UI;

namespace Luxel.Controls;

/// <summary>
/// Markdown ソース → 装飾 (WS-A / ADR-0012)。見出し (Bold + サイズ倍率)・太字・斜体・
/// インラインコード (Mono + 背景) を font-variant <see cref="MarkDecoration"/> で表す。
/// **表示は行に分解、編集単位はブロック**というテキスト新スタックの流儀で、read-only 文書レンダラ
/// および Markdown エディタの土台になる (`TextEditorView.Providers` に足すだけ)。
/// 記法マーカ (<c>#</c>/<c>**</c>/<c>`</c>) は淡色にして本文を引き立てる。
/// 表・図・数式など複数行ブロックは block widget (後段スライス) で足す。
/// </summary>
public static class MarkdownDecorations
{
    // 行内: コード → 太字 → 斜体 の順に処理し、consumed で重複 (太字の中の斜体等) を避ける。
    private static readonly Regex Code = new(@"`([^`\n]+)`", RegexOptions.Compiled);
    private static readonly Regex Bold = new(@"(\*\*|__)(?=\S)(.+?)(?<=\S)\1", RegexOptions.Compiled);
    private static readonly Regex Italic = new(@"(?<![\*_\w])([*_])(?=\S)([^\*_\n]+)(?<=\S)\1(?![\*_\w])", RegexOptions.Compiled);

    /// <summary>見出しレベル (1..6) → 基準サイズへの倍率。</summary>
    public static float HeadingScale(int level) => level switch
    {
        1 => 1.9f, 2 => 1.6f, 3 => 1.35f, 4 => 1.2f, 5 => 1.1f, _ => 1.05f,
    };

    /// <summary>Markdown 全文 → 装飾集合 (純関数、フォント非依存 = GPU 不要でテスト可)。</summary>
    public static DecorationSet Build(string text, Theme t)
    {
        var marks = new List<Decoration>();
        var consumed = new bool[text.Length];
        uint muted = t.TextMuted;
        uint codeBg = Styles.WithAlpha(t.Text, 22);

        // --- 行単位: ATX 見出し (# .. ######) ---
        int lineStart = 0;
        foreach (string line in text.Split('\n'))
        {
            int end = lineStart + line.Length;
            int h = 0;
            while (h < line.Length && line[h] == '#') h++;
            if (h is >= 1 and <= 6 && h < line.Length && line[h] == ' ')
            {
                int content = lineStart + h + 1;
                marks.Add(new MarkDecoration(lineStart, content, Foreground: muted));   // "# " マーカを淡色
                marks.Add(new MarkDecoration(content, end, Foreground: t.Text,
                    Variant: FontVariant.Bold, FontScale: HeadingScale(h)));
                for (int i = lineStart; i < end; i++) consumed[i] = true;               // 本文のインライン再走査は行わない
            }
            lineStart = end + 1;   // +1 = '\n'
        }

        // --- 行内: コード / 太字 / 斜体 ---
        Inline(Code, text, consumed, marks, muted, delim: 1,
            (from, to) => new MarkDecoration(from, to, Variant: FontVariant.Mono, Background: codeBg));
        Inline(Bold, text, consumed, marks, muted, delim: 2,
            (from, to) => new MarkDecoration(from, to, Variant: FontVariant.Bold));
        Inline(Italic, text, consumed, marks, muted, delim: 1,
            (from, to) => new MarkDecoration(from, to, Variant: FontVariant.Italic));

        return new DecorationSet(marks);
    }

    private static void Inline(Regex re, string text, bool[] consumed, List<Decoration> marks,
        uint muted, int delim, Func<int, int, MarkDecoration> inner)
    {
        foreach (Match m in re.Matches(text))
        {
            int s = m.Index, e = m.Index + m.Length;
            if (Overlaps(consumed, s, e)) continue;
            int a = s + delim, b = e - delim;
            if (b <= a) continue;
            marks.Add(inner(a, b));                                    // 本文にスタイル
            marks.Add(new MarkDecoration(s, a, Foreground: muted));    // 開始マーカを淡色
            marks.Add(new MarkDecoration(b, e, Foreground: muted));    // 終了マーカを淡色
            for (int i = s; i < e; i++) consumed[i] = true;
        }
    }

    private static bool Overlaps(bool[] consumed, int from, int to)
    {
        for (int i = from; i < to; i++) if (consumed[i]) return true;
        return false;
    }
}

/// <summary>Markdown ソースを装飾に変換する <see cref="IDecorationProvider"/> — <see cref="TextEditorView.Providers"/>
/// に足すと見出し/太字/斜体/コードが付く。テキストとテーマが変わらない限りキャッシュを返す。</summary>
public sealed class MarkdownProvider(Func<Theme> theme) : IDecorationProvider
{
    private string? _lastText;
    private uint _lastDisc;
    private DecorationSet _cache = DecorationSet.Empty;

    /// <inheritdoc/>
    public string Owner => "markdown";

    /// <inheritdoc/>
    public DecorationSet Provide(EditorState state)
    {
        Theme t = theme();
        uint disc = t.Text ^ (t.TextMuted << 1);   // テーマ変化の検出子
        string text = state.Doc.Text;
        if (text == _lastText && disc == _lastDisc) return _cache;
        _lastText = text;
        _lastDisc = disc;
        _cache = MarkdownDecorations.Build(text, t);
        return _cache;
    }
}
