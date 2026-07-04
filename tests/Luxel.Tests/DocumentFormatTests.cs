using Luxel.Document;
using Xunit;

namespace Luxel.Tests;

/// <summary>EX-M0: IDocumentFormat — パーサーが文章をすべて管理する (Markdown は一実装)。</summary>
public class DocumentFormatTests
{
    // ---- MarkdownFormat: 委譲 + エディタから移設したオートフォーマットの知識 ----

    [Fact]
    public void MarkdownFormat_RoundTrip_MatchesStaticMarkdown()
    {
        IDocumentFormat f = MarkdownFormat.Default;
        const string md = "# title\ntext **bold**\n- item";
        Assert.Equal(Markdown.Serialize(Markdown.Parse(md)), f.Serialize(f.Parse(md)));
        Assert.True(f.SupportsHybrid);
        Assert.Equal(BlockKind.Heading, f.ParseLine("# x").Kind);
        Assert.Equal("- item", f.SerializeBlock(new Block(BlockKind.ListItem, "item")));
    }

    [Fact]
    public void MarkdownFormat_TryAutoFormat_ConvertsPrefixes()
    {
        IDocumentFormat f = MarkdownFormat.Default;
        var ed = new DocumentEditor();
        ed.SetText("");
        ed.Insert("- ");
        Assert.True(f.TryAutoFormat(ed, " "));
        Assert.Equal(BlockKind.ListItem, ed.Doc.Blocks[0].Kind);
        Assert.Equal("", ed.Doc.Blocks[0].Text);

        Assert.False(f.TryAutoFormat(ed, " "));   // 段落でない → 何もしない
        Assert.False(f.TryAutoFormat(ed, "x"));   // 空白以外 → 何もしない
    }

    [Fact]
    public void MarkdownFormat_TryBlockCommit_ConvertsFence()
    {
        IDocumentFormat f = MarkdownFormat.Default;
        var ed = new DocumentEditor();
        ed.SetText("```cs");
        ed.End(false);
        Assert.True(f.TryBlockCommit(ed));
        Assert.Equal(BlockKind.CodeBlock, ed.Doc.Blocks[0].Kind);
        Assert.Equal("cs", ed.Doc.Blocks[0].CodeLang);

        ed.SetText("plain");
        ed.End(false);
        Assert.False(f.TryBlockCommit(ed));
    }

    // ---- PlainTextFormat: 記法なしフォーマットの最小実証 (差し替え可能性のゲート) ----

    [Fact]
    public void PlainTextFormat_RoundTrip_KeepsMarkdownAsLiteral()
    {
        IDocumentFormat f = PlainTextFormat.Default;
        const string src = "# not a heading\n**not bold**\n- not a list";
        RichDocument d = f.Parse(src);
        Assert.Equal(3, d.Blocks.Count);
        Assert.All(d.Blocks, b => Assert.Equal(BlockKind.Paragraph, b.Kind));   // 記法は解釈しない
        Assert.Equal(src, f.Serialize(d));                                      // 完全往復

        Assert.True(f.SupportsHybrid);
        Assert.Equal("# not a heading", f.SerializeBlock(d.Blocks[0]));         // ソース = 表示

        var ed = new DocumentEditor(d);
        Assert.False(f.TryAutoFormat(ed, " "));
        Assert.False(f.TryBlockCommit(ed));
        Assert.Equal("not a heading\n**not bold", f.SerializeRange(d, new DocPos(0, 2), new DocPos(1, 10)));
    }
}
