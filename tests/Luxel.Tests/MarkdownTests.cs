using Luxel.Document;
using Xunit;

namespace Luxel.Tests;

public class MarkdownTests
{
    private static Block B(RichDocument d, int i) => d.Blocks[i];

    [Fact]
    public void Parse_BlockKinds()
    {
        var d = Markdown.Parse("# Title\n## Sub\ntext\n> quoted\n- item\n1. first\n---\n```cs\nvar x = 1;\nvar y = 2;\n```");
        Assert.Equal(BlockKind.Heading, B(d, 0).Kind);
        Assert.Equal(1, B(d, 0).HeadingLevel);
        Assert.Equal("Title", B(d, 0).Text);
        Assert.Equal(2, B(d, 1).HeadingLevel);
        Assert.Equal(BlockKind.Paragraph, B(d, 2).Kind);
        Assert.Equal(BlockKind.Quote, B(d, 3).Kind);
        Assert.Equal("quoted", B(d, 3).Text);
        Assert.Equal(BlockKind.ListItem, B(d, 4).Kind);
        Assert.False(B(d, 4).Ordered);
        Assert.True(B(d, 5).Ordered);
        Assert.Equal(BlockKind.Divider, B(d, 6).Kind);
        Assert.Equal(BlockKind.CodeBlock, B(d, 7).Kind);
        Assert.Equal("cs", B(d, 7).CodeLang);
        Assert.Equal("var x = 1;\nvar y = 2;", B(d, 7).Text);   // フェンス内は複数行 1 ブロック
        Assert.Equal(8, d.Blocks.Count);
    }

    [Fact]
    public void Parse_BlankLines_ArePreservedAsEmptyBlocks()
    {
        // 改行 = 改行 (MD-NL): トップレベルの空行は空段落として保存され、表示 1 行に対応する
        var d = Markdown.Parse("a\n\nb\n\n\nc");
        Assert.Equal(6, d.Blocks.Count);   // a / 空 / b / 空 / 空 / c
        Assert.Equal("a", B(d, 0).Text);
        Assert.Equal(0, B(d, 1).Length);
        Assert.Equal("b", B(d, 2).Text);
        Assert.Equal(0, B(d, 3).Length);
        Assert.Equal(0, B(d, 4).Length);
        Assert.Equal("c", B(d, 5).Text);
        // round-trip 安定 (空行が消えも増えもしない)
        Assert.Equal("a\n\nb\n\n\nc", Markdown.Serialize(d));
        Assert.Equal(6, Markdown.Parse(Markdown.Serialize(d)).Blocks.Count);
    }

    [Fact]
    public void Parse_TrailingNewline_IsLineTerminator_NotBlankLine()
    {
        Assert.Single(Markdown.Parse("a\n").Blocks);       // 末尾の改行 1 つは行終端
        Assert.Equal(2, Markdown.Parse("a\n\n").Blocks.Count);   // 2 つなら空行 1 つ
        Assert.Single(Markdown.Parse("\n").Blocks);        // 空行だけの文書 = 空段落 1 つ
        Assert.Equal(0, Markdown.Parse("\n").Blocks[0].Length);
        // 先頭の空行も保存される
        var d = Markdown.Parse("\na");
        Assert.Equal(2, d.Blocks.Count);
        Assert.Equal(0, B(d, 0).Length);
        Assert.Equal("a", B(d, 1).Text);
    }

    [Fact]
    public void Parse_EmojiAndSmartyPants()
    {
        // :smile: → 😄 (EmojiInline = LiteralInline 派生)、"quoted" → “quoted”、--/--- → ダッシュ
        // (Markdig の SmartyPants は引用符/ダッシュ対応 — "..." の省略記号は対象外)
        var d = Markdown.Parse("hello :smile: \"quoted\" -- a --- b");
        string text = B(d, 0).Text;
        Assert.Contains("😄", text);
        Assert.Contains("“quoted”", text);
        Assert.Contains("–", text);    // --
        Assert.Contains("—", text);    // ---
        Assert.DoesNotContain("\"", text);
    }

    [Fact]
    public void SmartyPants_RoundTrip_ConvergesInOnePass()
    {
        // 変換後の文字がソースへ書き戻される正規化 — 2 回目以降は不変
        string once = Markdown.Serialize(Markdown.Parse("say \"hi\" -- ok :+1:"));
        string twice = Markdown.Serialize(Markdown.Parse(once));
        Assert.Equal(once, twice);
        Assert.Contains("“hi”", once);
        Assert.Contains("👍", once);
    }

    [Fact]
    public void Parse_NestedList()
    {
        var d = Markdown.Parse("- a\n  - b\n    - c\n  1. n");
        Assert.Equal(0, B(d, 0).Depth);
        Assert.Equal(1, B(d, 1).Depth);
        Assert.Equal(2, B(d, 2).Depth);
        Assert.Equal(1, B(d, 3).Depth);
        Assert.True(B(d, 3).Ordered);
    }

    [Fact]
    public void Parse_Inline()
    {
        var runs = B(Markdown.Parse("a **b *c*** and `x*y` [link](https://x)"), 0).Lines[0].Runs;
        Assert.Equal(new InlineRun("a "), runs[0]);
        Assert.Equal(new InlineRun("b ", new InlineStyle(Bold: true)), runs[1]);
        Assert.Equal(new InlineRun("c", new InlineStyle(Bold: true, Italic: true)), runs[2]);
        Assert.Equal(new InlineRun(" and ", InlineStyle.Plain), runs[3]);
        Assert.Equal(new InlineRun("x*y", new InlineStyle(Code: true)), runs[4]);   // code 内はリテラル
        Assert.Equal(new InlineRun(" ", InlineStyle.Plain), runs[5]);
        Assert.Equal(new InlineRun("link", new InlineStyle(Link: "https://x")), runs[6]);
    }

    [Fact]
    public void Parse_UnclosedMarkersStayLiteral()
    {
        Assert.Equal("a **b", B(Markdown.Parse("a **b"), 0).Text);
        Assert.Equal(InlineStyle.Plain, B(Markdown.Parse("a **b"), 0).Lines[0].Runs[0].Style);
        Assert.Equal("a `b", B(Markdown.Parse("a `b"), 0).Text);
    }

    [Fact]
    public void Parse_EscapesAreLiteral()
    {
        Block b = B(Markdown.Parse(@"a \*not italic\* b"), 0);
        Assert.Single(b.Lines[0].Runs);
        Assert.Equal("a *not italic* b", b.Text);
    }

    [Fact]
    public void RoundTrip_MdToDocToMd_IsStable()
    {
        const string md = "# Title\n\ntext **bold *both*** and `code`\n> quote\n- a\n  - b\n1. one\n2. two\n---\n```cs\nvar x;\n```";
        string once = Markdown.Serialize(Markdown.Parse(md));
        string twice = Markdown.Serialize(Markdown.Parse(once));
        Assert.Equal(once, twice);   // 正規形は不動点
        // 主要構造は保存される
        var d = Markdown.Parse(once);
        Assert.Equal(BlockKind.Heading, d.Blocks[0].Kind);
        Assert.Contains(d.Blocks, b => b.Kind == BlockKind.CodeBlock);
        Assert.Equal(2, d.Blocks.Count(b => b is { Kind: BlockKind.ListItem, Ordered: true }));
    }

    [Fact]
    public void RoundTrip_DocToMdToDoc_PreservesStructure()
    {
        var doc = RichDocument.FromBlocks(
        [
            new Block(BlockKind.Heading, "見出し") { HeadingLevel = 2 },
            MakeStyled(),
            new Block(BlockKind.ListItem, "項目") { Ordered = true },
            new Block(BlockKind.CodeBlock, "a\nb") { CodeLang = "txt" },
        ]);
        var back = Markdown.Parse(Markdown.Serialize(doc));
        Assert.Equal(doc.Blocks.Count, back.Blocks.Count);
        for (int i = 0; i < doc.Blocks.Count; i++)
        {
            Assert.Equal(doc.Blocks[i].Kind, back.Blocks[i].Kind);
            Assert.Equal(doc.Blocks[i].Text, back.Blocks[i].Text);
            Assert.Equal(doc.Blocks[i].Lines.Count, back.Blocks[i].Lines.Count);
            for (int li = 0; li < doc.Blocks[i].Lines.Count; li++)
            {
                Assert.Equal(doc.Blocks[i].Lines[li].Runs.Count, back.Blocks[i].Lines[li].Runs.Count);
                for (int r = 0; r < doc.Blocks[i].Lines[li].Runs.Count; r++)
                    Assert.Equal(doc.Blocks[i].Lines[li].Runs[r], back.Blocks[i].Lines[li].Runs[r]);
            }
        }

        static Block MakeStyled()
        {
            var b = new Block(BlockKind.Paragraph);
            b.Lines[0].Runs.Add(new InlineRun("plain "));
            b.Lines[0].Runs.Add(new InlineRun("bold", new InlineStyle(Bold: true)));
            b.Lines[0].Runs.Add(new InlineRun(" *literal asterisk* "));
            b.Lines[0].Runs.Add(new InlineRun("code", new InlineStyle(Code: true)));
            b.Lines[0].Runs.Add(new InlineRun("link", new InlineStyle(Link: "https://example.com")));
            return b;
        }
    }

    [Fact]
    public void Serialize_RenumbersOrderedLists()
    {
        var doc = RichDocument.FromBlocks(
        [
            new Block(BlockKind.ListItem, "a") { Ordered = true },
            new Block(BlockKind.ListItem, "b") { Ordered = true },
            new Block(BlockKind.Paragraph, "x"),
            new Block(BlockKind.ListItem, "c") { Ordered = true },
        ]);
        Assert.Equal("1. a\n2. b\nx\n1. c", Markdown.Serialize(doc));   // 段落で連番リセット
    }

    [Fact]
    public void DocPos_Ordering()
    {
        Assert.True(new DocPos(0, 5) < new DocPos(1, 0));
        Assert.True(new DocPos(1, 2) < new DocPos(1, 3));
        var r = new DocRange(new DocPos(2, 1), new DocPos(0, 4));
        Assert.Equal(new DocPos(0, 4), r.Min);
        Assert.Equal(new DocPos(2, 1), r.Max);
        Assert.False(r.IsCollapsed);
    }

    [Fact]
    public void EmptyDocument_HasOneParagraph()
    {
        var d = new RichDocument();
        Assert.Single(d.Blocks);
        Assert.Equal(BlockKind.Paragraph, d.Blocks[0].Kind);
        Assert.Equal("", Markdown.Serialize(d));
        Assert.Single(Markdown.Parse("").Blocks);
    }
}
