using System.Text.RegularExpressions;
using Luxel.Document;
using Luxel.Editor;
using Luxel.Typography;
using Luxel.UI;

namespace Luxel.Controls;

/// <summary>見出し 1 件 (レベル・テキスト・ソースオフセット)。TOC / <c>story:</c> ナビの素。</summary>
public readonly record struct MarkdownHeading(int Level, string Text, int Offset);

/// <summary>リンク 1 件 — リンクテキストのソース範囲 <c>[From, To)</c> と URL。クリック→ナビの素
/// (view の <see cref="TextEditorView.OnClickOffset"/> で当たり判定する)。</summary>
public readonly record struct MarkdownLink(int From, int To, string Text, string Url);

/// <summary>埋め込みフェンス <c>```embed &lt;key&gt; ... ```</c> の中身 — 種別キーとフェンス本文。
/// block widget の <see cref="BlockWidgetDecoration.Key"/> になり、view の <see cref="TextEditorView.WidgetResolver"/>
/// が種別で実 Widget を作る (例 mermaid/数式は Body を図/式ソースとして解決)。</summary>
public readonly record struct EmbedRef(string Key, string Body);

/// <summary>Markdown を「文書として描く」ワンショット (WS-A / ADR-0012) — <see cref="TextEditorView"/> に
/// <see cref="MarkdownProvider"/> を付け、read-only + 折返しで束ねる。表示は行、装飾は provider。
/// 将来 <see cref="Kit.Docs(DocString, bool, System.Collections.Generic.IReadOnlyList{IFenceResolver})"/> の
/// RichTextEditor をこれに差し替える。</summary>
public static class MarkdownDoc
{
    /// <summary>設定済みの文書レンダラを作る。<paramref name="body"/> が null なら既定 (テーマ) フォント。</summary>
    public static TextEditorView Create(Signal<string> markdown, Func<Theme> theme, float width, float height,
        VectorFont? body = null, VectorFont? bold = null, VectorFont? italic = null,
        VectorFont? boldItalic = null, VectorFont? mono = null, bool wrap = true, ISyntaxHighlighter? highlighter = null,
        IReadOnlyCollection<string>? embedKinds = null, FontCollection? fonts = null, bool fill = false)
    {
        TextEditorView ed = Kit.TextEditorView(markdown, editorHeight: height, editorWidth: width);
        if (body is not null) ed.EditorFont = body;
        if (fonts is not null) ed.Fonts = fonts;   // 日本語/絵文字フォールバック用フォント列
        ed.Fill = fill;   // true = 領域いっぱい (文書ページ)。width/height は初期見積り
        ed.BoldFont = bold;
        ed.ItalicFont = italic;
        ed.BoldItalicFont = boldItalic;
        ed.MonoFont = mono;
        ed.WrapText = wrap;
        ed.WrapLineHeight = 1.3f;   // 段落内はブロック間 (1.5) より詰める
        ed.ReadOnly = true;
        ed.DocSource = markdown.Peek();   // docs 索引用 (realize 不要で本文/見出し/リンクを取れる)
        ed.Providers.Add(new MarkdownProvider(theme, hideMarkers: true, highlighter, embedKinds));   // 文書レンダラ: マーカ非表示 + コード色分け + 埋め込み
        return ed;
    }

    /// <summary>既存の <see cref="DocString"/> (```luxel-ui の UI hole を含む markdown) を新スタックで描く橋。
    /// これで <c>Docs($"...{Widget}...")</c> 記法のページを 1 行変更で新スタックへ移行できる。
    /// block hole (luxel-ui) は <see cref="DocString.HoleWidgets"/> で解決。mermaid/数式など外部ドメインの
    /// フェンスは <paramref name="fences"/> (kind → 本文で widget を作る) で注入する (Controls は Diagram/MathText 非依存)。
    /// インライン hole (<c>[￼](luxel-ui:N)</c>) は後段。</summary>
    public static TextEditorView FromDoc(DocString content, Func<Theme> theme, float width, float height,
        VectorFont? body = null, VectorFont? bold = null, VectorFont? mono = null, ISyntaxHighlighter? highlighter = null,
        IReadOnlyDictionary<string, Func<string, Widget>>? fences = null, FontCollection? fonts = null, bool fill = false)
    {
        IReadOnlyList<Widget> holes = content.HoleWidgets;
        var kinds = new HashSet<string> { DocString.UiTypeId };
        if (fences is not null) foreach (string k in fences.Keys) kinds.Add(k);
        TextEditorView ed = Create(new Signal<string>(content.Md), theme, width, height,
            body: body, bold: bold, mono: mono, highlighter: highlighter, embedKinds: kinds, fonts: fonts, fill: fill);
        ed.WidgetResolver = key =>
        {
            if (key is not EmbedRef r) return null;
            if (r.Key == DocString.UiTypeId)
                return int.TryParse(r.Body.Trim(), out int i) && i >= 0 && i < holes.Count ? holes[i] : null;
            return fences is not null && fences.TryGetValue(r.Key, out Func<string, Widget>? f) ? f(r.Body) : null;
        };
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

    /// <summary>文書中のリンク <c>[text](url)</c> をリンクテキストの範囲 + URL で抽出する (純関数)。
    /// view の <see cref="TextEditorView.OnClickOffset"/> でクリック位置を当てて URL へ飛ばす。</summary>
    public static IReadOnlyList<MarkdownLink> Links(string text)
    {
        var list = new List<MarkdownLink>();
        foreach (Match m in Link.Matches(text))
        {
            Group g = m.Groups[1];
            list.Add(new MarkdownLink(g.Index, g.Index + g.Length, g.Value, m.Groups[2].Value));
        }
        return list;
    }

    /// <summary>見出しレベル (1..6) → 基準サイズへの倍率。</summary>
    public static float HeadingScale(int level) => level switch
    {
        1 => 1.9f, 2 => 1.6f, 3 => 1.35f, 4 => 1.2f, 5 => 1.1f, _ => 1.05f,
    };

    /// <summary>Markdown 全文 → 装飾集合 (純関数、フォント非依存 = GPU 不要でテスト可)。
    /// <paramref name="hideMarkers"/> = true (read-only 文書レンダラ) で記法マーカ (#/**/`/&gt;/-/[]() 等) を
    /// 淡色化ではなく**非表示** (幅0) にする。false (編集/live-preview) は従来どおり淡色。</summary>
    public static DecorationSet Build(string text, Theme t, bool hideMarkers = false, ISyntaxHighlighter? highlighter = null,
        IReadOnlyCollection<string>? embedKinds = null)
    {
        var marks = new List<Decoration>();
        var consumed = new bool[text.Length];
        uint muted = t.TextMuted;
        uint codeBg = Styles.WithAlpha(t.Text, 22);
        Decoration Marker(int from, int to) => hideMarkers
            ? new MarkDecoration(from, to, Hidden: true)
            : new MarkDecoration(from, to, Foreground: muted);
        // 埋め込みフェンス判定: ```embed <key> は常に、```<kind> は kind が embedKinds にあるとき。
        bool IsEmbedFence(string info, out string key)
        {
            if (info == "embed" || info.StartsWith("embed "))
            { key = info.Length > 5 ? info[5..].Trim() : ""; return true; }
            string first = info.Length > 0 ? info.Split(' ', 2)[0] : "";
            if (first.Length > 0 && embedKinds is not null && embedKinds.Contains(first)) { key = first; return true; }
            key = "";
            return false;
        }

        // --- 行単位: 埋め込み / 見出し / コードフェンス / 引用 / 箇条書き ---
        int lineStart = 0;
        bool inFence = false;
        string fenceLang = "";
        bool inEmbed = false;
        int embedStart = 0;
        string embedKey = "";
        var embedBody = new System.Text.StringBuilder();
        foreach (string line in text.Split('\n'))
        {
            int end = lineStart + line.Length;
            string trimmed = line.TrimStart();
            int indent = line.Length - trimmed.Length;

            // 埋め込みフェンス ```embed <key> <本文> ``` = 自動高さ block widget (view が key/本文で live UI を解決)
            if (inEmbed)
            {
                if (trimmed.StartsWith("```"))   // 閉じフェンス → 範囲全体を block widget に (本文を EmbedRef で渡す)
                {
                    marks.Add(new BlockWidgetDecoration(embedStart, end, new EmbedRef(embedKey, embedBody.ToString()), 0f));
                    inEmbed = false;
                }
                else
                {
                    if (embedBody.Length > 0) embedBody.Append('\n');
                    embedBody.Append(line);
                }
                Consume(consumed, lineStart, end);
                lineStart = end + 1;
                continue;
            }

            // ``` フェンス開閉 (行自体は淡色、間の行はコードブロック)。info が "embed" なら埋め込み開始。
            if (trimmed.StartsWith("```"))
            {
                string info = trimmed[3..].Trim();
                if (!inFence && IsEmbedFence(info, out string ek))
                {
                    embedStart = lineStart;
                    embedKey = ek;
                    embedBody.Clear();
                    inEmbed = true;
                    Consume(consumed, lineStart, end);
                    lineStart = end + 1;
                    continue;
                }
                marks.Add(Marker(lineStart, end));
                Consume(consumed, lineStart, end);
                fenceLang = inFence ? "" : info;   // 開き = info を言語に / 閉じ = クリア
                inFence = !inFence;
                lineStart = end + 1;
                continue;
            }
            if (inFence)
            {
                marks.Add(new LineDecoration(lineStart, codeBg));                        // 行背景
                if (end > lineStart) marks.Add(new MarkDecoration(lineStart, end, Variant: FontVariant.Mono));
                // シンタックスハイライトを装飾で (実テキストのまま = 選択可能・widget 化しない)
                if (highlighter is { } hl && fenceLang.Length > 0 && hl.Supports(fenceLang))
                    foreach (SyntaxToken tk in hl.Tokenize(fenceLang, line))
                        if (tk.Kind != TokenKind.Text && tk.Length > 0)
                            marks.Add(new MarkDecoration(lineStart + tk.Start, lineStart + tk.Start + tk.Length,
                                Foreground: CodeDecorations.TokenColor(t, tk.Kind)));
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
                marks.Add(Marker(lineStart, content));                                    // "# " マーカ
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
                marks.Add(Marker(gt, after));
                lineStart = end + 1;
                continue;
            }

            // 箇条書き / 番号付きリストのマーカを淡色 (本文はそのまま)
            if (trimmed.Length >= 2 && trimmed[1] == ' ' && trimmed[0] is '-' or '*' or '+')
                marks.Add(Marker(lineStart + indent, lineStart + indent + 2));
            else
            {
                int d = indent;
                while (d < line.Length && char.IsAsciiDigit(line[d])) d++;
                if (d > indent && d + 1 < line.Length && line[d] == '.' && line[d + 1] == ' ')
                    marks.Add(Marker(lineStart + indent, lineStart + d + 2));
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
            marks.Add(Marker(s, g.Index));                    // "["
            marks.Add(Marker(g.Index + g.Length, e));         // "](url)"
            Consume(consumed, s, e);
        }

        // --- 行内: コード / 太字 / 斜体 ---
        Inline(Code, text, consumed, marks, Marker, delim: 1,
            (from, to) => new MarkDecoration(from, to, Variant: FontVariant.Mono, Background: codeBg));
        Inline(Bold, text, consumed, marks, Marker, delim: 2,
            (from, to) => new MarkDecoration(from, to, Variant: FontVariant.Bold));
        Inline(Italic, text, consumed, marks, Marker, delim: 1,
            (from, to) => new MarkDecoration(from, to, Variant: FontVariant.Italic));

        return new DecorationSet(marks);
    }

    private static void Inline(Regex re, string text, bool[] consumed, List<Decoration> marks,
        Func<int, int, Decoration> marker, int delim, Func<int, int, MarkDecoration> inner)
    {
        foreach (Match m in re.Matches(text))
        {
            int s = m.Index, e = m.Index + m.Length;
            if (Overlaps(consumed, s, e)) continue;
            int a = s + delim, b = e - delim;
            if (b <= a) continue;
            marks.Add(inner(a, b));       // 本文にスタイル
            marks.Add(marker(s, a));      // 開始マーカ
            marks.Add(marker(b, e));      // 終了マーカ
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
public sealed class MarkdownProvider(Func<Theme> theme, bool hideMarkers = false, ISyntaxHighlighter? highlighter = null,
    IReadOnlyCollection<string>? embedKinds = null) : IDecorationProvider
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
        uint disc = t.Text ^ (t.TextMuted << 1) ^ CodeDecorations.TokenColor(t, TokenKind.Keyword);   // テーマ変化の検出子
        string text = state.Doc.Text;
        if (text == _lastText && disc == _lastDisc) return _cache;
        _lastText = text;
        _lastDisc = disc;
        _cache = MarkdownDecorations.Build(text, t, hideMarkers, highlighter, embedKinds);
        return _cache;
    }
}
