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
