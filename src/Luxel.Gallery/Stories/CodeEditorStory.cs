using Luxel.Controls;
using Luxel.Scripting;
using Luxel.UI;
using static Luxel.Controls.Kit;
using static Luxel.Gallery.Stories.StoryKit;

namespace Luxel.Gallery.Stories;

/// <summary>CodeEditor — 行番号ガター + トークン色 (E1) + 補完/診断/ホバー (E2、言語サービス連携)。</summary>
public static class CodeEditorStory
{
    // 補完/診断/ホバーの言語サービス (プロセス共有、初回は 1-2 秒)
    private static readonly Lazy<CsharpCodeLanguage> Lang = new(() => new CsharpCodeLanguage(
        new ScriptWorkspace(
            references: [typeof(object).Assembly, typeof(Enumerable).Assembly, typeof(System.Text.StringBuilder).Assembly],
            usings: ["System", "System.Linq", "System.Text", "System.Collections.Generic"])));

    private static CodeEditor MakeEditor(Signal<string> code)
    {
        CodeEditor ed = CodeEditor(code, editorHeight: 260f, editorWidth: 560f);
        (_, _, _, ed.MonoFont) = EditorFaces.Value;
        ed.Highlighter = Luxel.Highlight.TextMateHighlighter.Instance;
        ed.Language = "csharp";
        return ed;
    }

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
        CodeEditor ed = MakeEditor(code);

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

    [Story("Controls/CodeEditor/Completion", Height = 320, Order = 1)]
    public static Widget Completion(StoryContext ctx)
    {
        Signal<string> code = ctx.Signal("code", "var s = \"hi\";\ns.");
        CodeEditor ed = MakeEditor(code);
        ed.LanguageService = Lang.Value;   // 補完/診断/ホバー

        ctx.Play("complete", async d =>
        {
            await d.Snap();
            await d.Click(ed);
            await d.Key(Key.End);                    // 2 行目 "s." の末尾へ
            await d.Key(Key.Space, ctrl: true);      // 補完を開く
            await d.Step(2);
            await d.Expect(() => ed.CompletionOpen && ed.CompletionCount > 0, "補完ポップアップが開く");
            await d.Snap("popup");                   // 候補リストの絵
            await d.Key(Key.Enter);                  // 先頭候補を確定
            await d.Expect(() => !ed.CompletionOpen, "Enter で確定して閉じる");
            await d.Snap("inserted");
        });
        ctx.Play("diagnostics", async d =>
        {
            code.Value = "int x = ;";                // 構文エラー
            await d.Click(ed);
            await d.Key(Key.End);
            await d.Type(" ");                       // 編集トリガで診断計算
            await d.Expect(() => ed.DiagnosticCount > 0, "エラーに波線が付く");
            await d.Snap("squiggle");
        });

        return Border(background: Bind.From(() => UiTheme.T.Background), padding: new Thickness(20))[
            VStack(10)[
                Heading("CodeEditor — 補完 / 診断 / ホバー (E2)"),
                Muted("Ctrl+Space で補完ポップアップ (↑↓/Enter/Escape)。エラーは波線。キャレット位置の型がホバー。"),
                ed]];
    }
}
