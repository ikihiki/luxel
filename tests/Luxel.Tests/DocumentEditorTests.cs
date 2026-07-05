using Luxel.Document;
using Xunit;

namespace Luxel.Tests;

public class DocumentEditorTests
{
    private static DocumentEditor Ed(string text)
    {
        var ed = new DocumentEditor();
        ed.SetText(text);
        return ed;
    }

    [Fact]
    public void SetText_SplitsIntoParagraphs()
    {
        var ed = Ed("a\nb\nc");
        Assert.Equal(3, ed.Doc.Blocks.Count);
        Assert.Equal("a\nb\nc", ed.Doc.PlainText);
    }

    [Fact]
    public void Insert_WithNewlines_SplitsBlocks()
    {
        var ed = Ed("ab");
        ed.PlaceCaret(new DocPos(0, 1));
        ed.Insert("x\ny");
        Assert.Equal("ax\nyb", ed.Doc.PlainText);
        Assert.Equal(new DocPos(1, 1), ed.Caret);
    }

    [Fact]
    public void Enter_SplitsBlock_AndBackspaceMerges()
    {
        var ed = Ed("hello");
        ed.PlaceCaret(new DocPos(0, 2));
        int sv = ed.StructureVersion;
        ed.InsertNewline();
        Assert.Equal("he\nllo", ed.Doc.PlainText);
        Assert.Equal(new DocPos(1, 0), ed.Caret);
        Assert.True(ed.StructureVersion > sv);

        ed.Backspace();   // 行頭 Backspace = 結合
        Assert.Equal("hello", ed.Doc.PlainText);
        Assert.Equal(new DocPos(0, 2), ed.Caret);
    }

    [Fact]
    public void DeleteForward_AtBlockEnd_MergesNext()
    {
        var ed = Ed("ab\ncd");
        ed.PlaceCaret(new DocPos(0, 2));
        ed.DeleteForward();
        Assert.Equal("abcd", ed.Doc.PlainText);
        Assert.Equal(new DocPos(0, 2), ed.Caret);
    }

    [Fact]
    public void MoveLeftRight_CrossesBlocks()
    {
        var ed = Ed("ab\ncd");
        ed.PlaceCaret(new DocPos(1, 0));
        ed.MoveLeft(false);
        Assert.Equal(new DocPos(0, 2), ed.Caret);
        ed.MoveRight(false);
        Assert.Equal(new DocPos(1, 0), ed.Caret);
    }

    [Fact]
    public void MoveAndDelete_AreGraphemeBased()
    {
        var ed = Ed("éx");   // é (e + 結合アクセント) + x
        ed.End(false);
        ed.MoveLeft(false);
        Assert.Equal(new DocPos(0, 2), ed.Caret);   // x の前 = 合成列の直後
        ed.Backspace();                              // é 全体が消える
        Assert.Equal("x", ed.Doc.PlainText);
    }

    [Fact]
    public void SelectionAcrossBlocks_DeleteJoins()
    {
        var ed = Ed("abc\ndef\nghi");
        ed.Select(new DocPos(0, 1), new DocPos(2, 2));
        ed.Insert("X");
        Assert.Equal("aXi", ed.Doc.PlainText);
        Assert.Equal(new DocPos(0, 2), ed.Caret);
        Assert.Single(ed.Doc.Blocks);
    }

    [Fact]
    public void SelectAll_ThenType_ReplacesEverything()
    {
        var ed = Ed("abc\ndef");
        ed.SelectAll();
        ed.Insert("z");
        Assert.Equal("z", ed.Doc.PlainText);
    }

    [Fact]
    public void Insert_InheritsPrecedingRunStyle()
    {
        var b = new Block(BlockKind.Paragraph);
        Line l = b.Lines[0];
        l.Runs.Add(new InlineRun("ab", new InlineStyle(Bold: true)));
        l.Runs.Add(new InlineRun("cd"));
        var ed = new DocumentEditor(RichDocument.FromBlocks([b]));

        ed.PlaceCaret(new DocPos(0, 2));   // bold run 末尾
        ed.Insert("X");
        Assert.Equal(2, l.Runs.Count);
        Assert.Equal("abX", l.Runs[0].Text);
        Assert.True(l.Runs[0].Style.Bold);

        ed.PlaceCaret(new DocPos(0, 1));   // bold run 内
        ed.Insert("Y");
        Assert.Equal("aYbX", l.Runs[0].Text);
    }

    [Fact]
    public void DeleteRange_MergesAdjacentSameStyleRuns()
    {
        var l = new Line();
        l.Runs.Add(new InlineRun("ab"));
        l.Runs.Add(new InlineRun("XY", new InlineStyle(Bold: true)));
        l.Runs.Add(new InlineRun("cd"));
        DocumentEditor.DeleteRange(l, 2, 4);   // bold run 全体を削除
        Assert.Single(l.Runs);
        Assert.Equal("abcd", l.Runs[0].Text);
    }

    [Fact]
    public void Composition_DisplayAndCommit()
    {
        var ed = Ed("ab");
        ed.PlaceCaret(new DocPos(0, 1));
        ed.SetComposition("かな", targetStart: 0, targetLen: 2);
        Assert.Equal("aかなb", ed.DisplayTextOf(0));
        Assert.Equal(3, ed.DisplayCaretOffset);
        Assert.Equal((1, 2), ed.CompositionDisplayRange);
        Assert.Equal((1, 2), ed.TargetDisplayRange);

        ed.CommitComposition("仮名");
        Assert.Equal("a仮名b", ed.Doc.PlainText);
        Assert.Equal("", ed.Composition);
        Assert.Equal(new DocPos(0, 3), ed.Caret);
    }

    [Fact]
    public void Composition_ReplacesSelection()
    {
        var ed = Ed("abcd");
        ed.Select(new DocPos(0, 1), new DocPos(0, 3));
        ed.SetComposition("x");
        Assert.Equal("axd", ed.DisplayTextOf(0));
        ed.CommitComposition("x");
        Assert.Equal("axd", ed.Doc.PlainText);
    }

    [Fact]
    public void BlockLocalTsfSurface_ReplaceAndSelect()
    {
        var ed = Ed("abc\ndef");
        ed.PlaceCaret(new DocPos(1, 1));
        Assert.Equal("def", ed.CurrentBlockText);
        ed.SelectInBlock(0, 2);
        Assert.Equal((0, 2), ed.SelectionInBlock);
        ed.ReplaceInBlock(0, 2, "XY");
        Assert.Equal("abc\nXYf", ed.Doc.PlainText);
        Assert.Equal(new DocPos(1, 2), ed.Caret);
    }

    [Fact]
    public void HomeEnd_AndClamp()
    {
        var ed = Ed("abc");
        ed.End(false);
        Assert.Equal(new DocPos(0, 3), ed.Caret);
        ed.Home(false);
        Assert.Equal(new DocPos(0, 0), ed.Caret);
        ed.PlaceCaret(new DocPos(99, 99));   // クランプ
        Assert.Equal(new DocPos(0, 3), ed.Caret);
    }
}
