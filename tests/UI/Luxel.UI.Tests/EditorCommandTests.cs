using Luxel.Document;

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

    // ---- 行操作 ----

    [Fact]
    public void MoveLineDown_SwapsWithNext()
    {
        var t = EditCommands.MoveLineDown(S("a\nb\nc", 0));   // 行 0 "a" を下へ
        Assert.Equal("b\na\nc", t.State.Doc.Text);
        Assert.Equal(2, t.State.Selection.Main.Head);        // キャレットは "a" 行に追従
    }

    [Fact]
    public void MoveLineUp_SwapsWithPrev()
    {
        var t = EditCommands.MoveLineUp(S("a\nb\nc", 4));     // 行 2 "c"... offset 4 は 3 行目
        Assert.Equal("a\nc\nb", t.State.Doc.Text);
    }

    [Fact]
    public void MoveLineUp_AtTop_NoOp()
    {
        Assert.False(EditCommands.MoveLineUp(S("a\nb", 0)).DocChanged);
    }

    [Fact]
    public void DuplicateLine_CopiesBelow()
    {
        var t = EditCommands.DuplicateLine(S("ab\ncd", 1));   // 行 0 "ab"
        Assert.Equal("ab\nab\ncd", t.State.Doc.Text);
        Assert.Equal(4, t.State.Selection.Main.Head);        // 複製行の同じ桁 (1) へ: offset 3+1
    }

    [Fact]
    public void ToggleComment_AddsThenRemoves()
    {
        var on = EditCommands.ToggleLineComment(S("  foo", 3));
        Assert.Equal("  // foo", on.State.Doc.Text);         // インデント直後に "// "
        var off = EditCommands.ToggleLineComment(on.State);
        Assert.Equal("  foo", off.State.Doc.Text);           // 戻る
    }

    [Fact]
    public void ToggleComment_MultiLine()
    {
        var s = EditorState.Create("a\nb\nc", EditorSelection.Single(0, 5));   // 全行選択
        var on = EditCommands.ToggleLineComment(s);
        Assert.Equal("// a\n// b\n// c", on.State.Doc.Text);
    }

    // ---- 検索 ----

    [Fact]
    public void Search_FindAll()
    {
        var m = TextSearch.FindAll("foo bar foo", "foo");
        Assert.Equal(2, m.Count);
        Assert.Equal((0, 3), m[0]);
        Assert.Equal((8, 11), m[1]);
    }

    [Fact]
    public void Search_IgnoreCaseAndEmpty()
    {
        Assert.Equal(2, TextSearch.FindAll("Foo foo", "foo", ignoreCase: true).Count);
        Assert.Empty(TextSearch.FindAll("abc", ""));
    }

    // ---- マルチカーソル ----

    [Fact]
    public void SelectNextOccurrence_FirstSelectsWord()
    {
        var s = S("foo bar foo", 1);   // "foo" 内
        Transaction t = EditCommands.SelectNextOccurrence(s);
        Assert.Single(t.State.Selection.Ranges);
        Assert.Equal(0, t.State.Selection.Main.From);
        Assert.Equal(3, t.State.Selection.Main.To);   // "foo" を選択
    }

    [Fact]
    public void SelectNextOccurrence_AddsNextMatch()
    {
        var s = EditorState.Create("foo bar foo", EditorSelection.Single(0, 3));   // "foo" 選択済み
        Transaction t = EditCommands.SelectNextOccurrence(s);
        Assert.Equal(2, t.State.Selection.Ranges.Count);       // 2 つ目の "foo" を追加
        Assert.Equal(8, t.State.Selection.Main.From);
        Assert.Equal(11, t.State.Selection.Main.To);
    }

    [Fact]
    public void SelectNextOccurrence_ThenTypeReplacesAll_OneUndo()
    {
        var s = EditorState.Create("foo bar foo", EditorSelection.Single(0, 3));
        var t1 = EditCommands.SelectNextOccurrence(s);         // 2 箇所選択
        var t2 = EditCommands.InsertText(t1.State, "X");       // 両方置換 (1 transaction)
        Assert.Equal("X bar X", t2.State.Doc.Text);
        var h = new History();
        h.Record(t2);
        Assert.Equal("foo bar foo", h.Undo(t2.State).Doc.Text);  // 1 undo で両方戻る
    }

    [Fact]
    public void SelectNextOccurrence_WrapsWhenAllSelected()
    {
        // 2 箇所とも選択済みで再実行 → 追加なし (全件選択済み)
        var s = EditorState.Create("foo foo",
            EditorSelection.Of([new SelectionRange(0, 3), new SelectionRange(4, 7)], 1));
        Transaction t = EditCommands.SelectNextOccurrence(s);
        Assert.Equal(2, t.State.Selection.Ranges.Count);
    }

    [Fact]
    public void ClearSecondaryCursors_KeepsMain()
    {
        var s = EditorState.Create("a b c",
            EditorSelection.Of([SelectionRange.Cursor(0), SelectionRange.Cursor(2), SelectionRange.Cursor(4)], 1));
        Transaction t = EditCommands.ClearSecondaryCursors(s);
        Assert.Single(t.State.Selection.Ranges);
        Assert.Equal(2, t.State.Selection.Main.Head);   // 主のみ残る
    }
}
