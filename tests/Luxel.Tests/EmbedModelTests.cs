using Luxel.Document;
using Xunit;

namespace Luxel.Tests;

/// <summary>EX-M1: Embed モデル (payload / フェンスリゾルバ / テーブル / 画像) と原子ブロック意味論。</summary>
public class EmbedModelTests
{
    private sealed class StrudelResolver : IFenceResolver
    {
        public IBlockPayload? Resolve(string info, string body)
            => info.StartsWith("strudel") ? new FencePayload(info, body) : null;
    }

    // ---- フェンスリゾルバ (パーサーが判断) ----

    [Fact]
    public void FenceResolver_PromotesToEmbed_AndRoundTrips()
    {
        var fmt = new MarkdownFormat();
        fmt.FenceResolvers.Add(new StrudelResolver());
        const string md = "before\n```strudel\ns(\"bd sd\").fast(2)\n```\nafter";

        RichDocument d = fmt.Parse(md);
        Assert.Equal(3, d.Blocks.Count);
        Assert.Equal(BlockKind.Embed, d.Blocks[1].Kind);
        var p = Assert.IsType<FencePayload>(d.Blocks[1].Payload);
        Assert.Equal("strudel", p.TypeId);
        Assert.Equal("s(\"bd sd\").fast(2)", p.Body);

        Assert.Equal(md, fmt.Serialize(d));   // フェンス原文で往復
    }

    [Fact]
    public void FenceWithoutResolver_StaysCodeBlock()
    {
        var fmt = new MarkdownFormat();   // リゾルバなし
        RichDocument d = fmt.Parse("```strudel\ncode\n```");
        Assert.Equal(BlockKind.CodeBlock, d.Blocks[0].Kind);
        Assert.Equal("strudel", d.Blocks[0].CodeLang);
        Assert.Equal("```strudel\ncode\n```", fmt.Serialize(d));   // 保全
    }

    [Fact]
    public void FencePayload_TypeId_IsFirstWordOfInfo()
    {
        Assert.Equal("csharp", new FencePayload("csharp live", "x").TypeId);
    }

    // ---- 画像 ----

    [Fact]
    public void Image_ParagraphWithSoleImage_BecomesEmbed_AndRoundTrips()
    {
        RichDocument d = Markdown.Parse("text\n![ロゴ](assets/logo.png)\ntext2");
        Assert.Equal(BlockKind.Embed, d.Blocks[1].Kind);
        var img = Assert.IsType<ImagePayload>(d.Blocks[1].Payload);
        Assert.Equal("assets/logo.png", img.Src);
        Assert.Equal("ロゴ", img.Alt);
        Assert.Equal("text\n![ロゴ](assets/logo.png)\ntext2", Markdown.Serialize(d));
    }

    [Fact]
    public void Image_InlineWithText_StaysParagraph()
    {
        RichDocument d = Markdown.Parse("see ![icon](i.png) here");
        Assert.Equal(BlockKind.Paragraph, d.Blocks[0].Kind);   // 文中画像はブロック化しない (v1)
    }

    // ---- テーブル ----

    [Fact]
    public void Table_PipeTable_ParsesAndRoundTrips()
    {
        const string md = "| name | value |\n| :--- | ---: |\n| a | 1 |\n| b\\|c | **2** |";
        RichDocument d = Markdown.Parse(md);
        Assert.Single(d.Blocks);
        var t = Assert.IsType<TablePayload>(d.Blocks[0].Payload);
        Assert.Equal(2, t.Columns);
        Assert.Equal(3, t.Rows.Count);
        Assert.Equal("name", t.Cell(0, 0));
        Assert.Equal(TableAlign.Left, t.Aligns[0]);
        Assert.Equal(TableAlign.Right, t.Aligns[1]);
        Assert.Equal("b|c", t.Cell(2, 0));
        Assert.Equal("**2**", t.Cell(2, 1));   // セル内インラインはリテラル保持

        Assert.Equal(md, Markdown.Serialize(d));   // 正規形は不動点
    }

    [Fact]
    public void Table_BetweenParagraphs_RoundTripsWithBlankLines()
    {
        // pipe table は前後に空行がないと段落の継続に食われる。空行は空段落として保存され
        // (改行 = 改行)、シリアライザは隣に空段落があれば強制空行を重ねない → round-trip 安定
        RichDocument d = Markdown.Parse("before\n\n| a |\n| --- |\n| 1 |\n\nafter");
        Assert.Equal(5, d.Blocks.Count);   // before / 空行 / table / 空行 / after
        Assert.Equal(BlockKind.Embed, d.Blocks[2].Kind);
        string md = Markdown.Serialize(d);
        Assert.Equal("before\n\n| a |\n| --- |\n| 1 |\n\nafter", md);
        RichDocument back = Markdown.Parse(md);
        Assert.Equal(5, back.Blocks.Count);
        Assert.IsType<TablePayload>(back.Blocks[2].Payload);   // 再パースでも表のまま
    }

    // ---- 原子ブロック意味論 (DocumentEditor) ----

    private static DocumentEditor WithEmbed()
    {
        var ed = new DocumentEditor(RichDocument.FromBlocks(
        [
            new Block(BlockKind.Paragraph, "above"),
            new Block(BlockKind.Embed) { Payload = new FencePayload("x", "body") },
            new Block(BlockKind.Paragraph, "below"),
        ]));
        return ed;
    }

    [Fact]
    public void Backspace_OnEmbed_DeletesWholeBlock()
    {
        var ed = WithEmbed();
        ed.PlaceCaret(new DocPos(1, 0));
        ed.Backspace();
        Assert.Equal(2, ed.Doc.Blocks.Count);
        Assert.Equal("above\nbelow", ed.Doc.PlainText);
        Assert.Equal(new DocPos(0, 5), ed.Caret);
        ed.Undo();
        Assert.Equal(BlockKind.Embed, ed.Doc.Blocks[1].Kind);
    }

    [Fact]
    public void Backspace_AtHeadAfterEmbed_DeletesEmbedNotMerge()
    {
        var ed = WithEmbed();
        ed.PlaceCaret(new DocPos(2, 0));
        ed.Backspace();
        Assert.Equal(2, ed.Doc.Blocks.Count);
        Assert.Equal("above\nbelow", ed.Doc.PlainText);
    }

    [Fact]
    public void TypingAndEnter_OnEmbed_EscapeToParagraphAfter()
    {
        var ed = WithEmbed();
        ed.PlaceCaret(new DocPos(1, 0));
        ed.Insert("hi");
        Assert.Equal(BlockKind.Embed, ed.Doc.Blocks[1].Kind);   // Embed は不変
        Assert.Equal("hibelow", ed.Doc.Blocks[2].Text);         // 直後の段落に入る
        Assert.Equal(new DocPos(2, 2), ed.Caret);

        ed.PlaceCaret(new DocPos(1, 0));
        ed.InsertNewline();
        Assert.Equal(new DocPos(2, 0), ed.Caret);               // Enter も直後へ (分割しない)
        Assert.Equal(BlockKind.Embed, ed.Doc.Blocks[1].Kind);
    }

    [Fact]
    public void SelectionAcrossEmbed_DeletesIt()
    {
        var ed = WithEmbed();
        ed.Select(new DocPos(0, 2), new DocPos(2, 2));
        ed.Insert("X");
        Assert.Equal("abXlow", ed.Doc.PlainText);
        Assert.DoesNotContain(ed.Doc.Blocks, b => b.Kind == BlockKind.Embed);
    }

    [Fact]
    public void StyleAndBlockKind_SkipEmbed()
    {
        var ed = WithEmbed();
        ed.SelectAll();
        ed.ToggleBold();
        Assert.Equal(BlockKind.Embed, ed.Doc.Blocks[1].Kind);
        Assert.Empty(ed.Doc.Blocks[1].Lines[0].Runs);

        ed.SelectAll();
        ed.SetBlockKind(BlockKind.Quote);
        Assert.Equal(BlockKind.Embed, ed.Doc.Blocks.Single(b => b.Kind == BlockKind.Embed).Kind);
    }

    [Fact]
    public void InsertEmbed_AndReplacePayload_AreUndoable()
    {
        var ed = new DocumentEditor();
        ed.SetText("para");
        ed.End(false);
        ed.InsertEmbed(new FencePayload("chart", "v1"));
        Assert.Equal(BlockKind.Embed, ed.Doc.Blocks[1].Kind);
        Assert.Equal(new DocPos(2, 0), ed.Caret);

        ed.ReplacePayload(1, new FencePayload("chart", "v2"));
        Assert.Equal("v2", ((FencePayload)ed.Doc.Blocks[1].Payload!).Body);

        ed.Undo();
        Assert.Equal("v1", ((FencePayload)ed.Doc.Blocks[1].Payload!).Body);
        ed.Undo();
        Assert.DoesNotContain(ed.Doc.Blocks, b => b.Kind == BlockKind.Embed);
    }
}
