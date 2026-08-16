using System.Text.RegularExpressions;
using Luxel.Document;
using Luxel.Typography;
using Luxel.UI;
using Luxel.Resources;

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

/// <summary><see cref="TextEditorAppearance"/> で上書きできる Markdown ブロック種別キー。</summary>
public static class MarkdownBlockKinds
{
    public const string Paragraph = "markdown.paragraph";
    public const string Quote = "markdown.quote";
    public const string CodeBlock = "markdown.code-block";
    public const string BulletList = "markdown.list.bullet";
    public const string OrderedList = "markdown.list.ordered";
    public const string TaskList = "markdown.list.task";
    public const string HorizontalRule = "markdown.horizontal-rule";
    public const string Table = "markdown.table";
    public static string Heading(int level) => $"markdown.heading.{Math.Clamp(level, 1, 6)}";
}

/// <summary>Markdown を「文書として描く」ワンショット (WS-A / ADR-0012) — <see cref="TextEditorView"/> に
/// <see cref="MarkdownProvider"/> を付け、read-only + 折返しで束ねる。表示は行、装飾は provider。
/// Gallery の docs レンダラ (旧 <c>Kit.Docs</c> の後継) はこれ。</summary>
public static class MarkdownDoc
{
    /// <summary>設定済みの文書レンダラを作る。<paramref name="body"/> が null なら既定 (テーマ) フォント。</summary>
    public static TextEditorView Create(Signal<string> markdown, Func<Theme> theme, float width, float height,
        VectorFont? body = null, VectorFont? bold = null, VectorFont? italic = null,
        VectorFont? boldItalic = null, VectorFont? mono = null, bool wrap = true, ISyntaxHighlighter? highlighter = null,
        IReadOnlyCollection<string>? embedKinds = null, FontCollection? fonts = null, bool fill = false, bool editable = false,
        TextEditorAppearance? appearance = null, ResourceSystem? resources = null)
    {
        TextEditorView ed = Kit.TextEditorView(markdown, editorHeight: height, editorWidth: width);
        if (body is not null) ed.EditorFont = body;
        if (fonts is not null) ed.Fonts = fonts;   // 日本語/絵文字フォールバック用フォント列
        ed.Fill = fill;   // true = 領域いっぱい (文書ページ)。width/height は初期見積り
        ed.BoldFont = bold;
        ed.ItalicFont = italic;
        ed.BoldItalicFont = boldItalic;
        ed.MonoFont = mono;
        ed.Appearance = appearance ?? TextEditorAppearance.Default;
        ed.WrapText = wrap;
        ed.WrapLineHeight = 1.3f;   // 段落内はブロック間 (1.5) より詰める
        ed.ReadOnly = !editable;    // editable=true は Live Preview 編集モード (キャレット行のみマーカを見せる)
        ed.ShowBlockControls = editable;
        ed.DocSource = markdown.Peek();   // docs 索引用 (realize 不要で本文/見出し/リンクを取れる)
        ed.BlockProvider = MarkdownEditorFeatures.BlockProvider;
        ed.InsertItems = MarkdownEditorFeatures.InsertItems;
        ed.SelectionActions = MarkdownEditorFeatures.SelectionActions;
        ed.WidgetResolver = key => MarkdownBlockEmbeds.Resolve(ed, key, resources, MathF.Max(80, width - 64));
        // 文書レンダラ: マーカ非表示 + コード色分け + 埋め込み。editable なら live-preview (キャレット行だけ raw)。
        ed.Providers.Add(new MarkdownProvider(() => ed.ResolveDocumentTheme(theme), hideMarkers: true, highlighter, embedKinds, livePreview: editable,
            appearance: () => ed.Appearance));
        return ed;
    }

    /// <summary>既存の <see cref="DocString"/> (```luxel-ui の UI hole を含む markdown) を新スタックで描く橋。
    /// これで <c>Docs($"...{Widget}...")</c> 記法のページを 1 行変更で新スタックへ移行できる。
    /// block hole (luxel-ui) は <see cref="DocString.HoleWidgets"/> で解決。mermaid/数式など外部ドメインの
    /// フェンスは <paramref name="fences"/> (kind → 本文で widget を作る) で注入する (Controls は Diagram/MathText 非依存)。
    /// インライン hole (<c>[￼](luxel-ui:N)</c>) は後段。</summary>
    public static TextEditorView FromDoc(DocString content, Func<Theme> theme, float width, float height,
        VectorFont? body = null, VectorFont? bold = null, VectorFont? mono = null, ISyntaxHighlighter? highlighter = null,
        IReadOnlyDictionary<string, Func<string, Widget>>? fences = null, FontCollection? fonts = null, bool fill = false,
        bool toc = false)
    {
        IReadOnlyList<Widget> holes = content.HoleWidgets;
        var kinds = new HashSet<string> { DocString.UiTypeId };
        if (fences is not null) foreach (string k in fences.Keys) kinds.Add(k);
        string md = toc ? InsertToc(content.Md) : content.Md;   // TOC = アンカー付き markdown リスト (hole 番号は不変)
        TextEditorView ed = Create(new Signal<string>(md), theme, width, height,
            body: body, bold: bold, mono: mono, highlighter: highlighter, embedKinds: kinds, fonts: fonts, fill: fill);
        ed.DocEmbeds = content.Embeds;
        Func<object, Widget?>? standardResolver = ed.WidgetResolver;
        ed.WidgetResolver = key =>
        {
            if (key is not EmbedRef r) return standardResolver?.Invoke(key);
            if (r.Key == DocString.UiTypeId)
                return int.TryParse(r.Body.Trim(), out int i) && i >= 0 && i < holes.Count ? holes[i] : null;
            return fences is not null && fences.TryGetValue(r.Key, out Func<string, Widget>? f) ? f(r.Body) : standardResolver?.Invoke(key);
        };
        return ed;
    }

    /// <summary>見出しアンカーの slug: lowercase + 空白 (半角/全角) → <c>-</c>、さらに
    /// markdown リンク URL <c>](…)</c> を壊す丸括弧/角括弧を除去する。見出しに <c>(...)</c> があっても
    /// TOC の <c>#アンカー</c> が正しく張れる (括弧が残ると URL 内の <c>)</c> がリンクを途中で閉じる)。
    /// TOC 生成・ナビ・デッドリンク検証で共通に使う。</summary>
    public static string Slug(string heading)
    {
        string s = heading.Trim().ToLowerInvariant().Replace(' ', '-').Replace('　', '-');
        var sb = new System.Text.StringBuilder(s.Length);
        foreach (char c in s) if (c is not ('(' or ')' or '（' or '）' or '[' or ']')) sb.Append(c);
        return sb.ToString();
    }

    /// <summary>TOC = アンカーリンク付き markdown リストを最初の H1 直後 (無ければ先頭) へ挿入する。
    /// ただの markdown なのでフォントもリンク機構 (<c>#アンカー</c>→スクロール) もそのまま効く。
    /// H2/H3 が対象・コードフェンス内は無視。slug は <see cref="Slug"/> で本文と一致させる。</summary>
    public static string InsertToc(string md)
    {
        const string tocMarker = "<!-- luxel-toc -->";
        if (md.Contains(tocMarker, StringComparison.Ordinal)) return md;
        string[] lines = md.Split('\n');
        var toc = new List<string>();
        bool inFence = false;
        foreach (string l in lines)
        {
            if (l.TrimStart().StartsWith("```")) { inFence = !inFence; continue; }
            if (inFence) continue;
            if (l.StartsWith("## ")) toc.Add($"- [{l[3..].Trim()}](#{Slug(l[3..])})");
            else if (l.StartsWith("### ")) toc.Add($"  - [{l[4..].Trim()}](#{Slug(l[4..])})");
        }
        if (toc.Count == 0) return md;
        string block = tocMarker + "\n" + string.Join('\n', toc) + "\n<!-- /luxel-toc -->";
        for (int i = 0; i < lines.Length; i++)
            if (lines[i].StartsWith("# ")) { lines[i] += "\n\n" + block; return string.Join('\n', lines); }
        return block + "\n\n" + md;
    }

    /// <summary><c>Toc()</c> が生成した placeholder の位置へ H2/H3 の目次を展開する。</summary>
    public static string RenderTocPlaceholder(string md)
    {
        const string placeholder = "<!-- luxel-toc-placeholder -->";
        if (!md.Contains(placeholder, StringComparison.Ordinal)) return md;

        string generated = InsertToc(md.Replace(placeholder, string.Empty, StringComparison.Ordinal));
        const string marker = "<!-- luxel-toc -->";
        const string closing = "<!-- /luxel-toc -->";
        int start = generated.IndexOf(marker, StringComparison.Ordinal);
        int end = generated.IndexOf(closing, StringComparison.Ordinal);
        if (start < 0 || end < start)
            return md.Replace(placeholder, string.Empty, StringComparison.Ordinal);

        end += closing.Length;
        string block = generated[start..end];
        return md.Replace(placeholder, block, StringComparison.Ordinal);
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
        1 => 2f, 2 => 1.5f, 3 => 1.17f, 4 => 1f, 5 => 0.83f, _ => 0.67f,
    };

    /// <summary>Markdown 全文 → 装飾集合 (純関数、フォント非依存 = GPU 不要でテスト可)。
    /// <paramref name="hideMarkers"/> = true (read-only 文書レンダラ) で記法マーカ (#/**/`/&gt;/-/[]() 等) を
    /// 淡色化ではなく**非表示** (幅0) にする。false (編集/live-preview) は従来どおり淡色。</summary>
    public static DecorationSet Build(string text, Theme t, bool hideMarkers = false, ISyntaxHighlighter? highlighter = null,
        IReadOnlyCollection<string>? embedKinds = null, Func<int, bool>? reveal = null,
        TextEditorAppearance? appearance = null)
    {
        var marks = new List<Decoration>();
        var consumed = new bool[text.Length];
        uint muted = t.TextMuted;
        uint codeBg = Styles.WithAlpha(t.Text, 22);
        TextEditorBlockAppearance Resolve(string kind, TextEditorBlockAppearance fallback)
        {
            TextEditorBlockAppearance? value = appearance?.Block(kind);
            return value is null ? fallback : new TextEditorBlockAppearance(
                FontSize: value.FontSize ?? fallback.FontSize,
                FontScale: value.FontScale ?? fallback.FontScale,
                FontVariant: value.FontVariant ?? fallback.FontVariant,
                Foreground: value.Foreground ?? fallback.Foreground,
                Background: value.Background ?? fallback.Background,
                Accent: value.Accent ?? fallback.Accent,
                Indent: value.Indent ?? fallback.Indent,
                BarWidth: value.BarWidth ?? fallback.BarWidth);
        }
        void AddTextStyle(List<Decoration> target, int from, int to, TextEditorBlockAppearance style)
        {
            if (to <= from) return;
            float? scale = style.FontSize is { } size
                ? size / (appearance?.FontSize ?? TextEditorAppearance.Default.FontSize ?? t.FontSm)
                : style.FontScale;
            if (scale is null && style.FontVariant is null && style.Foreground is null) return;
            target.Add(new MarkDecoration(from, to, Foreground: style.Foreground,
                Variant: style.FontVariant, FontScale: scale));
        }
        // live-preview: reveal(pos)=true の行 (キャレット行) はマーカを畳まず淡色で見せる (Typora 風)。
        bool Hide(int pos) => hideMarkers && reveal?.Invoke(pos) != true;
        Decoration Marker(int from, int to) => Hide(from)
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

        // GFM table は非アクティブ時に標準 TableBlock へ置換する。キャレットが表内なら raw source に戻す。
        foreach (MarkdownTableSpan table in MarkdownBlockEmbeds.Tables(text))
        {
            bool revealed = false;
            if (reveal is not null)
            {
                int at = table.From;
                while (at <= table.To)
                {
                    if (reveal(at)) { revealed = true; break; }
                    int newline = text.IndexOf('\n', at);
                    if (newline < 0 || newline >= table.To) break;
                    at = newline + 1;
                }
            }
            if (hideMarkers && !revealed)
            {
                marks.Add(new BlockWidgetDecoration(table.From, table.To, table.Ref, 0f));
                Consume(consumed, table.From, table.To);
            }
        }

        // --- 行単位: 埋め込み / 見出し / コードフェンス / 引用 / 箇条書き ---
        int lineStart = 0;
        bool inFence = false;
        string fenceLang = "";
        int codeStart = 0, codeLastEnd = 0;
        TextEditorBlockAppearance? activeCodeStyle = null;
        bool inEmbed = false;
        int embedStart = 0;
        string embedKey = "";
        var embedBody = new System.Text.StringBuilder();
        foreach (string line in text.Split('\n'))
        {
            int end = lineStart + line.Length;
            string trimmed = line.TrimStart();
            int indent = line.Length - trimmed.Length;

            if (end > lineStart && Overlaps(consumed, lineStart, end))
            {
                lineStart = end + 1;
                continue;
            }

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
                if (inFence && activeCodeStyle is { } closingStyle && codeLastEnd >= codeStart
                    && closingStyle.Background is { } blockBackground)
                    marks.Add(new BlockDecoration(codeStart, codeLastEnd, Background: blockBackground,
                        Indent: closingStyle.Indent ?? 12f, Radius: 4f));
                marks.Add(Marker(lineStart, end));
                Consume(consumed, lineStart, end);
                if (!inFence)
                {
                    fenceLang = info;
                    codeStart = end + 1;
                    codeLastEnd = codeStart;
                    activeCodeStyle = Resolve(MarkdownBlockKinds.CodeBlock,
                        new TextEditorBlockAppearance(FontSize: 14f, FontVariant: FontVariant.Mono,
                            Background: codeBg, Indent: 12f));
                }
                else { fenceLang = ""; activeCodeStyle = null; }
                inFence = !inFence;
                lineStart = end + 1;
                continue;
            }
            if (inFence)
            {
                TextEditorBlockAppearance codeStyle = activeCodeStyle ?? Resolve(MarkdownBlockKinds.CodeBlock,
                    new TextEditorBlockAppearance(FontSize: 14f, FontVariant: FontVariant.Mono,
                        Background: codeBg, Indent: 12f));
                codeLastEnd = end;
                AddTextStyle(marks, lineStart, end, codeStyle);
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

            // HTMLコメントはMarkdown文書のメタデータであり、read-only表示では描画しない。
            // TOC展開が生成する <!-- luxel-toc --> / <!-- /luxel-toc --> もここで幅0になる。
            if (trimmed.StartsWith("<!--", StringComparison.Ordinal)
                && trimmed.EndsWith("-->", StringComparison.Ordinal))
            {
                marks.Add(Hide(lineStart)
                    ? new MarkDecoration(lineStart, end, Hidden: true)
                    : new MarkDecoration(lineStart, end, Foreground: muted));
                Consume(consumed, lineStart, end);
                lineStart = end + 1;
                continue;
            }

            // ATX 見出し (# .. ######、行頭)
            int h = 0;
            while (h < line.Length && line[h] == '#') h++;
            if (h is >= 1 and <= 6 && h < line.Length && line[h] == ' ')
            {
                int content = lineStart + h + 1;
                TextEditorBlockAppearance headingStyle = Resolve(MarkdownBlockKinds.Heading(h),
                    new TextEditorBlockAppearance(FontScale: HeadingScale(h), FontVariant: FontVariant.Bold, Foreground: t.Text));
                marks.Add(Marker(lineStart, content));                                    // "# " マーカ
                if (headingStyle.Background is { } headingBackground) marks.Add(new LineDecoration(lineStart, headingBackground));
                AddTextStyle(marks, content, end, headingStyle);
                Consume(consumed, lineStart, end);                                       // 本文のインライン再走査は行わない
                lineStart = end + 1;
                continue;
            }

            // 引用 (> ...): 左縦バー + インデント、マーカは淡色。本文のインラインは効かせる
            if (trimmed.StartsWith("> ") || trimmed == ">")
            {
                TextEditorBlockAppearance quoteStyle = Resolve(MarkdownBlockKinds.Quote,
                    new TextEditorBlockAppearance(Accent: muted, Indent: 12f, BarWidth: 3f));
                if (end > lineStart) marks.Add(new BlockDecoration(lineStart, end, Background: quoteStyle.Background,
                    BarColor: quoteStyle.Accent, BarWidth: quoteStyle.BarWidth ?? 3f, Indent: quoteStyle.Indent ?? 0f));
                int gt = lineStart + indent;
                int after = Math.Min(gt + (trimmed.StartsWith("> ") ? 2 : 1), end);
                marks.Add(Marker(gt, after));
                AddTextStyle(marks, after, end, quoteStyle);
                lineStart = end + 1;
                continue;
            }

            // 水平線。read-only ではソースを畳み、行 prefix でテーマ色の罫線として見せる。
            if (MarkdownEditorFeatures.IsHorizontalRule(trimmed))
            {
                TextEditorBlockAppearance ruleStyle = Resolve(MarkdownBlockKinds.HorizontalRule,
                    new TextEditorBlockAppearance(Accent: muted));
                if (Hide(lineStart))
                {
                    marks.Add(new MarkDecoration(lineStart, end, Hidden: true));
                    marks.Add(new LinePrefixDecoration(lineStart, "────────────────", ruleStyle.Accent ?? muted));
                }
                else marks.Add(Marker(lineStart, end));
                Consume(consumed, lineStart, end);
                lineStart = end + 1;
                continue;
            }

            // 単独画像行は非アクティブ時に ImageBlock へ置換。編集行では raw Markdown を見せる。
            if (MarkdownBlockEmbeds.TryImage(line, out MarkdownImageRef image) && Hide(lineStart))
            {
                marks.Add(new BlockWidgetDecoration(lineStart, end, image, 0f));
                Consume(consumed, lineStart, end);
                lineStart = end + 1;
                continue;
            }

            // 箇条書き / 番号付きリスト。read-only (hideMarkers) は源の "- " を畳んで
            // 行頭 prefix で "• " (番号は "N. " のまま) を出す = マーカ非表示でも箇条書きに見える。
            // 編集モード (hideMarkers=false) は従来どおりマーカを淡色化 (本文はそのまま)。
            if (MarkdownEditorFeatures.IsTask(trimmed))
            {
                TextEditorBlockAppearance taskStyle = Resolve(MarkdownBlockKinds.TaskList,
                    new TextEditorBlockAppearance(Accent: muted));
                int markerEnd = lineStart + indent + 6;
                if (taskStyle.Background is { } taskBackground) marks.Add(new LineDecoration(lineStart, taskBackground));
                if (Hide(lineStart))
                {
                    string glyph = trimmed[3] is 'x' or 'X' ? "☑ " : "☐ ";
                    ListBullet(marks, lineStart, markerEnd, indent, glyph, taskStyle.Accent ?? muted);
                }
                else marks.Add(Marker(lineStart + indent, markerEnd));
                AddTextStyle(marks, markerEnd, end, taskStyle);
            }
            else if (trimmed.Length >= 2 && trimmed[1] == ' ' && trimmed[0] is '-' or '*' or '+')
            {
                TextEditorBlockAppearance listStyle = Resolve(MarkdownBlockKinds.BulletList,
                    new TextEditorBlockAppearance(Accent: muted));
                if (listStyle.Background is { } listBackground) marks.Add(new LineDecoration(lineStart, listBackground));
                if (Hide(lineStart)) ListBullet(marks, lineStart, lineStart + indent + 2, indent, "• ", listStyle.Accent ?? muted);
                else marks.Add(Marker(lineStart + indent, lineStart + indent + 2));
                AddTextStyle(marks, lineStart + indent + 2, end, listStyle);
            }
            else
            {
                int d = indent;
                while (d < line.Length && char.IsAsciiDigit(line[d])) d++;
                if (d > indent && d + 1 < line.Length && line[d] == '.' && line[d + 1] == ' ')
                {
                    TextEditorBlockAppearance listStyle = Resolve(MarkdownBlockKinds.OrderedList,
                        new TextEditorBlockAppearance(Accent: muted));
                    if (listStyle.Background is { } listBackground) marks.Add(new LineDecoration(lineStart, listBackground));
                    if (Hide(lineStart)) ListBullet(marks, lineStart, lineStart + d + 2, indent, line[indent..(d + 1)] + " ", listStyle.Accent ?? muted);
                    else marks.Add(Marker(lineStart + indent, lineStart + d + 2));
                    AddTextStyle(marks, lineStart + d + 2, end, listStyle);
                }
                else
                {
                    TextEditorBlockAppearance paragraphStyle = Resolve(MarkdownBlockKinds.Paragraph, new TextEditorBlockAppearance());
                    if (paragraphStyle.Background is { } paragraphBackground) marks.Add(new LineDecoration(lineStart, paragraphBackground));
                    AddTextStyle(marks, lineStart, end, paragraphStyle);
                }
            }
            lineStart = end + 1;   // +1 = '\n'
        }

        if (inFence && activeCodeStyle is { } unfinishedStyle && codeLastEnd >= codeStart
            && unfinishedStyle.Background is { } unfinishedBackground)
            marks.Add(new BlockDecoration(codeStart, codeLastEnd, Background: unfinishedBackground,
                Indent: unfinishedStyle.Indent ?? 12f, Radius: 4f));

        // --- 行内: リンク [text](url) → text をアクセント色+下線、括弧/URL は淡色 ---
        // ただし url が `<kind>:<body>` で kind が埋め込み種別なら**行内 widget** (WidgetDecoration) に置換
        // する — DocString の `[￼](luxel-ui:N)` インライン hole がこれ (自動サイズ、view が key で解決)。
        uint link = t.Primary;
        foreach (Match m in Link.Matches(text))
        {
            int s = m.Index, e = m.Index + m.Length;
            if (Overlaps(consumed, s, e)) continue;
            string url = m.Groups[2].Value;
            int colon = url.IndexOf(':');
            if (colon > 0 && embedKinds is not null && embedKinds.Contains(url[..colon]))
            {
                // `[￼](kind:body)` 全体 [s,e) を自動サイズ行内 widget に置換
                marks.Add(new WidgetDecoration(s, e, 0f, 0f, new EmbedRef(url[..colon], url[(colon + 1)..])));
                Consume(consumed, s, e);
                continue;
            }
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

    /// <summary>read-only 文書のリスト行: 源のインデント+マーカ <c>[lineStart, hideEnd)</c> を畳み、
    /// 行頭 prefix で <c>インデント空白 + glyph</c> ("• " / "1. ") を淡色で出す。マーカ非表示でも
    /// 箇条書きに見え、ネストは prefix の空白でインデントが保たれる。</summary>
    private static void ListBullet(List<Decoration> marks, int lineStart, int hideEnd, int indent, string glyph, uint color)
    {
        marks.Add(new MarkDecoration(lineStart, hideEnd, Hidden: true));
        marks.Add(new LinePrefixDecoration(lineStart, new string(' ', indent) + glyph, color));
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
    IReadOnlyCollection<string>? embedKinds = null, bool livePreview = false,
    Func<TextEditorAppearance>? appearance = null) : IDecorationProvider
{
    private string? _lastText;
    private int _lastDisc;
    private string _lastReveal = "";
    private TextEditorAppearance? _lastAppearance;
    private DecorationSet _cache = DecorationSet.Empty;

    /// <inheritdoc/>
    public string Owner => "markdown";

    /// <inheritdoc/>
    public DecorationSet Provide(EditorState state)
    {
        Theme t = theme();
        TextEditorAppearance currentAppearance = appearance?.Invoke() ?? TextEditorAppearance.Default;
        int disc = ThemeDiscriminator(t);
        string text = state.Doc.Text;
        // live-preview: キャレット/選択のある行を reveal (マーカを畳まず淡色で見せる = Typora 風の編集モード)
        HashSet<int>? revealLines = null;
        string revealSig = "";
        if (livePreview)
        {
            revealLines = new HashSet<int>();
            foreach (SelectionRange r in state.Selection.Ranges)
                for (int l = state.Doc.LineOf(r.From); l <= state.Doc.LineOf(r.To); l++) revealLines.Add(l);
            revealSig = string.Join(",", revealLines.Order());
        }
        if (text == _lastText && disc == _lastDisc && revealSig == _lastReveal
            && ReferenceEquals(currentAppearance, _lastAppearance)) return _cache;
        _lastText = text; _lastDisc = disc; _lastReveal = revealSig; _lastAppearance = currentAppearance;
        Func<int, bool>? reveal = revealLines is null ? null : pos => revealLines.Contains(state.Doc.LineOf(pos));
        _cache = MarkdownDecorations.Build(text, t, hideMarkers, highlighter, embedKinds, reveal, currentAppearance);
        return _cache;
    }

    private static int ThemeDiscriminator(Theme t)
    {
        var hash = new HashCode();
        hash.Add(t.Text);
        hash.Add(t.TextMuted);
        hash.Add(t.Primary);
        hash.Add(t.TokComment);
        hash.Add(t.TokString);
        hash.Add(t.TokEscape);
        hash.Add(t.TokRegexp);
        hash.Add(t.TokNumber);
        hash.Add(t.TokConstant);
        hash.Add(t.TokKeyword);
        hash.Add(t.TokKeywordControl);
        hash.Add(t.TokOperator);
        hash.Add(t.TokFunction);
        hash.Add(t.TokType);
        hash.Add(t.TokVariable);
        hash.Add(t.TokTag);
        hash.Add(t.TokAttribute);
        return hash.ToHashCode();
    }
}
