using Luxel.Controls;
using Luxel.UI;
using static Luxel.Controls.Kit;
using static Luxel.Gallery.Stories.StoryKit;

namespace Luxel.Gallery.Stories;

/// <summary>CodeEditor (E1) — 行番号ガター + 現在行ハイライト + シンタックスハイライトのコードエディタ。</summary>
public static class CodeEditorStory
{
    [Story("Controls/CodeEditor/Basic", Height = 320)]
    public static Widget Basic(StoryContext ctx)
    {
        Signal<string> code = ctx.Signal("code",
            "// CodeEditor — 等幅・行番号・トークン色\n" +
            "public int Fib(int n)\n" +
            "{\n" +
            "    if (n < 2) return n;\n" +
            "    return Fib(n - 1) + Fib(n - 2);\n" +
            "}");
        CodeEditor ed = CodeEditor(code, editorHeight: 260f, editorWidth: 560f);
        (_, _, _, ed.MonoFont) = EditorFaces.Value;
        ed.Highlighter = Luxel.Highlight.TextMateHighlighter.Instance;
        ed.Language = "csharp";

        ctx.Play(async d =>
        {
            await d.Snap();                          // ガター + ハイライトの初期絵
            await d.Click(ed);                       // フォーカス
            await d.Key(Key.End);
            await d.Type(" // done");
            await d.Expect(() => ed.Text.Contains("// done"), "入力が反映される");
            await d.Snap("typed");
        });

        return Border(background: Bind.From(() => UiTheme.T.Background), padding: new Thickness(20))[
            VStack(10)[
                Heading("CodeEditor"),
                Muted("行番号ガター + 現在行ハイライト + シンタックスハイライト。等幅・折り返しなし。"),
                ed]];
    }
}
