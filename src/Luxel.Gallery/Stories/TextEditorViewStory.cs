using Luxel.Controls;
using Luxel.Editor;
using Luxel.Typography;
using Luxel.UI;
using static Luxel.Controls.Kit;
using static Luxel.Gallery.Stories.StoryKit;

namespace Luxel.Gallery.Stories;

/// <summary>TextEditorView — テキストエディタ新スタック (ADR-0006 / ToDo 22) のビュー。
/// 編集意味論・座標写像・装飾は canvas 非依存の Luxel.Editor が持ち、この widget は入力を Transaction にして
/// ジオメトリの矩形を塗るだけ。折返し・プロポーショナル・マルチカーソルはジオメトリ由来で最初から正しい。</summary>
public static class TextEditorViewStory
{
    [Story("Controls/TextEditorView/Basic", Height = 300)]
    public static Widget Basic(StoryContext ctx)
    {
        Signal<string> value = ctx.Signal("text",
            "新スタックのテキストエディタ。\nTransaction ベースで undo が正確、\nマルチカーソルが native。");
        TextEditorView ed = TextEditorView(value, editorHeight: 200f, editorWidth: 560f);

        ctx.Play("edit", async d =>
        {
            await d.Snap();                         // 初期テキスト + キャレット
            await d.Click(ed);                      // フォーカス
            await d.Key(Key.End);
            await d.Type(" 折返しも正しい。");
            await d.Expect(() => ed.Text.Contains("折返し"), "入力が反映される");
            await d.Snap("typed");
            await d.Key(Key.A, ctrl: true);         // 全選択
            await d.Expect(() => ed.HasSelection, "Ctrl+A で全選択");
            await d.Snap("selected");
        });
        ctx.Play("undo", async d =>
        {
            await d.Click(ed);
            await d.Key(Key.End);
            await d.Type("XYZ");
            await d.Expect(() => ed.Text.Contains("XYZ"), "入力される");
            await d.Key(Key.Z, ctrl: true);
            await d.Key(Key.Z, ctrl: true);
            await d.Key(Key.Z, ctrl: true);
            await d.Expect(() => !ed.Text.Contains("XYZ"), "undo で戻る");
            await d.Snap("undone");
        });

        return Border(background: Bind.From(() => UiTheme.T.Background), padding: new Thickness(20))[
            VStack(10)[
                Heading("TextEditorView (新スタック)"),
                Muted("Luxel.Editor (Transaction + native 複数レンジ + 装飾) を canvas に載せた薄いビュー。"),
                ed]];
    }

    [Story("Controls/TextEditorView/Code", Height = 320, Order = 2)]
    public static Widget Code(StoryContext ctx, ICodeLanguage lang)
    {
        Signal<string> code = ctx.Signal("code",
            "// 新スタックのコードエディタ (プロバイダで色分け + 波線)\n" +
            "public int Fib(int n)\n" +
            "{\n" +
            "    if (n < 2) return n;\n" +
            "    return Fib(n - 1) + Fib(n - 2);\n" +
            "}\n" +
            "int broken = ;");
        TextEditorView ed = TextEditorView(code, editorHeight: 260f, editorWidth: 560f);
        ed.ShowLineNumbers = true;
        (_, _, _, VectorFont mono) = EditorFaces.Value;
        ed.EditorFont = mono;

        Func<Theme> theme = () => UiTheme.T;
        ed.Providers.Add(new SyntaxHighlightProvider(Luxel.Highlight.TextMateHighlighter.Instance, "csharp", theme));
        var diags = new DiagnosticsProvider(lang, theme);
        ed.Providers.Add(diags);
        ed.Providers.Add(new CurrentLineProvider(theme));

        ctx.Play(async d =>
        {
            await d.Snap();                               // 色分け + 行番号 + 現在行 + "int broken = ;" に波線
            await d.Expect(() => diags.Count > 0, "エラー行に波線が付く");
            await d.Click(ed);
            await d.Key(Key.Down); await d.Key(Key.Down); await d.Key(Key.Down);
            await d.Snap("caret");                        // 現在行ハイライトが移動
        });

        return Border(background: Bind.From(() => UiTheme.T.Background), padding: new Thickness(20))[
            VStack(10)[
                Heading("TextEditorView — コード (色分け + 診断波線 + ガター)"),
                Muted("SyntaxHighlightProvider / DiagnosticsProvider / CurrentLineProvider を Providers に足すだけ。"),
                ed]];
    }

    [Story("Controls/TextEditorView/Edit", Height = 320, Order = 3)]
    public static Widget Edit(StoryContext ctx)
    {
        Signal<string> code = ctx.Signal("code", "int foo = 1;\nint bar = foo + foo;\nreturn foo * bar;");
        TextEditorView ed = TextEditorView(code, editorHeight: 190f, editorWidth: 520f);
        ed.ShowLineNumbers = true;
        (_, _, _, VectorFont mono) = EditorFaces.Value;
        ed.EditorFont = mono;
        ed.Providers.Add(new SyntaxHighlightProvider(Luxel.Highlight.TextMateHighlighter.Instance, "csharp", () => UiTheme.T));

        Signal<string> query = ctx.Signal("find", "");
        TextField findBar = TextField(query, placeholder: "find", width: 140);
        Widget bar = HStack(6)[
            findBar,
            Button(_ => ed.SetSearch(query.Value), "検索", fontSize: 12f),
            Button(_ => ed.FindNext(), "›", fontSize: 12f),
            Button(_ => ed.ReplaceAll("qux"), "全置換→qux", fontSize: 12f)];

        ctx.Play("line-ops", async d =>
        {
            await d.Click(ed);
            await d.Key(Key.Down);                          // 2 行目へ
            await d.Key(Key.Down, alt: true);               // 行を下へ移動
            await d.Key(Key.Down, shift: true, alt: true);  // 行を複製
            await d.Key(Key.Slash, ctrl: true);             // コメントトグル
            await d.Expect(() => ed.Text.Contains("//"), "行操作が反映される");
            await d.Snap("line-ops");
        });
        ctx.Play("search", async d =>
        {
            ed.SetSearch("foo");
            await d.Step(1);
            await d.Expect(() => ed.SearchMatchCount == 4, "4 マッチをハイライト");
            await d.Snap("matches");
            ed.ReplaceAll("qux");
            await d.Step(1);
            await d.Expect(() => ed.Text.Contains("qux") && !ed.Text.Contains("foo"), "全置換される");
            await d.Snap("replaced");
        });

        return Border(background: Bind.From(() => UiTheme.T.Background), padding: new Thickness(20))[
            VStack(10)[
                Heading("TextEditorView — 行操作 / 検索置換"),
                Muted("Alt+↑↓ 行移動 / Shift+Alt+↓ 複製 / Ctrl+/ コメント。検索は背景ハイライト + ナビ + 全置換。"),
                bar,
                ed]];
    }

    [Story("Controls/TextEditorView/Completion", Height = 320, Order = 4)]
    public static Widget Completion(StoryContext ctx, ICodeLanguage lang)
    {
        Signal<string> code = ctx.Signal("code", "var s = \"hi\";\ns.");
        TextEditorView ed = TextEditorView(code, editorHeight: 240f, editorWidth: 560f);
        ed.ShowLineNumbers = true;
        (_, _, _, VectorFont mono) = EditorFaces.Value;
        ed.EditorFont = mono;
        ed.LanguageService = lang;
        ed.Providers.Add(new SyntaxHighlightProvider(Luxel.Highlight.TextMateHighlighter.Instance, "csharp", () => UiTheme.T));
        ed.Providers.Add(new DiagnosticsProvider(lang, () => UiTheme.T));

        ctx.Play("complete", async d =>
        {
            await d.Snap();
            await d.Click(ed);
            await d.Key(Key.Down); await d.Key(Key.End);      // 2 行目 "s." の末尾へ
            await d.Key(Key.Space, ctrl: true);               // 補完を開く
            await d.Step(2);
            await d.Expect(() => ed.CompletionOpen && ed.CompletionCount > 0, "補完ポップアップが開く");
            await d.Snap("popup");
            int all = ed.CompletionCount;
            await d.Type("Le");                               // タイプで絞り込み (閉じずにフィルタ)
            await d.Step(1);
            await d.Expect(() => ed.CompletionOpen && ed.CompletionCount <= all, "タイプで候補が絞られる");
            await d.Snap("filtered");
            await d.Key(Key.Enter);                           // 確定
            await d.Expect(() => !ed.CompletionOpen && ed.Text.Contains("Le"), "Enter で確定して閉じる");
            await d.Snap("confirmed");
        });
        ctx.Play("hover", async d =>
        {
            await d.Click(ed);
            var wp = ed.WorldPos;
            d.Host.PointerMove(wp.X + 34, wp.Y + 33);         // 2 行目 "s." の識別子 s の上に留まる
            await d.Step(31);                                 // dwell (フレーム基準 = 決定的)
            await d.Expect(() => ed.HoverTipOpen, "同一位置に留まるとホバーツールチップ");
            await d.Snap("hover");
        });

        return Border(background: Bind.From(() => UiTheme.T.Background), padding: new Thickness(20))[
            VStack(10)[
                Heading("TextEditorView — 補完 / ホバー (Popup)"),
                Muted("Ctrl+Space で補完 (CaretRect にアンカー、画面端でフリップ)。dwell でホバー。"),
                ed]];
    }

    // 行頭番号 + 行内 widget (◯ をチェックボックスに置換) + 左縦バーを供給する装飾プロバイダ (デモ用)
    private sealed class ListDecoProvider : IDecorationProvider
    {
        public string Owner => "list-demo";
        public DecorationSet Provide(EditorState s)
        {
            var d = new List<Decoration>();
            TextDoc doc = s.Doc;
            for (int i = 0; i < doc.LineCount; i++)
                d.Add(new LinePrefixDecoration(doc.LineStart(i), $"{i + 1}. ", 0xFF8A8A8A));
            int ci = 0;
            for (int idx = doc.Text.IndexOf('◯'); idx >= 0; idx = doc.Text.IndexOf('◯', idx + 1))
                d.Add(new WidgetDecoration(idx, idx + 1, 44f, 22f, $"chk{ci++}"));
            d.Add(new BlockDecoration(0, doc.Length, BarColor: 0xFF4A90D9, BarWidth: 3f, Indent: 14f));
            return new DecorationSet(d);
        }
    }

    [Story("Controls/TextEditorView/Widgets", Height = 260, Order = 1)]
    public static Widget Widgets(StoryContext ctx)
    {
        Signal<string> value = ctx.Signal("text", "牛乳を買う ◯\n卵を買う ◯\nパンを買う ◯");
        TextEditorView ed = TextEditorView(value, editorHeight: 150f, editorWidth: 460f);

        Signal<bool>[] checks = [ctx.Signal("c0", false), ctx.Signal("c1", false), ctx.Signal("c2", true)];
        Switch[] toggles = [Switch(checks[0]), Switch(checks[1]), Switch(checks[2])];
        ed.WidgetResolver = key => key switch
        {
            "chk0" => toggles[0],
            "chk1" => toggles[1],
            "chk2" => toggles[2],
            _ => null,
        };
        ed.Providers.Add(new ListDecoProvider());

        ctx.Play("check", async d =>
        {
            await d.Snap();                                  // 番号 + 行内チェックボックス + 左バー
            await d.Click(toggles[0]);                       // 行内 widget を押す
            await d.Step(1);
            await d.Expect(() => checks[0].Value, "行内チェックボックスが押せて ON になる");
            await d.Snap("checked");
        });

        return Border(background: Bind.From(() => UiTheme.T.Background), padding: new Thickness(20))[
            VStack(10)[
                Heading("TextEditorView — 行内 widget + 行頭装飾"),
                Muted("装飾プロバイダが行頭番号・置換 widget (◯→チェックボックス)・左縦バーを供給。widget は行内でホストされ状態を持つ。"),
                ed]];
    }
}
