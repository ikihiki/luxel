using Luxel.Controls;
using Luxel.UI;
using static Luxel.Controls.Kit;
using static Luxel.Gallery.Stories.StoryKit;

namespace Luxel.Gallery.Stories;

/// <summary>CodeEditor — 行番号ガター + トークン色 (E1) + 補完/診断/ホバー (E2、言語サービス連携)。</summary>
public static class CodeEditorStory
{
    // 補完/診断/ホバーの言語サービス (ICodeLanguage) は DI 注入 — GalleryServices の共有シングルトン。

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

        ctx.Play("edit", async d =>
        {
            await d.Snap();                          // ガター + ハイライトの初期絵
            await d.Click(ed);                       // フォーカス
            await d.Key(Key.End);
            await d.Type(" // done");
            await d.Expect(() => ed.Text.Contains("// done"), "入力が反映される");
            await d.Snap("typed");
        });
        ctx.Play("line-ops", async d =>
        {
            await d.Click(ed);                       // フォーカス (行 0 = コメント行)
            await d.Key(Key.Down); await d.Key(Key.Down);   // "if (n < 2)..." 行へ
            await d.Key(Key.D, ctrl: true);          // 行複製
            await d.Expect(() => ed.Text.Split('\n').Length == 7, "Ctrl+D で行が増える");
            await d.Key(Key.Slash, ctrl: true);      // コメントトグル
            await d.Snap("line-ops");
        });
        ctx.Play("scrollbar", async d =>
        {
            // 高さを超える行数 → 縦スクロールバー (サム) が出る
            var sb = new System.Text.StringBuilder();
            for (int i = 1; i <= 40; i++) sb.Append($"var line{i} = {i};\n");
            code.Value = sb.ToString();
            await d.Step(2);
            await d.Click(ed);
            await d.Wheel(280, 130, -80);            // 下へスクロール
            await d.Step(2);
            await d.Snap("scrolled");                // ガター行番号が進み + スクロールバーのサム
        });

        return Border(background: Bind.From(() => UiTheme.T.Background), padding: new Thickness(20))[
            VStack(10)[
                Heading("CodeEditor"),
                Muted("行番号ガター + 現在行ハイライト + シンタックスハイライト。Ctrl+D 複製 / Ctrl+/ コメント / Alt+↑↓ 移動。"),
                ed]];
    }

    [Story("Controls/CodeEditor/Search", Height = 340, Order = 2)]
    public static Widget Search(StoryContext ctx)
    {
        Signal<string> code = ctx.Signal("code",
            "int foo = 1;\nint bar = foo + foo;\nreturn foo * bar;");
        CodeEditor ed = MakeEditor(code);
        Signal<string> query = ctx.Signal("find", "");
        Signal<string> repl = ctx.Signal("replace", "");
        TextField findBar = TextField(query, placeholder: "find", width: 160);
        TextField replBar = TextField(repl, placeholder: "replace", width: 160);

        Func<string> count = () => ed.SearchMatchCount > 0 ? $"{ed.SearchCurrent + 1}/{ed.SearchMatchCount}" : "-";
        Widget bar = HStack(6)[
            findBar,
            Button(_ => ed.SetSearch(query.Value), "検索", fontSize: 12f),
            Button(_ => ed.FindPrev(), "‹", fontSize: 12f),
            Button(_ => ed.FindNext(), "›", fontSize: 12f),
            Text(count, 12, color: Bind.From(() => UiTheme.T.TextMuted), margin: new Thickness(0, 6, 0, 0)),
            replBar,
            Button(_ => ed.ReplaceAll(repl.Value), "全置換", fontSize: 12f)];

        ctx.Play(async d =>
        {
            ed.SetSearch("foo");
            await d.Step(2);
            await d.Expect(() => ed.SearchMatchCount == 4, "4 マッチをハイライト");
            await d.Snap("matches");
            ed.ReplaceAll("qux");
            await d.Step(2);
            await d.Expect(() => ed.Text.Contains("qux") && !ed.Text.Contains("foo"), "全置換される");
            await d.Snap("replaced");
        });

        return Border(background: Bind.From(() => UiTheme.T.Background), padding: new Thickness(20))[
            VStack(10)[
                Heading("CodeEditor — 検索 / 置換 (E3)"),
                bar,
                ed]];
    }

    [Story("Controls/CodeEditor/Completion", Height = 320, Order = 1)]
    public static Widget Completion(StoryContext ctx, ICodeLanguage lang)
    {
        Signal<string> code = ctx.Signal("code", "var s = \"hi\";\ns.");
        CodeEditor ed = MakeEditor(code);
        ed.LanguageService = lang;   // 補完/診断/ホバー (DI 注入)

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
