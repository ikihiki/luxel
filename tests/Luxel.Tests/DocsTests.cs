using Luxel.Controls;
using Luxel.Document;
using Luxel.UI;
using Xunit;

namespace Luxel.Tests;

/// <summary>MDX: DocString (補完文字列 markdown + UI hole) + Kit.Docs。</summary>
public class DocsTests
{
    [Fact]
    public void DocString_MixesMarkdown_TextHoles_And_WidgetHoles()
    {
        var count = new Signal<int>(3);
        Widget live = Kit.Button(_ => { }, "x");
        RichTextEditor doc = Kit.Docs($"""
            # Title

            count = {count}

            {live}

            tail
            """);

        // 表示専用 + 領域いっぱい
        Assert.True(doc.ReadOnly);
        Assert.Equal(Align.Stretch, doc.HAlign.Get());
        Assert.Equal(Align.Stretch, doc.VAlign.Get());

        // ブロック列: 見出し / テキスト補完済み段落 / UI embed / 末尾段落
        IReadOnlyList<Block> blocks = doc.Editor.Doc.Blocks;
        Assert.Equal(BlockKind.Heading, blocks[0].Kind);
        Assert.Equal("Title", blocks[0].Text);
        Block para = Assert.Single(blocks, b => b.Kind == BlockKind.Paragraph && b.Text.Contains("count"));
        Assert.Equal("count = 3", para.Text);   // Signal hole は構築時の値が焼き込まれる
        Block embed = Assert.Single(blocks, b => b.Kind == BlockKind.Embed);
        Assert.Equal(DocString.UiTypeId, embed.Payload!.TypeId);
        Assert.Contains(blocks, b => b.Kind == BlockKind.Paragraph && b.Text == "tail");
    }

    [Fact]
    public void Docs_UiFactory_ResolvesHoleWidget_InCanvasFrame()
    {
        Widget live = Kit.Button(_ => { }, "x");
        RichTextEditor doc = Kit.Docs($"""
            before

            {live}
            """);

        Block embed = Assert.Single(doc.Editor.Doc.Blocks, b => b.Kind == BlockKind.Embed);
        BlockWidgetFactory factory = doc.Widgets.Find(DocString.UiTypeId)!;
        Widget canvas = factory(new BlockWidgetContext
        {
            Payload = embed.Payload!,
            MaxWidth = 400,
            Theme = UiTheme.Current,
            Commit = _ => { },
            Invalidate = () => { },
        });
        Assert.True(ContainsWidget(canvas, live));   // hole の widget が枠の中にそのまま入る
    }

    [Fact]
    public void DocString_MultipleHoles_KeepOrder()
    {
        Widget a = Kit.Label("a"), b = Kit.Label("b");
        RichTextEditor doc = Kit.Docs($"""
            {a}

            mid

            {b}
            """);

        var embeds = doc.Editor.Doc.Blocks.Where(x => x.Kind == BlockKind.Embed).ToArray();
        Assert.Equal(2, embeds.Length);
        BlockWidgetFactory factory = doc.Widgets.Find(DocString.UiTypeId)!;
        Widget W(Block e) => factory(new BlockWidgetContext
        {
            Payload = e.Payload!, MaxWidth = 400, Theme = UiTheme.Current,
            Commit = _ => { }, Invalidate = () => { },
        });
        Assert.True(ContainsWidget(W(embeds[0]), a));
        Assert.True(ContainsWidget(W(embeds[1]), b));
    }

    [Fact]
    public void Slug_NormalizesHeadings()
    {
        Assert.Equal("使い方", RichTextEditor.Slug("使い方"));
        Assert.Equal("getting-started", RichTextEditor.Slug(" Getting Started "));
    }

    [Fact]
    public void DocsWithCtx_RoutesStoryLinks_ToNavigate()
    {
        string? navigated = null;
        var ctx = new StoryContext();
        ctx.SetNavigator(p => navigated = p);
        RichTextEditor doc = Kit.Docs(ctx, $"""
            # T

            [go](story:Button/Variants) と [ext](https://example.com)
            """);
        Assert.True(doc.OnLink.HasHandler);
        doc.OnLink.Invoke(doc, "story:Button/Variants");
        Assert.Equal("Button/Variants", navigated);
        doc.OnLink.Invoke(doc, "https://example.com");   // 未知スキームは Log のみ (落ちない)
        Assert.Contains(ctx.LogSnapshot(), e => e.Message.Contains("example.com"));
    }

    [Fact]
    public void DocsToc_InsertsAnchorList_AfterFirstHeading()
    {
        RichTextEditor doc = Kit.Docs($"""
            # Title

            intro

            ## Alpha

            ## Beta

            ### Gamma
            """, toc: true);
        IReadOnlyList<Block> blocks = doc.Editor.Doc.Blocks;
        Assert.Equal(BlockKind.Heading, blocks[0].Kind);

        // TOC = アンカーリンク付きリスト (H1 直後、H2/H3 の 3 件)
        Block[] toc = blocks.Where(b => b.Kind == BlockKind.ListItem
            && b.Runs.Any(r => r.Style.Link is string l && l.StartsWith('#'))).ToArray();
        Assert.Equal(3, toc.Length);
        Assert.Equal("Alpha", toc[0].Text);
        Assert.Contains(toc[0].Runs, r => r.Style.Link == "#alpha");
        Assert.Equal(1, toc[2].Depth);   // ### Gamma はネスト
        // TOC はイントロより前 (H1 直後)
        int tocIdx = blocks.ToList().IndexOf(toc[0]);
        int introIdx = blocks.ToList().FindIndex(b => b.Text == "intro");
        Assert.True(tocIdx < introIdx);
    }

    private static bool ContainsWidget(Widget root, Widget target)
    {
        if (ReferenceEquals(root, target)) return true;
        foreach (Widget c in root.DebugChildren())
            if (ContainsWidget(c, target)) return true;
        return false;
    }
}
