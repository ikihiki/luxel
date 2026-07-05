using Luxel.Document;
using Xunit;

namespace Luxel.Tests;

/// <summary>DW5: コールアウト (GitHub alert 記法) / CJK 強調 / デッドリンク検証。</summary>
public class DocsWritingTests
{
    [Fact]
    public void Callout_ParsesKind_AndRoundTrips()
    {
        const string md = "> [!NOTE]\n> 補足です";
        RichDocument doc = Markdown.Parse(md);
        Block callout = Assert.Single(doc.Blocks);   // マーカー行 + 本文行は 1 ブロック (装飾の単位)
        Assert.True(callout.CalloutMarker);
        Assert.Equal("NOTE", callout.Callout);
        Assert.Equal(2, callout.Lines.Count);
        Assert.Equal("NOTE", callout.Lines[0].Text);      // ラベル行 = Lines[0]
        Assert.Equal("補足です", callout.Lines[1].Text);

        // round-trip: マーカーが記法へ戻り、再パースで同じ構造になる
        string outMd = Markdown.Serialize(doc);
        Assert.Contains("> [!NOTE]", outMd);
        RichDocument again = Markdown.Parse(outMd);
        Assert.Equal("NOTE", Assert.Single(again.Blocks).Callout);
    }

    [Fact]
    public void Callout_Kinds_Warning()
    {
        RichDocument doc = Markdown.Parse("> [!WARNING]\n> 注意");
        Assert.Equal("WARNING", doc.Blocks.First(b => b.CalloutMarker).Callout);
    }

    [Fact]
    public void Quote_ConsecutiveLines_GroupIntoOneBlock()
    {
        // 連続する引用行は 1 ブロック (バー装飾が 1 本に繋がる単位)
        RichDocument doc = Markdown.Parse("> a\n> b\n\n> c");
        Assert.Equal(3, doc.Blocks.Count);   // quote(a,b) / 空段落 / quote(c)
        Assert.Equal(2, doc.Blocks[0].Lines.Count);
        Assert.Equal(1, doc.Blocks[0].QuoteDepth);
        Assert.Equal(BlockKind.Quote, doc.Blocks[2].Kind);
        Assert.Equal("> a\n> b\n\n> c", Markdown.Serialize(doc));
    }

    // ---- 引用の階層 (QuoteDepth — 引用は見出し/リスト/コードに重なり、入れ子にもなる) ----

    [Fact]
    public void QuotedHeading_KeepsQuote_AndRoundTrips()
    {
        RichDocument doc = Markdown.Parse("> # Title");
        Block b = Assert.Single(doc.Blocks);
        Assert.Equal(BlockKind.Heading, b.Kind);
        Assert.Equal(1, b.QuoteDepth);
        Assert.Equal("Title", b.Text);
        Assert.Equal("> # Title", Markdown.Serialize(doc));
    }

    [Fact]
    public void QuotedList_KeepsQuote_AndRoundTrips()
    {
        RichDocument doc = Markdown.Parse("> - a\n> - b");
        Assert.Equal(2, doc.Blocks.Count);
        Assert.All(doc.Blocks, b => Assert.Equal(BlockKind.ListItem, b.Kind));
        Assert.All(doc.Blocks, b => Assert.Equal(1, b.QuoteDepth));
        Assert.Equal("> - a\n> - b", Markdown.Serialize(doc));
    }

    [Fact]
    public void QuotedCode_KeepsQuote_AndRoundTrips()
    {
        const string md = "> ```cs\n> var x;\n> var y;\n> ```";
        RichDocument doc = Markdown.Parse(md);
        Block b = Assert.Single(doc.Blocks);
        Assert.Equal(BlockKind.CodeBlock, b.Kind);
        Assert.Equal(1, b.QuoteDepth);
        Assert.Equal(2, b.Lines.Count);
        Assert.Equal(md, Markdown.Serialize(doc));
    }

    [Fact]
    public void NestedQuote_KeepsDepth_AndRoundTrips()
    {
        RichDocument doc = Markdown.Parse("> outer\n> > inner");
        Assert.Equal(2, doc.Blocks.Count);   // 深さが違うので別ブロック
        Assert.Equal(1, doc.Blocks[0].QuoteDepth);
        Assert.Equal(2, doc.Blocks[1].QuoteDepth);
        Assert.Equal("> outer\n> > inner", Markdown.Serialize(doc));
    }

    [Fact]
    public void QuotedStructures_RoundTrip_IsFixpoint()
    {
        const string md = "> # H\n> text\n> - item\n> > deep";
        string once = Markdown.Serialize(Markdown.Parse(md));
        Assert.Equal(once, Markdown.Serialize(Markdown.Parse(once)));   // 正規形は不動点
    }

    [Fact]
    public void Backspace_ReleasesOneLevelAtATime()
    {
        // 引用内の見出し: 見出し{qd1} → 引用テキスト{qd1} → 段落 と一段ずつ外れる
        var ed = new DocumentEditor(Markdown.Parse("> # T"));
        ed.PlaceCaret(new DocPos(0, 0));
        ed.Backspace();
        Assert.Equal(BlockKind.Quote, ed.Doc.Blocks[0].Kind);
        Assert.Equal(1, ed.Doc.Blocks[0].QuoteDepth);
        ed.Backspace();
        Assert.Equal(BlockKind.Paragraph, ed.Doc.Blocks[0].Kind);
        Assert.Equal(0, ed.Doc.Blocks[0].QuoteDepth);

        // 入れ子引用: qd2 → qd1 → 段落
        var ed2 = new DocumentEditor(Markdown.Parse("> > deep"));
        ed2.PlaceCaret(new DocPos(0, 0));
        ed2.Backspace();
        Assert.Equal(1, ed2.Doc.Blocks[0].QuoteDepth);
        Assert.Equal(BlockKind.Quote, ed2.Doc.Blocks[0].Kind);
    }

    [Fact]
    public void CjkEmphasis_BoldInsideJapanese()
    {
        // CommonMark 素の規則では「日本語**太字**が効かない」— UseCjkFriendlyEmphasis で効く
        RichDocument doc = Markdown.Parse("日本語**太字**です");
        Assert.Contains(doc.Blocks[0].Lines[0].Runs, r => r.Style.Bold && r.Text == "太字");
    }

    // ---- LinkCheck ----

    private static RichDocument Doc(string md) => Markdown.Parse(md);

    [Fact]
    public void LinkCheck_ValidAnchor_And_BrokenAnchor()
    {
        RichDocument doc = Doc("# T\n\n## 使い方\n\n[ok](#使い方) [ng](#存在しない)");
        List<string> broken = LinkCheck.FindBroken(doc.Blocks);
        Assert.Equal(["#存在しない"], broken);
    }

    [Fact]
    public void LinkCheck_StoryLinks_CheckedAgainstResolver()
    {
        RichDocument doc = Doc("[a](story:Docs/Button) [b](story:Nope/Nope)");
        List<string> broken = LinkCheck.FindBroken(doc.Blocks, p => p == "Docs/Button");
        Assert.Equal(["story:Nope/Nope"], broken);
    }

    [Fact]
    public void LinkCheck_ExternalLinks_Ignored()
    {
        RichDocument doc = Doc("[x](https://example.com)");
        Assert.Empty(LinkCheck.FindBroken(doc.Blocks, _ => false));
    }
}
