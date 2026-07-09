using System.Text.RegularExpressions;
using Luxel.Editor;
using Luxel.Typography;
using Luxel.UI;

namespace Luxel.Controls;

/// <summary>見出し 1 件 (レベル・テキスト・ソースオフセット)。TOC / <c>story:</c> ナビの素。</summary>
public readonly record struct MarkdownHeading(int Level, string Text, int Offset);

/// <summary>Markdown を「文書として描く」ワンショット (WS-A / ADR-0012) — <see cref="TextEditorView"/> に
/// <see cref="MarkdownProvider"/> を付け、read-only + 折返しで束ねる。表示は行、装飾は provider。
/// 将来 <see cref="Kit.Docs(DocString, bool, System.Collections.Generic.IReadOnlyList{IFenceResolver})"/> の
/// RichTextEditor をこれに差し替える。</summary>
public static class MarkdownDoc
{
    /// <summary>設定済みの文書レンダラを作る。<paramref name="body"/> が null なら既定 (テーマ) フォント。</summary>
    public static TextEditorView Create(Signal<string> markdown, Func<Theme> theme, float width, float height,
        VectorFont? body = null, VectorFont? bold = null, VectorFont? italic = null,
        VectorFont? boldItalic = null, VectorFont? mono = null, bool wrap = true)
    {
        TextEditorView ed = Kit.TextEditorView(markdown, editorHeight: height, editorWidth: width);
        if (body is not null) ed.EditorFont = body;
        ed.BoldFont = bold;
        ed.ItalicFont = italic;
        ed.BoldItalicFont = boldItalic;
        ed.MonoFont = mono;
        ed.WrapText = wrap;
        ed.ReadOnly = true;
        ed.Providers.Add(new MarkdownProvider(theme));
        return ed;
    }
}

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
    private static readonly Regex Link = new(@"\[([^\]\n]+)\]\(([^)\n]+)\)", RegexOptions.Compiled);
    private static readonly Regex Code = new(@"`([^`\n]+)`", RegexOptions.Compiled);
    private static readonly Regex Bold = new(@"(\*\*|__)(?=\S)(.+?)(?<=\S)\1", RegexOptions.Compiled);
    private static readonly Regex Italic = new(@"(?<![\*_\w])([*_])(?=\S)([^\*_\n]+)(?<=\S)\1(?![\*_\w])", RegexOptions.Compiled);

    /// <summary>文書中の ATX 見出しを順に抽出する (TOC / <c>story:</c> ナビの素。フェンス内は除外)。純関数。</summary>
    public static IReadOnlyList<MarkdownHeading> Headings(string text)
    {
        var list = new List<MarkdownHeading>();
        int lineStart = 0;
        bool inFence = false;
        foreach (string line in text.Split('\n'))
        {
            string trimmed = line.TrimStart();
            if (trimmed.StartsWith("```")) inFence = !inFence;
            else if (!inFence)
            {
                int h = 0;
                while (h < line.Length && line[h] == '#') h++;
                if (h is >= 1 and <= 6 && h < line.Length && line[h] == ' ')
                    list.Add(new MarkdownHeading(h, line[(h + 1)..].Trim(), lineStart));
            }
            lineStart += line.Length + 1;
        }
        return list;
    }

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

        // --- 行単位: 見出し / コードフェンス / 引用 / 箇条書き ---
        int lineStart = 0;
        bool inFence = false;
        foreach (string line in text.Split('\n'))
        {
            int end = lineStart + line.Length;
            string trimmed = line.TrimStart();
            int indent = line.Length - trimmed.Length;

            // ``` フェンス開閉 (行自体は淡色、間の行はコードブロック)
            if (trimmed.StartsWith("```"))
            {
                marks.Add(new MarkDecoration(lineStart, end, Foreground: muted));
                Consume(consumed, lineStart, end);
                inFence = !inFence;
                lineStart = end + 1;
                continue;
            }
            if (inFence)
            {
                marks.Add(new LineDecoration(lineStart, codeBg));                        // 行背景
                if (end > lineStart) marks.Add(new MarkDecoration(lineStart, end, Variant: FontVariant.Mono));
                Consume(consumed, lineStart, end);                                       // インライン無効
                lineStart = end + 1;
                continue;
            }

            // ATX 見出し (# .. ######、行頭)
            int h = 0;
            while (h < line.Length && line[h] == '#') h++;
            if (h is >= 1 and <= 6 && h < line.Length && line[h] == ' ')
            {
                int content = lineStart + h + 1;
                marks.Add(new MarkDecoration(lineStart, content, Foreground: muted));    // "# " マーカを淡色
                marks.Add(new MarkDecoration(content, end, Foreground: t.Text,
                    Variant: FontVariant.Bold, FontScale: HeadingScale(h)));
                Consume(consumed, lineStart, end);                                       // 本文のインライン再走査は行わない
                lineStart = end + 1;
                continue;
            }

            // 引用 (> ...): 左縦バー + インデント、マーカは淡色。本文のインラインは効かせる
            if (trimmed.StartsWith("> ") || trimmed == ">")
            {
                if (end > lineStart) marks.Add(new BlockDecoration(lineStart, end, BarColor: muted, Indent: 12f));
                int gt = lineStart + indent;
                int after = Math.Min(gt + (trimmed.StartsWith("> ") ? 2 : 1), end);
                marks.Add(new MarkDecoration(gt, after, Foreground: muted));
                lineStart = end + 1;
                continue;
            }

            // 箇条書き / 番号付きリストのマーカを淡色 (本文はそのまま)
            if (trimmed.Length >= 2 && trimmed[1] == ' ' && trimmed[0] is '-' or '*' or '+')
                marks.Add(new MarkDecoration(lineStart + indent, lineStart + indent + 2, Foreground: muted));
            else
            {
                int d = indent;
                while (d < line.Length && char.IsAsciiDigit(line[d])) d++;
                if (d > indent && d + 1 < line.Length && line[d] == '.' && line[d + 1] == ' ')
                    marks.Add(new MarkDecoration(lineStart + indent, lineStart + d + 2, Foreground: muted));
            }
            lineStart = end + 1;   // +1 = '\n'
        }

        // --- 行内: リンク [text](url) → text をアクセント色+下線、括弧/URL は淡色 ---
        uint link = t.Primary;
        foreach (Match m in Link.Matches(text))
        {
            int s = m.Index, e = m.Index + m.Length;
            if (Overlaps(consumed, s, e)) continue;
            Group g = m.Groups[1];
            marks.Add(new MarkDecoration(g.Index, g.Index + g.Length, Foreground: link, Underline: new UnderlineStyle(link)));
            marks.Add(new MarkDecoration(s, g.Index, Foreground: muted));                    // "["
            marks.Add(new MarkDecoration(g.Index + g.Length, e, Foreground: muted));         // "](url)"
            Consume(consumed, s, e);
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

    private static void Consume(bool[] consumed, int from, int to)
    {
        for (int i = from; i < to && i < consumed.Length; i++) consumed[i] = true;
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
