using Luxel.Controls;
using Luxel.Document;
using Luxel.UI;

namespace Luxel.Tests;

/// <summary>Markdown → 装飾 (WS-A / ADR-0012) の単体テスト — 見出し (Bold+サイズ)・太字・斜体・
/// インラインコード (Mono+背景) が正しいソース範囲・変種で出ること。純関数・フォント非依存 (GPU 不要)。</summary>
public class MarkdownDecorationsTests
{
    private static readonly Theme T = Theme.Light;
    private static DecorationSet Build(string s) => MarkdownDecorations.Build(s, T);
    private static MarkDecoration At(DecorationSet set, int from, int to)
        => set.OfKind<MarkDecoration>().First(m => m.From == from && m.To == to);

    [Fact]
    public void Heading_IsBoldAndScaled_MarkerMuted()
    {
        var set = Build("# Title");                     // '#'=0 ' '=1 "Title"=[2,7)
        MarkDecoration head = At(set, 2, 7);
        Assert.Equal(FontVariant.Bold, head.Variant);
        Assert.Equal(1.9f, head.FontScale!.Value, 3);   // h1 = 1.9x
        Assert.Equal(T.Text, head.Foreground);
        Assert.Equal(T.TextMuted, At(set, 0, 2).Foreground);   // "# " マーカは淡色
    }

    [Fact]
    public void HideMarkers_MarkerIsHiddenNotMuted()
    {
        var set = MarkdownDecorations.Build("# Title", T, hideMarkers: true);
        MarkDecoration marker = At(set, 0, 2);   // "# "
        Assert.True(marker.Hidden);              // 非表示 (幅0)
        Assert.Null(marker.Foreground);          // 淡色化ではない
        Assert.Equal(FontVariant.Bold, At(set, 2, 7).Variant);   // 本文は従来どおり
    }

    [Fact]
    public void ReadOnlyList_HidesMarker_ShowsBulletPrefix()
    {
        // "- item" : read-only は源の "- " を畳み、行頭 prefix "• " を出す
        var set = MarkdownDecorations.Build("- item", T, hideMarkers: true);
        Assert.True(At(set, 0, 2).Hidden);   // "- " は非表示
        LinePrefixDecoration pre = set.OfKind<LinePrefixDecoration>().Single();
        Assert.Equal("• ", pre.Text);
        Assert.Equal(0, pre.At);
        Assert.Equal(T.TextMuted, pre.Color);
    }

    [Fact]
    public void ReadOnlyList_NestedAndOrdered_PreservesIndentAndNumber()
    {
        // ネスト箇条書き: indent 空白 + "• "、番号リストは "1. " をそのまま prefix に
        var nested = MarkdownDecorations.Build("  - deep", T, hideMarkers: true);
        Assert.Equal("  • ", nested.OfKind<LinePrefixDecoration>().Single().Text);
        var ordered = MarkdownDecorations.Build("1. first", T, hideMarkers: true);
        Assert.Equal("1. ", ordered.OfKind<LinePrefixDecoration>().Single().Text);
        Assert.True(ordered.OfKind<MarkDecoration>().Any(m => m is { From: 0, To: 3, Hidden: true }));  // "1. " 畳み
    }

    [Fact]
    public void EditMode_List_MarkerMutedNotBulleted()
    {
        // hideMarkers=false (編集) は従来どおりマーカ淡色・prefix なし
        var set = MarkdownDecorations.Build("- item", T);
        Assert.Empty(set.OfKind<LinePrefixDecoration>());
        Assert.Equal(T.TextMuted, At(set, 0, 2).Foreground);
    }

    [Fact]
    public void Heading_Level_ScalesDown()
    {
        Assert.Equal(MarkdownDecorations.HeadingScale(2), At(Build("## Sub"), 3, 6).FontScale!.Value, 3);
    }

    [Fact]
    public void Bold_StylesInner_NotDelimiters()
    {
        var set = Build("a **b** c");                   // '*'=2,3 'b'=4 '*'=5,6
        Assert.Equal(FontVariant.Bold, At(set, 4, 5).Variant);
        Assert.DoesNotContain(set.OfKind<MarkDecoration>(), m => m.Variant == FontVariant.Italic);
    }

    [Fact]
    public void Italic_StylesInner()
    {
        Assert.Equal(FontVariant.Italic, At(Build("a *b* c"), 3, 4).Variant);   // 'a'0 ' '1 '*'2 'b'3 '*'4
    }

    [Fact]
    public void InlineCode_IsMonoWithBackground()
    {
        var set = Build("x `y` z");                     // 'x'0 ' '1 '`'2 'y'3 '`'4
        MarkDecoration code = At(set, 3, 4);
        Assert.Equal(FontVariant.Mono, code.Variant);
        Assert.NotNull(code.Background);
    }

    [Fact]
    public void HeadingLine_DoesNotRescanInline()
    {
        // 見出し行内の "**" は消費済みなので二重に太字マークされない (見出しマークのみ)
        var set = Build("# a **b** c");
        Assert.DoesNotContain(set.OfKind<MarkDecoration>(), m => m.From == 7 && m.Variant == FontVariant.Bold && m.FontScale is null);
    }

    [Fact]
    public void Blockquote_HasBarAndMutedMarker()
    {
        var set = Build("> quoted");                          // '>'0 ' '1 "quoted"=[2,8)
        Assert.Contains(set.OfKind<BlockDecoration>(), b => b.From == 0 && b.BarColor == T.TextMuted);
        Assert.Equal(T.TextMuted, At(set, 0, 2).Foreground);  // "> " マーカは淡色
    }

    [Fact]
    public void FencedCode_LinesGetBackgroundAndMono()
    {
        var set = Build("```\ncode\n```");                    // ```=[0,3) \n=3 code=[4,8) \n=8 ```=[9,12)
        Assert.Contains(set.OfKind<LineDecoration>(), l => l.At == 4);          // コード行の背景
        Assert.Equal(FontVariant.Mono, At(set, 4, 8).Variant);                  // コード本文は等幅
    }

    [Fact]
    public void Headings_ExtractsLevelsTextOffsets()
    {
        var hs = MarkdownDecorations.Headings("# One\ntext\n## Two\n### Three");
        Assert.Equal(3, hs.Count);
        Assert.Equal((1, "One"), (hs[0].Level, hs[0].Text));
        Assert.Equal((2, "Two"), (hs[1].Level, hs[1].Text));
        Assert.Equal(11, hs[1].Offset);                 // "# One\ntext\n" = 11
        Assert.Equal("Three", hs[2].Text);
    }

    [Fact]
    public void Headings_SkipsFencedHashes()
    {
        var hs = MarkdownDecorations.Headings("# Real\n```\n# not a heading\n```");
        Assert.Single(hs);
        Assert.Equal("Real", hs[0].Text);
    }

    [Fact]
    public void Links_ExtractsTextRangeAndUrl()
    {
        var ls = MarkdownDecorations.Links("see [docs](http://x) and [more](y)");   // "see [" = 5
        Assert.Equal(2, ls.Count);
        Assert.Equal((5, 9), (ls[0].From, ls[0].To));
        Assert.Equal("docs", ls[0].Text);
        Assert.Equal("http://x", ls[0].Url);
        Assert.Equal("y", ls[1].Url);
    }

    [Fact]
    public void InsertToc_AddsAnchorListAfterH1_SkipsFencedHashes()
    {
        string md = MarkdownDoc.InsertToc("# Title\n\nintro\n## First Section\ntext\n### Sub A\n```\n## not a heading\n```\n## Second");
        // TOC は H1 直後に挿入され、H2 は "- [..](#slug)"、H3 は 2 スペースインデント
        Assert.Contains("# Title\n\n<!-- luxel-toc -->\n- [First Section](#first-section)", md);
        Assert.Contains("  - [Sub A](#sub-a)", md);
        Assert.Contains("- [Second](#second)", md);
        Assert.DoesNotContain("(#not-a-heading)", md);   // フェンス内の ## は無視
        // アンカーの slug は Headings の本文と一致する (ナビが解決できる)
        foreach (MarkdownHeading h in MarkdownDecorations.Headings(md).Where(x => x.Level >= 2))
            Assert.Contains($"(#{MarkdownDoc.Slug(h.Text)})", md);
    }

    [Fact]
    public void RenderTocPlaceholder_ExpandsAtTheAuthoredPosition()
    {
        string md = "# Title\n\nintro\n\n<!-- luxel-toc-placeholder -->\n\noutro\n\n## Section";
        string rendered = MarkdownDoc.RenderTocPlaceholder(md);

        Assert.Contains("intro\n\n<!-- luxel-toc -->\n- [Section](#section)\n<!-- /luxel-toc -->\n\noutro", rendered);
        Assert.DoesNotContain("luxel-toc-placeholder", rendered);
    }

    [Fact]
    public void LivePreview_RevealedLineShowsMarkerRaw_OthersHidden()
    {
        // reveal(pos)=true の行 (キャレット行) はマーカを淡色 raw、他行は非表示 — Typora 風編集モード
        var set = MarkdownDecorations.Build("# A\n# B", T, hideMarkers: true, reveal: pos => pos < 4);   // 行0 のみ
        Assert.False(At(set, 0, 2).Hidden);                     // 行0 "# " は raw
        Assert.Equal(T.TextMuted, At(set, 0, 2).Foreground);
        Assert.True(At(set, 4, 6).Hidden);                       // 行1 "# " は非表示
    }

    [Fact]
    public void LivePreview_RevealedListLine_ShowsRawDash_NotBullet()
    {
        // reveal した箇条書き行は "- " を raw (淡色) で見せ、bullet prefix は出さない
        var set = MarkdownDecorations.Build("- a\n- b", T, hideMarkers: true, reveal: pos => pos < 4);   // 行0 のみ
        Assert.Empty(set.OfKind<LinePrefixDecoration>().Where(p => p.At == 0));   // 行0 は bullet なし
        Assert.Equal(T.TextMuted, At(set, 0, 2).Foreground);                       // 行0 "- " は raw
        Assert.Single(set.OfKind<LinePrefixDecoration>().Where(p => p.At == 4));   // 行1 は bullet (•)
    }

    [Fact]
    public void InlineHole_BecomesWidgetDecoration_NotTextLink()
    {
        // `[￼](luxel-ui:2)` (DocString のインライン hole) は埋め込み種別 luxel-ui が embedKinds にあれば
        // 行内 widget (WidgetDecoration、自動サイズ) に置換される — テキストリンクではない。
        var kinds = new HashSet<string> { "luxel-ui" };
        var set = MarkdownDecorations.Build("状態 [￼](luxel-ui:2) です", T, hideMarkers: true, embedKinds: kinds);
        WidgetDecoration wd = set.OfKind<WidgetDecoration>().Single();
        Assert.Equal("状態 ".Length, wd.From);                       // `[` の位置
        Assert.Equal("状態 [￼](luxel-ui:2)".Length, wd.To);          // `)` の直後まで置換
        Assert.True(wd.Width <= 0 && wd.Height <= 0);                // 自動サイズ
        Assert.Equal(new EmbedRef("luxel-ui", "2"), wd.Key);
    }

    [Fact]
    public void InlineHole_UnknownScheme_StaysTextLink()
    {
        // 埋め込み種別でない url (http 等) は従来どおりテキストリンクのまま
        var set = MarkdownDecorations.Build("[x](https://a)", T, embedKinds: new HashSet<string> { "luxel-ui" });
        Assert.Empty(set.OfKind<WidgetDecoration>());
    }

    [Fact]
    public void Slug_StripsParens_SoAnchorUrlDoesNotBreak()
    {
        // 見出しに (...) があっても slug から括弧を除く → TOC の #アンカーが Links で正しく抽出できる
        Assert.Equal("セグメンタ-itextsegmenter", MarkdownDoc.Slug("セグメンタ (ITextSegmenter)"));
        string md = MarkdownDoc.InsertToc("# T\n\n## セグメンタ (ITextSegmenter)\nx");
        MarkdownLink toc = MarkdownDecorations.Links(md).Single(l => l.Url.StartsWith("#"));
        Assert.Equal("#セグメンタ-itextsegmenter", toc.Url);   // URL 内に ) が無く途中で閉じない
        Assert.False(toc.Url.Contains(')'));
    }

    [Fact]
    public void InsertToc_IsIdempotent()
    {
        string once = MarkdownDoc.InsertToc("# Title\n\n## Section\nbody");
        string twice = MarkdownDoc.InsertToc(once);
        Assert.Equal(once, twice);
        Assert.Equal(1, once.Split("<!-- luxel-toc -->").Length - 1);
    }

    [Fact]
    public void InsertToc_NoHeadings_ReturnsUnchanged()
        => Assert.Equal("# Only title\n\nbody", MarkdownDoc.InsertToc("# Only title\n\nbody"));

    [Fact]
    public void Link_TextIsAccentUnderlined_MarkersMuted()
    {
        var set = Build("see [docs](http://x) here");          // '['=4 "docs"=[5,9) ']'=9
        MarkDecoration txt = At(set, 5, 9);
        Assert.Equal(T.Primary, txt.Foreground);
        Assert.NotNull(txt.Underline);
        Assert.Equal(T.TextMuted, At(set, 4, 5).Foreground);   // "[" は淡色
    }

    private sealed class StubHl : Luxel.Document.ISyntaxHighlighter
    {
        public bool Supports(string lang) => lang == "csharp";
        public Luxel.Document.SyntaxToken[] Tokenize(string lang, string code)
        {
            int i = code.IndexOf("KW", System.StringComparison.Ordinal);
            return i >= 0 ? [new Luxel.Document.SyntaxToken(i, 2, Luxel.Document.TokenKind.Keyword)] : [];
        }
    }

    [Fact]
    public void CodeFence_SyntaxHighlight_EmitsForegroundTokensViaDecoration()
    {
        // "```csharp\nKW x\n```": ```csharp=[0,9) \n=9 "KW x"=[10,14) → KW=[10,12)。装飾 (widget でない) で色付け
        var set = MarkdownDecorations.Build("```csharp\nKW x\n```", T, highlighter: new StubHl());
        Assert.Contains(set.OfKind<MarkDecoration>(),
            m => m.From == 10 && m.To == 12 && m.Foreground == CodeDecorations.TokenColor(T, Luxel.Document.TokenKind.Keyword));
    }

    [Fact]
    public void EmbedFence_EmitsAutoHeightBlockWidget()
    {
        // "intro\n```embed counter\n```\nend": ```embed = [6,22), ``` = [23,26)
        var set = Build("intro\n```embed counter\n```\nend");
        BlockWidgetDecoration bw = set.OfKind<BlockWidgetDecoration>().Single();
        Assert.Equal("counter", ((EmbedRef)bw.Key).Key);
        Assert.True(bw.Height <= 0);          // 自動高さ
        Assert.Equal((6, 26), (bw.From, bw.To));
    }

    [Fact]
    public void EmbedKinds_RecognizesDirectKindFence()
    {
        // ```luxel-ui\n0\n``` を embedKinds で埋め込み扱いに (DocString 橋)
        var set = MarkdownDecorations.Build("```luxel-ui\n0\n```", T, embedKinds: new[] { "luxel-ui" });
        EmbedRef r = (EmbedRef)set.OfKind<BlockWidgetDecoration>().Single().Key;
        Assert.Equal("luxel-ui", r.Key);
        Assert.Equal("0", r.Body);
    }

    [Fact]
    public void EmbedKinds_Null_DirectKindIsCodeFence_NotEmbed()
    {
        // embedKinds を渡さなければ ```luxel-ui は通常のコードフェンス (無回帰)
        var set = MarkdownDecorations.Build("```luxel-ui\n0\n```", T);
        Assert.Empty(set.OfKind<BlockWidgetDecoration>());
    }

    [Fact]
    public void EmbedFence_CapturesBody()
    {
        // 本文を持つ埋め込み (mermaid/数式の図・式ソースの土台)
        EmbedRef r = (EmbedRef)Build("```embed mermaid\nA --> B\nC --> D\n```")
            .OfKind<BlockWidgetDecoration>().Single().Key;
        Assert.Equal("mermaid", r.Key);
        Assert.Equal("A --> B\nC --> D", r.Body);
    }

    [Fact]
    public void ListMarkers_AreMuted()
    {
        Assert.Equal(T.TextMuted, At(Build("- item"), 0, 2).Foreground);        // 箇条書き "- "
        Assert.Equal(T.TextMuted, At(Build("1. item"), 0, 3).Foreground);       // 番号付き "1. "
    }
}
