using Luxel.Editor;

namespace Luxel.Tests;

/// <summary>エディタ新スタック S4 (ToDo 22) — EditCommands の純関数コマンドを canvas 不要で検証。
/// タイプ/削除/移動/選択、マルチカーソル、サロゲートペア境界。</summary>
public class EditorCommandTests
{
    private static EditorState S(string text, int caret) => EditorState.Create(text, EditorSelection.Cursor(caret));

    [Fact]
    public void InsertText_AtCaret()
    {
        Transaction t = EditCommands.InsertText(S("helo", 3), "l");
        Assert.Equal("hello", t.State.Doc.Text);
        Assert.Equal(4, t.State.Selection.Main.Head);
    }

    [Fact]
    public void InsertText_ReplacesSelection()
    {
        var s = EditorState.Create("hello", EditorSelection.Single(0, 5));
        Transaction t = EditCommands.InsertText(s, "bye");
        Assert.Equal("bye", t.State.Doc.Text);
        Assert.Equal(3, t.State.Selection.Main.Head);
    }

    [Fact]
    public void InsertText_MultiCursor()
    {
        var s = EditorState.Create("a.b.c", EditorSelection.Of([SelectionRange.Cursor(1), SelectionRange.Cursor(3)]));
        Transaction t = EditCommands.InsertText(s, "!");
        Assert.Equal("a!.b!.c", t.State.Doc.Text);
        Assert.Equal(2, t.State.Selection.Ranges.Count);
        Assert.Equal(2, t.State.Selection.Ranges[0].Head);   // "a!|"
        Assert.Equal(5, t.State.Selection.Ranges[1].Head);   // "...b!|"
    }

    [Fact]
    public void DeleteBackward_Char()
    {
        Transaction t = EditCommands.DeleteBackward(S("hello", 5));
        Assert.Equal("hell", t.State.Doc.Text);
        Assert.Equal(4, t.State.Selection.Main.Head);
    }

    [Fact]
    public void DeleteBackward_AtStart_NoOp()
    {
        Transaction t = EditCommands.DeleteBackward(S("hi", 0));
        Assert.False(t.DocChanged);
        Assert.Equal("hi", t.State.Doc.Text);
    }

    [Fact]
    public void DeleteForward_Char()
    {
        Transaction t = EditCommands.DeleteForward(S("hello", 0));
        Assert.Equal("ello", t.State.Doc.Text);
        Assert.Equal(0, t.State.Selection.Main.Head);
    }

    [Fact]
    public void DeleteBackward_Selection()
    {
        var s = EditorState.Create("hello", EditorSelection.Single(1, 4));
        Transaction t = EditCommands.DeleteBackward(s);
        Assert.Equal("ho", t.State.Doc.Text);
        Assert.Equal(1, t.State.Selection.Main.Head);
    }

    [Fact]
    public void DeleteBackward_SurrogatePair()
    {
        // 𠮷 (U+20BB7, サロゲートペア 2 char) を 1 回の Backspace で消す
        var s = EditorState.Create("a𠮷", EditorSelection.Cursor(3));
        Transaction t = EditCommands.DeleteBackward(s);
        Assert.Equal("a", t.State.Doc.Text);
        Assert.Equal(1, t.State.Selection.Main.Head);
    }

    [Fact]
    public void InsertNewline_SplitsLine()
    {
        Transaction t = EditCommands.InsertNewline(S("ab", 1));
        Assert.Equal("a\nb", t.State.Doc.Text);
        Assert.Equal(2, t.State.Selection.Main.Head);
    }

    [Fact]
    public void MoveLeftRight_CollapsesOrExtends()
    {
        // 選択なし: 左右移動
        Assert.Equal(2, EditCommands.MoveLeft(S("hello", 3), select: false).State.Selection.Main.Head);
        Assert.Equal(4, EditCommands.MoveRight(S("hello", 3), select: false).State.Selection.Main.Head);
        // 選択あり・非 extend: From/To へ潰す
        var sel = EditorState.Create("hello", EditorSelection.Single(1, 4));
        Assert.Equal(1, EditCommands.MoveLeft(sel, false).State.Selection.Main.Head);
        Assert.Equal(4, EditCommands.MoveRight(sel, false).State.Selection.Main.Head);
        // extend: head を伸ばす
        var ext = EditCommands.MoveRight(S("hello", 2), select: true);
        Assert.Equal(2, ext.State.Selection.Main.Anchor);
        Assert.Equal(3, ext.State.Selection.Main.Head);
    }

    [Fact]
    public void HomeEnd_LineEdges()
    {
        var s = S("ab\ncdef", 5);   // 2 行目の途中
        Assert.Equal(3, EditCommands.MoveLineStart(s, false).State.Selection.Main.Head);   // 行頭 offset 3
        Assert.Equal(7, EditCommands.MoveLineEnd(s, false).State.Selection.Main.Head);      // 行末 offset 7
    }

    [Fact]
    public void SelectAll_WholeDoc()
    {
        Transaction t = EditCommands.SelectAll(S("hello\nworld", 0));
        Assert.Equal(0, t.State.Selection.Main.From);
        Assert.Equal(11, t.State.Selection.Main.To);
    }
}
