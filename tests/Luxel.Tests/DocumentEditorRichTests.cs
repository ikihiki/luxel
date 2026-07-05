using Luxel.Document;
using Xunit;

namespace Luxel.Tests;

/// <summary>ED-M3: スタイルトグル / ブロック型変換 / 型付き Enter・Backspace / undo・redo。</summary>
public class DocumentEditorRichTests
{
    private static DocumentEditor Ed(string text)
    {
        var ed = new DocumentEditor();
        ed.SetText(text);
        return ed;
    }

    // ---- スタイルトグル ----

    [Fact]
    public void ToggleBold_SplitsRuns_AndUntoggleMerges()
    {
        var ed = Ed("hello world");
        ed.Select(new DocPos(0, 2), new DocPos(0, 7));
        ed.ToggleBold();
        Line l = ed.Doc.Blocks[0].Lines[0];
        Assert.Equal(3, l.Runs.Count);
        Assert.Equal("he", l.Runs[0].Text);
        Assert.Equal("llo w", l.Runs[1].Text);
        Assert.True(l.Runs[1].Style.Bold);
        Assert.Equal("orld", l.Runs[2].Text);

        ed.ToggleBold();   // 選択は保持されている → 解除で 1 run に戻る
        Assert.Single(l.Runs);
        Assert.Equal("hello world", l.Runs[0].Text);
    }

    [Fact]
    public void ToggleBold_MixedSelection_AppliesToAll()
    {
        var ed = Ed("abcdef");
        ed.Select(new DocPos(0, 0), new DocPos(0, 3));
        ed.ToggleBold();   // abc = bold
        ed.Select(new DocPos(0, 0), new DocPos(0, 6));
        ed.ToggleBold();   // 混在 → 全体 bold
        Assert.True(ed.SelectionHasStyle(s => s.Bold));
        Assert.Single(ed.Doc.Blocks[0].Lines[0].Runs);
    }

    [Fact]
    public void ToggleStyle_AcrossBlocks_SkipsCodeBlocks()
    {
        var ed = Ed("aa\nbb");
        ed.PlaceCaret(new DocPos(1, 0));
        ed.SetBlockKind(BlockKind.CodeBlock);
        ed.Select(new DocPos(0, 0), new DocPos(1, 2));
        ed.ToggleItalic();
        Assert.True(ed.Doc.Blocks[0].Lines[0].Runs[0].Style.Italic);
        Assert.False(ed.Doc.Blocks[1].Lines[0].Runs[0].Style.Italic);   // コードブロックは対象外
    }

    // ---- ブロック型変換 ----

    [Fact]
    public void SetBlockKind_TogglesBackToParagraph()
    {
        var ed = Ed("title");
        ed.SetBlockKind(BlockKind.Heading, headingLevel: 2);
        Assert.Equal(BlockKind.Heading, ed.Doc.Blocks[0].Kind);
        Assert.Equal(2, ed.Doc.Blocks[0].HeadingLevel);
        ed.SetBlockKind(BlockKind.Heading, headingLevel: 2);   // 同型 → 段落へ
        Assert.Equal(BlockKind.Paragraph, ed.Doc.Blocks[0].Kind);
        ed.SetBlockKind(BlockKind.Heading, headingLevel: 2);
        ed.SetBlockKind(BlockKind.Heading, headingLevel: 1);   // レベル違い → 変換 (トグルでない)
        Assert.Equal(1, ed.Doc.Blocks[0].HeadingLevel);
    }

    [Fact]
    public void SetBlockKind_CodeBlock_MergesRange_AndSplitsBack()
    {
        var ed = Ed("a\nb\nc");
        ed.Select(new DocPos(0, 0), new DocPos(2, 1));
        ed.SetBlockKind(BlockKind.CodeBlock);
        Assert.Single(ed.Doc.Blocks);
        Assert.Equal(BlockKind.CodeBlock, ed.Doc.Blocks[0].Kind);
        Assert.Equal("a\nb\nc", ed.Doc.Blocks[0].Text);

        ed.SelectAll();
        ed.SetBlockKind(BlockKind.CodeBlock);   // 同型トグル → 段落へ分解
        Assert.Equal(3, ed.Doc.Blocks.Count);
        Assert.All(ed.Doc.Blocks, b => Assert.Equal(BlockKind.Paragraph, b.Kind));
        Assert.Equal("a\nb\nc", ed.Doc.PlainText);
    }

    // ---- 型付き Enter / Backspace ----

    [Fact]
    public void Enter_InListItem_ContinuesList_AndEmptyItemExits()
    {
        var ed = Ed("item");
        ed.SetBlockKind(BlockKind.ListItem, ordered: true);
        ed.End(false);
        ed.InsertNewline();
        Assert.Equal(BlockKind.ListItem, ed.Doc.Blocks[1].Kind);   // 次項目
        Assert.True(ed.Doc.Blocks[1].Ordered);

        ed.InsertNewline();   // 空項目で Enter → リスト解除 (分割しない)
        Assert.Equal(2, ed.Doc.Blocks.Count);
        Assert.Equal(BlockKind.Paragraph, ed.Doc.Blocks[1].Kind);
    }

    [Fact]
    public void Enter_InHeading_TailIsParagraph()
    {
        var ed = Ed("headline");
        ed.SetBlockKind(BlockKind.Heading);
        ed.PlaceCaret(new DocPos(0, 4));
        ed.InsertNewline();
        Assert.Equal(BlockKind.Heading, ed.Doc.Blocks[0].Kind);
        Assert.Equal("head", ed.Doc.Blocks[0].Text);
        Assert.Equal(BlockKind.Paragraph, ed.Doc.Blocks[1].Kind);
        Assert.Equal("line", ed.Doc.Blocks[1].Text);
    }

    [Fact]
    public void Enter_InCodeBlock_IsLiteral_AndTrailingEmptyLineExits()
    {
        var ed = Ed("code");
        ed.SetBlockKind(BlockKind.CodeBlock);
        ed.End(false);
        ed.InsertNewline();
        Assert.Single(ed.Doc.Blocks);
        Assert.Equal("code\n", ed.Doc.Blocks[0].Text);   // リテラル改行

        ed.InsertNewline();   // 末尾空行で Enter → コードを抜ける
        Assert.Equal(2, ed.Doc.Blocks.Count);
        Assert.Equal("code", ed.Doc.Blocks[0].Text);
        Assert.Equal(BlockKind.Paragraph, ed.Doc.Blocks[1].Kind);
        Assert.Equal(new DocPos(1, 0), ed.Caret);
    }

    [Fact]
    public void Backspace_AtHeadOfTypedBlock_ConvertsToParagraphFirst()
    {
        var ed = Ed("a\nquoted");
        ed.PlaceCaret(new DocPos(1, 0));
        ed.SetBlockKind(BlockKind.Quote);
        ed.PlaceCaret(new DocPos(1, 0));
        ed.Backspace();   // 1 回目 = 型解除
        Assert.Equal(2, ed.Doc.Blocks.Count);
        Assert.Equal(BlockKind.Paragraph, ed.Doc.Blocks[1].Kind);
        ed.Backspace();   // 2 回目 = 結合
        Assert.Single(ed.Doc.Blocks);
        Assert.Equal("aquoted", ed.Doc.PlainText);
    }

    [Fact]
    public void SplitBlock_PreservesRunStyles()
    {
        var ed = Ed("abcd");
        ed.Select(new DocPos(0, 0), new DocPos(0, 4));
        ed.ToggleBold();
        ed.PlaceCaret(new DocPos(0, 2));
        ed.InsertNewline();
        Assert.True(ed.Doc.Blocks[0].Lines[0].Runs[0].Style.Bold);
        Assert.True(ed.Doc.Blocks[1].Lines[0].Runs[0].Style.Bold);   // リッチ分割 (プレーン化しない)
        Assert.Equal("ab", ed.Doc.Blocks[0].Text);
        Assert.Equal("cd", ed.Doc.Blocks[1].Text);
    }

    // ---- undo / redo ----

    [Fact]
    public void Undo_Redo_RestoresTextAndCaret()
    {
        var ed = Ed("base");
        ed.End(false);
        ed.Insert("X");
        Assert.Equal("baseX", ed.Doc.PlainText);
        ed.Undo();
        Assert.Equal("base", ed.Doc.PlainText);
        Assert.Equal(new DocPos(0, 4), ed.Caret);
        ed.Redo();
        Assert.Equal("baseX", ed.Doc.PlainText);
    }

    [Fact]
    public void Undo_CoalescesConsecutiveTyping()
    {
        var ed = Ed("");
        ed.Insert("a");
        ed.Insert("b");
        ed.Insert("c");
        Assert.Equal("abc", ed.Doc.PlainText);
        ed.Undo();   // 連続タイプは 1 op
        Assert.Equal("", ed.Doc.PlainText);
        Assert.False(ed.CanUndo);
        ed.Redo();
        Assert.Equal("abc", ed.Doc.PlainText);
    }

    [Fact]
    public void Undo_CaretMoveBreaksCoalescing()
    {
        var ed = Ed("ab");
        ed.PlaceCaret(new DocPos(0, 1));
        ed.Insert("X");
        ed.PlaceCaret(new DocPos(0, 0));
        ed.PlaceCaret(new DocPos(0, 2));
        ed.Insert("Y");
        Assert.Equal("aXYb", ed.Doc.PlainText);
        ed.Undo();
        Assert.Equal("aXb", ed.Doc.PlainText);
        ed.Undo();
        Assert.Equal("ab", ed.Doc.PlainText);
    }

    [Fact]
    public void Undo_StructuralOps_RestoreBlocks()
    {
        var ed = Ed("one\ntwo");
        ed.PlaceCaret(new DocPos(0, 3));
        ed.InsertNewline();
        Assert.Equal(3, ed.Doc.Blocks.Count);
        ed.Undo();
        Assert.Equal(2, ed.Doc.Blocks.Count);
        Assert.Equal("one\ntwo", ed.Doc.PlainText);

        ed.SelectAll();
        ed.SetBlockKind(BlockKind.CodeBlock);
        Assert.Single(ed.Doc.Blocks);
        ed.Undo();
        Assert.Equal(2, ed.Doc.Blocks.Count);
        Assert.All(ed.Doc.Blocks, b => Assert.Equal(BlockKind.Paragraph, b.Kind));
    }

    [Fact]
    public void Undo_StyleToggle_RestoresRuns()
    {
        var ed = Ed("hello");
        ed.Select(new DocPos(0, 1), new DocPos(0, 4));
        ed.ToggleBold();
        Assert.Equal(3, ed.Doc.Blocks[0].Lines[0].Runs.Count);
        ed.Undo();
        Assert.Single(ed.Doc.Blocks[0].Lines[0].Runs);
        Assert.False(ed.Doc.Blocks[0].Lines[0].Runs[0].Style.Bold);
    }

    [Fact]
    public void NewEdit_ClearsRedo()
    {
        var ed = Ed("a");
        ed.End(false);
        ed.Insert("b");
        ed.Undo();
        Assert.True(ed.CanRedo);
        ed.Insert("c");
        Assert.False(ed.CanRedo);
        Assert.Equal("ac", ed.Doc.PlainText);
    }

    // ---- hybrid / オートフォーマット支援 ----

    [Fact]
    public void HybridRestore_IsNotJournaled_AndKeepsUndoConsistent()
    {
        var ed = Ed("# not yet heading");
        ed.End(false);
        ed.Insert("!");
        // hybrid 相当: ソース段落 → 整形ブロックへジャーナル外置換
        var parsed = Markdown.ParseLine(ed.Doc.Blocks[0].Text);
        ed.HybridRestoreLine(0, parsed);
        Assert.Equal(BlockKind.Heading, ed.Doc.Blocks[0].Kind);
        Assert.Equal("not yet heading!", ed.Doc.Blocks[0].Text);

        // Insert("!") だけが undo される (畳み込みは記録されない)。エントリはソース段落時代の
        // スナップショットなので、ブロックはソース形へ戻る (畳み込みは表示側 SyncHybrid の責務)。
        ed.Undo();
        Assert.Equal("# not yet heading", ed.Doc.Blocks[0].Text);
        Assert.Equal(BlockKind.Paragraph, ed.Doc.Blocks[0].Kind);
        Assert.False(ed.CanUndo);
    }

    [Fact]
    public void HybridSwapLine_MovesCaretWhenRequested()
    {
        var ed = Ed("item");
        ed.PlaceCaret(new DocPos(0, 2));
        ed.HybridSwapLine(0, new Block(BlockKind.Paragraph, "- item"), caretOffset: 4);
        Assert.Equal(new DocPos(0, 4), ed.Caret);
    }

    [Fact]
    public void HybridSwapLine_SplitsMultiLineQuote_AndRestoreMergesBack()
    {
        // 3 行の引用 (1 ブロック) の中間行をソース展開 → ブロックが 3 分割される
        var ed = new DocumentEditor(Markdown.Parse("> a\n> b\n> c"));
        Assert.Single(ed.Doc.Blocks);
        Assert.Equal(3, ed.Doc.Blocks[0].Lines.Count);

        ed.PlaceCaret(new DocPos(1, 0));
        ed.HybridSwapLine(1, new Block(BlockKind.Paragraph, "> b"), caretOffset: 2);
        Assert.Equal(3, ed.Doc.Blocks.Count);
        Assert.Equal(BlockKind.Paragraph, ed.Doc.Blocks[1].Kind);
        Assert.Equal("> b", ed.Doc.Blocks[1].Lines[0].Text);
        Assert.Equal(new DocPos(1, 2), ed.Caret);

        // 畳み込み → 再パース + 正規化マージで 1 ブロックへ戻る
        ed.HybridRestoreLine(1, Markdown.ParseLine("> b"));
        Assert.Single(ed.Doc.Blocks);
        Assert.Equal(3, ed.Doc.Blocks[0].Lines.Count);
        Assert.Equal("b", ed.Doc.Blocks[0].Lines[1].Text);
    }

    [Fact]
    public void ApplyAutoFormat_RemovesPrefixAndConverts_OneUndoOp()
    {
        var ed = Ed("- hello");
        ed.PlaceCaret(new DocPos(0, 2));   // "- " 直後 (space を打った状態)
        ed.ApplyAutoFormat(BlockKind.ListItem, 2);
        Assert.Equal(BlockKind.ListItem, ed.Doc.Blocks[0].Kind);
        Assert.Equal("hello", ed.Doc.Blocks[0].Text);
        Assert.Equal(new DocPos(0, 0), ed.Caret);
        ed.Undo();
        Assert.Equal(BlockKind.Paragraph, ed.Doc.Blocks[0].Kind);
        Assert.Equal("- hello", ed.Doc.Blocks[0].Text);
    }

    [Fact]
    public void ConvertToCodeFence_MakesEmptyCodeBlock()
    {
        var ed = Ed("```cs");
        ed.End(false);
        ed.ConvertToCodeFence("cs");
        Assert.Equal(BlockKind.CodeBlock, ed.Doc.Blocks[0].Kind);
        Assert.Equal("cs", ed.Doc.Blocks[0].CodeLang);
        Assert.Equal("", ed.Doc.Blocks[0].Text);
        ed.Undo();
        Assert.Equal("```cs", ed.Doc.Blocks[0].Text);
        Assert.Equal(BlockKind.Paragraph, ed.Doc.Blocks[0].Kind);
    }

    /// <summary>性能ゲート (部分更新の規律): タイプ 1 打鍵で動くのは編集ブロックの Version だけ。
    /// 表示側 (TextArea/RichTextEditor) はこのキーで部分更新するため、他が動くと全ブロック再レイアウトになる。</summary>
    [Fact]
    public void Typing_BumpsOnlyEditedBlockVersion()
    {
        var ed = Ed("aaa\nbbb\nccc");
        int v0 = ed.Doc.LineAt(0).Version, v2 = ed.Doc.LineAt(2).Version, sv = ed.StructureVersion;
        ed.PlaceCaret(new DocPos(1, 1));
        ed.Insert("x");
        ed.Insert("y");
        Assert.Equal(v0, ed.Doc.LineAt(0).Version);
        Assert.Equal(v2, ed.Doc.LineAt(2).Version);
        Assert.Equal(sv, ed.StructureVersion);   // 構造 (ノード列) 再構築も起きない
    }

    [Fact]
    public void CopyHelpers_PlainAndMarkdownRange()
    {
        var ed = Ed("abc\ndef\nghi");
        Assert.Equal("bc\ndef\ng", ed.GetText(new DocPos(0, 1), new DocPos(2, 1)));

        ed.PlaceCaret(new DocPos(0, 0));
        ed.SetBlockKind(BlockKind.Heading, 2);
        ed.Select(new DocPos(0, 1), new DocPos(1, 2));
        string md = Markdown.SerializeRange(ed.Doc, ed.SelMin, ed.SelMax);
        Assert.Equal("## bc\nde", md);   // 端ブロックは選択部分の run のみ + 型の記法は保持
    }

    [Fact]
    public void InsertDivider_AddsDividerAndParagraph()
    {
        var ed = Ed("text");
        ed.End(false);
        ed.InsertDivider();
        Assert.Equal(3, ed.Doc.Blocks.Count);
        Assert.Equal(BlockKind.Divider, ed.Doc.Blocks[1].Kind);
        Assert.Equal(BlockKind.Paragraph, ed.Doc.Blocks[2].Kind);
        Assert.Equal(new DocPos(2, 0), ed.Caret);
        ed.Undo();
        Assert.Single(ed.Doc.Blocks);
    }
}
