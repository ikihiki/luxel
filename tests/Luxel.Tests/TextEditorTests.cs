using Luxel.UI;
using Luxel.Controls;
using Xunit;

namespace Luxel.Tests;

public class TextEditorTests
{
    [Fact]
    public void Insert_AndCaretAdvances()
    {
        var e = new TextEditor();
        e.Insert("ab"); e.Insert("c");
        Assert.Equal("abc", e.Text);
        Assert.Equal(3, e.Caret);
    }

    [Fact]
    public void Backspace_And_Delete()
    {
        var e = new TextEditor(); e.SetText("abcd"); e.End(false);
        e.Backspace();
        Assert.Equal("abc", e.Text);
        e.Home(false); e.DeleteForward();
        Assert.Equal("bc", e.Text);
    }

    [Fact]
    public void Selection_Replace()
    {
        var e = new TextEditor(); e.SetText("hello");
        e.Home(false); e.MoveRight(true); e.MoveRight(true);   // select "he"
        Assert.True(e.HasSelection);
        e.Insert("HE");
        Assert.Equal("HEllo", e.Text);
    }

    [Fact]
    public void SelectAll_Delete()
    {
        var e = new TextEditor(); e.SetText("abc"); e.SelectAll(); e.Backspace();
        Assert.Equal("", e.Text);
    }

    [Fact]
    public void Ime_Composition_PreeditThenCommit()
    {
        var e = new TextEditor(); e.SetText("x"); e.End(false);
        e.SetComposition("にほん");
        Assert.Equal("xにほん", e.Display);       // 表示には preedit が入る
        Assert.Equal("x", e.Text);               // 確定前は本文不変
        e.CommitComposition("日本");
        Assert.Equal("x日本", e.Text);
        Assert.Equal("", e.Composition);
    }

    [Fact]
    public void Composition_DisplayAndTargetRanges()
    {
        var e = new TextEditor(); e.SetText("ab"); e.End(false);   // caret=2
        e.SetComposition("にほんご", targetStart: 1, targetLen: 2);
        Assert.Equal("abにほんご", e.Display);
        Assert.Equal((2, 4), e.CompositionDisplayRange);           // caret(2) から 4 文字
        Assert.Equal((3, 2), e.TargetDisplayRange);                // caret+1 から 2 文字
        e.CommitComposition("日本語");
        Assert.Equal("ab日本語", e.Text);
        Assert.Equal(0, e.CompositionDisplayRange.len);            // 確定で合成解除 (長さ 0)
    }

    [Fact]
    public void Field_ValidatesReactively()
    {
        var f = new Field<string>("", v => v.Length < 3 ? "short" : null);
        Assert.Equal("short", f.Error.Value);
        f.Value.Value = "abcd";
        Assert.Null(f.Error.Value);
    }
}
