using Luxel.Controls;
using Luxel.Document;
using Luxel.Typography;
using Luxel.TwoD;
using Luxel.TwoD.Skia;
using Luxel.UI;
using Xunit;
using static Luxel.Controls.Kit;

namespace Luxel.Tests;

/// <summary>CodeEditor E1 — ガター/編集/ハイライトを **GpuDevice なし** (Skia) で検証。</summary>
public class CodeEditorTests
{
    private static (UiHost Host, CodeEditor Ed, RetainedCanvas Canvas) NewEditor(
        string code, ISyntaxHighlighter? hl = null)
    {
        VectorFont font = VectorFont.LoadSystem();
        var canvas = new RetainedCanvas();
        var host = new UiHost(canvas, font, 400, 200);
        CodeEditor ed = CodeEditor(new Signal<string>(code), editorHeight: 180f, editorWidth: 380f);
        ed.MonoFont = font;
        if (hl is not null) ed.Highlighter = hl;
        host.SetRoot(ed);
        return (host, ed, canvas);
    }

    [Fact]
    public void Renders_WithGutter_WithoutGpu()
    {
        (UiHost host, CodeEditor ed, RetainedCanvas canvas) = NewEditor("int a = 1;\nvar b = a + 2;");
        byte[] px = SkiaRenderer.RenderRgba(canvas, Camera2D.Pixels, 400, 200);
        int nonBg = 0;
        for (int i = 0; i < px.Length; i += 4)
            if (px[i] < 245 || px[i + 1] < 245 || px[i + 2] < 245) nonBg++;
        Assert.True(nonBg > 100, $"コード/ガターが描画されていない (非背景 {nonBg})");
        Assert.Equal(2, ed.Text.Split('\n').Length);
    }

    [Fact]
    public void Typing_InsertsAndSyncsSignal()
    {
        var code = new Signal<string>("");
        VectorFont font = VectorFont.LoadSystem();
        var host = new UiHost(new RetainedCanvas(), font, 400, 200);
        CodeEditor ed = CodeEditor(code, editorHeight: 180f, editorWidth: 380f);
        ed.MonoFont = font;
        host.SetRoot(ed);

        host.Click(60, 20);                 // フォーカス
        host.Char("x"); host.Char("="); host.Char("1");
        Assert.Equal("x=1", code.Value);    // signal へ双方向反映
        Assert.Equal("x=1", ed.Text);
    }

    [Fact]
    public void Enter_AddsLine_And_CaretOffsetTracks()
    {
        var code = new Signal<string>("ab");
        VectorFont font = VectorFont.LoadSystem();
        var host = new UiHost(new RetainedCanvas(), font, 400, 200);
        CodeEditor ed = CodeEditor(code, editorHeight: 180f, editorWidth: 380f);
        ed.MonoFont = font;
        host.SetRoot(ed);

        host.Click(120, 12);                // 行 0 内をクリック (x はテキスト超で末尾へクランプ)
        host.KeyDown(Key.End);
        host.KeyDown(Key.Enter);
        host.Char("c");
        Assert.Equal("ab\nc", code.Value);
    }

    [Fact]
    public void Highlighter_TokensAreQueried()
    {
        var hl = new RecordingHighlighter();
        (UiHost host, CodeEditor ed, RetainedCanvas canvas) = NewEditor("class Foo", hl);
        _ = SkiaRenderer.RenderRgba(canvas, Camera2D.Pixels, 400, 200);
        Assert.Contains("class Foo", hl.SeenLines);   // 行単位でトークン化される
    }

    [Fact]
    public void CaretOffset_IsFlatAcrossLines()
    {
        var code = new Signal<string>("ab\ncde");
        VectorFont font = VectorFont.LoadSystem();
        var host = new UiHost(new RetainedCanvas(), font, 400, 200);
        CodeEditor ed = CodeEditor(code, editorHeight: 180f, editorWidth: 380f);
        ed.MonoFont = font;
        host.SetRoot(ed);

        host.Click(60, 10);                 // 行 0 にフォーカス
        host.KeyDown(Key.Down);             // 2 行目へ
        host.KeyDown(Key.End);              // 行末
        Assert.Equal(6, ed.CaretOffset);    // "ab"(2) + \n(1) + "cde"(3) = 6
    }

    // ---- E2: 言語サービス (補完/診断/ホバー) — スタブで配線を検証 ----

    private sealed class StubLanguage : ICodeLanguage
    {
        public IReadOnlyList<CodeCompletion> Complete(string code, int position)
            => [new("Length", "Length", "Property"), new("ToUpper", "ToUpper", "Method")];
        public IReadOnlyList<CodeDiagnostic> Diagnose(string code)
            => code.Contains("err") ? [new(1, 1, 3, "bad", true)] : [];
        public string? Hover(string code, int position) => "hover!";
    }

    [Fact]
    public void CtrlSpace_OpensCompletion_EnterInserts()
    {
        var code = new Signal<string>("x");
        VectorFont font = VectorFont.LoadSystem();
        var host = new UiHost(new RetainedCanvas(), font, 400, 200);
        CodeEditor ed = CodeEditor(code, editorHeight: 180f, editorWidth: 380f);
        ed.MonoFont = font; ed.LanguageService = new StubLanguage();
        host.SetRoot(ed);

        host.Click(60, 10);
        host.KeyDown(Key.End);
        host.KeyDown(Key.Space, ctrl: true);
        Assert.True(ed.CompletionOpen);
        Assert.Equal(2, ed.CompletionCount);

        host.KeyDown(Key.Down);                  // 2 番目 (ToUpper) を選択
        host.KeyDown(Key.Enter);
        Assert.False(ed.CompletionOpen);
        Assert.Contains("ToUpper", ed.Text);     // 断片 "x" を置換して挿入
    }

    [Fact]
    public void Escape_ClosesCompletion()
    {
        var code = new Signal<string>("");
        VectorFont font = VectorFont.LoadSystem();
        var host = new UiHost(new RetainedCanvas(), font, 400, 200);
        CodeEditor ed = CodeEditor(code, editorHeight: 180f, editorWidth: 380f);
        ed.MonoFont = font; ed.LanguageService = new StubLanguage();
        host.SetRoot(ed);
        host.Click(60, 10);
        host.KeyDown(Key.Space, ctrl: true);
        Assert.True(ed.CompletionOpen);
        host.KeyDown(Key.Escape);
        Assert.False(ed.CompletionOpen);
    }

    [Fact]
    public void Diagnostics_ComputedOnEdit()
    {
        var code = new Signal<string>("");
        VectorFont font = VectorFont.LoadSystem();
        var host = new UiHost(new RetainedCanvas(), font, 400, 200);
        CodeEditor ed = CodeEditor(code, editorHeight: 180f, editorWidth: 380f);
        ed.MonoFont = font; ed.LanguageService = new StubLanguage();
        host.SetRoot(ed);
        host.Click(60, 10);
        host.Char("e"); host.Char("r"); host.Char("r");
        Assert.Equal(1, ed.DiagnosticCount);     // "err" → 診断 1
        Assert.Equal("hover!", ed.HoverText);    // キャレット位置のホバー
    }

    // ---- E3: 行操作 ----

    private static (UiHost, CodeEditor) Focused(string code)
    {
        var sig = new Signal<string>(code);
        VectorFont font = VectorFont.LoadSystem();
        var host = new UiHost(new RetainedCanvas(), font, 400, 200);
        CodeEditor ed = CodeEditor(sig, editorHeight: 180f, editorWidth: 380f);
        ed.MonoFont = font;
        host.SetRoot(ed);
        host.Click(60, 10);   // フォーカス (行 0)
        return (host, ed);
    }

    [Fact]
    public void CtrlD_DuplicatesLine()
    {
        (UiHost host, CodeEditor ed) = Focused("hello\nworld");
        host.KeyDown(Key.D, ctrl: true);
        Assert.Equal("hello\nhello\nworld", ed.Text);
    }

    [Fact]
    public void CtrlSlash_TogglesComment()
    {
        (UiHost host, CodeEditor ed) = Focused("    int x = 1;");
        host.KeyDown(Key.Slash, ctrl: true);
        Assert.Equal("    // int x = 1;", ed.Text);   // インデント保持で "// " 付与
        host.KeyDown(Key.Slash, ctrl: true);
        Assert.Equal("    int x = 1;", ed.Text);      // トグルで戻る
    }

    [Fact]
    public void AltDown_MovesLineDown()
    {
        (UiHost host, CodeEditor ed) = Focused("a\nb\nc");
        host.KeyDown(Key.Down, alt: true);            // 行 0 (a) を下へ
        Assert.Equal("b\na\nc", ed.Text);
    }

    [Fact]
    public void AltUp_AtTop_IsNoOp()
    {
        (UiHost host, CodeEditor ed) = Focused("a\nb");
        host.KeyDown(Key.Up, alt: true);              // 先頭行は動かない
        Assert.Equal("a\nb", ed.Text);
    }

    // ---- E3: 検索/置換 ----

    [Fact]
    public void Search_FindsAllMatches_AndWraps()
    {
        (_, CodeEditor ed) = Focused("foo bar foo\nbaz foo");
        ed.SetSearch("foo");
        Assert.Equal(3, ed.SearchMatchCount);
        Assert.Equal(0, ed.SearchCurrent);
        ed.FindNext(); Assert.Equal(1, ed.SearchCurrent);
        ed.FindNext(); Assert.Equal(2, ed.SearchCurrent);
        ed.FindNext(); Assert.Equal(0, ed.SearchCurrent);   // ラップ
        ed.FindPrev(); Assert.Equal(2, ed.SearchCurrent);
    }

    [Fact]
    public void ReplaceAll_ReplacesEveryMatch()
    {
        (_, CodeEditor ed) = Focused("x = x + x");
        ed.SetSearch("x");
        ed.ReplaceAll("y");
        Assert.Equal("y = y + y", ed.Text);
        Assert.Equal(0, ed.SearchMatchCount);   // 置換後はクリア
    }

    [Fact]
    public void ReplaceCurrent_ReplacesOne_AndReindexes()
    {
        (_, CodeEditor ed) = Focused("a a a");
        ed.SetSearch("a");
        Assert.Equal(3, ed.SearchMatchCount);
        ed.ReplaceCurrent("b");
        Assert.Equal("b a a", ed.Text);
        Assert.Equal(2, ed.SearchMatchCount);   // 残り 2
    }

    private sealed class RecordingHighlighter : ISyntaxHighlighter
    {
        public readonly List<string> SeenLines = new();
        public bool Supports(string lang) => true;
        public SyntaxToken[] Tokenize(string lang, string code)
        {
            SeenLines.Add(code);
            return code.StartsWith("class")
                ? [new SyntaxToken(0, 5, TokenKind.Keyword)]
                : [];
        }
    }
}
