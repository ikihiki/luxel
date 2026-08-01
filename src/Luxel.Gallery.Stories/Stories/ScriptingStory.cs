using Luxel.Controls;
using Luxel.Scripting;
using Luxel.Gallery.Playground;
using Luxel.Graphics.TwoD;
using Luxel.UI;
using Luxel.UI.Tailwind;
using static Luxel.Controls.Kit;
using static Luxel.Gallery.Stories.StoryKit;

namespace Luxel.Gallery.Stories;

/// <summary>
/// C# スクリプト (P1) — docs に埋め込める ```csx ライブブロックの実演。
/// エディタで編集 → Run = Roslyn コンパイル (ソースキャッシュ) → 最後の式の Widget をその場に実体化。
/// コンパイルエラーは行番号付きでインライン表示、実行時例外もスクリプト行へマップされる。
/// 初期状態は未実行 (コード表示のみ) — snap/E2E 決定的。
/// </summary>
public static class ScriptingStory
{
    // ScriptHost / ScriptWorkspace / 言語サービスは **DI で共有** (GalleryServices が登録)。
    // ストーリー関数の引数で受け取る (minimal API 風) — static Lazy は撤去済み。

    /// <summary>csx プレイグラウンド: コードエディタ + Run + インライン診断 + 出力 (返した Widget)。</summary>
    private sealed class CsxBlock : CompositeControl, IDisposable
    {
        private readonly StoryContext _ctx;
        private readonly ScriptHost _host;            // DI 注入 (GalleryServices の共有シングルトン)
        private readonly Signal<string> _code;
        private readonly Signal<string> _status = new("");
        private readonly Signal<int> _ver = new(0);   // 出力/診断の構造変化 → TrackBuild が Rebuild
        private readonly TextEditorView _editor;      // 新スタック (ガター/補完/診断/ハイライトを provider で)
        private readonly float _maxW;

        private string _diags = "";       // Run 由来のメッセージ (構造状態)
        private Widget? _output;

        /// <summary>コードエディタ (play からクリック/フォーカス/Ctrl+Space する)。</summary>
        internal TextEditorView Editor => _editor;
        /// <summary>Run ボタン (play からクリックするために公開)。</summary>
        internal Button RunButton { get; }
        /// <summary>直近 Run が成功して Widget を出したか (play の Expect 用)。</summary>
        internal bool LastRunOk { get; private set; }

        public CsxBlock(string initialCode, float maxWidth, StoryContext ctx, ScriptHost host, ICodeLanguage lang)
        {
            _ctx = ctx;
            _host = host;
            _maxW = MathF.Max(240, maxWidth);
            _code = new Signal<string>(initialCode);
            _editor = TextEditorView(_code, editorHeight: 170f, editorWidth: _maxW - 96);
            _editor.ShowLineNumbers = true;
            _editor.EditorFont = EditorFaces.Value.Mono;
            _editor.LanguageService = lang;   // Ctrl+Space 補完 + ホバー (DI 注入)
            Func<Theme> th = () => UiTheme.T;
            _editor.Providers.Add(new SyntaxHighlightProvider(Luxel.Highlight.TextMateHighlighter.Instance, "csharp", th));
            _editor.Providers.Add(new DiagnosticsProvider(lang, th));   // 診断波線
            _editor.Providers.Add(new CurrentLineProvider(th));         // 現在行ハイライト
            RunButton = Button(_ => Run(), "Run");
        }

        /// <summary>コードを差し替える (play からエラー例の検証に使う)。</summary>
        internal void SetCode(string code) => _code.Value = code;

        private void Run()
        {
            ScriptResult r = _host.Run(_code.Value, new ScriptGlobals { Ctx = _ctx });
            LastRunOk = false;
            if (!r.Success)
            {
                _output = null;
                _diags = r.Exception is not null
                    ? $"実行時例外{(r.ExceptionLine is int l ? $" (行 {l})" : "")}: {r.Exception.Message}"
                    : string.Join("\n", r.Diagnostics.Where(d => d.IsError)
                        .Select(d => $"行 {d.Line}:{d.Column} {d.Message}"));
                _status.Value = "✗";
            }
            else
            {
                (_output as IDisposable)?.Dispose();
                _output = r.ReturnValue as Widget;
                _diags = _output is null && r.ReturnValue is not null
                    ? $"戻り値が Widget ではありません: {r.ReturnValue.GetType().Name}" : "";
                _status.Value = _output is not null ? "✓" : "✓ (出力なし)";
                LastRunOk = _output is not null;
            }
            _ver.Value++;   // 構造変化 — Rebuild
        }

        protected override Widget Build()
        {
            _ = _ver.Value;   // TrackBuild — Run 毎に作り直す
            Func<string> status = () => _status.Value;
            var kids = new List<Widget>
            {
                HStack(6)[
                    _editor,
                    VStack(4)[
                        RunButton,
                        Text(status, 12, color: Bind.From(() => UiTheme.T.TextMuted)),
                        Text("Ctrl+Space 補完", 10, color: Bind.From(() => UiTheme.T.TextMuted))]],
            };
            if (_diags.Length > 0)
                kids.Add(Text(_diags, 11, color: Tw.Red600));
            if (_output is not null)
                kids.Add(Border(background: Bind.From(() => UiTheme.T.SurfaceAlt),
                                rounded: 6, padding: new Thickness(10), width: _maxW)[_output]);
            return VStack(6)[kids.ToArray()];
        }

        public override string? DebugDetail => $"csx ({_code.Value.Length} 文字)";

        public void Dispose() => (_output as IDisposable)?.Dispose();
    }

    [Story(PlaygroundContract.StoryPath, Height = 620, Order = 2031)]
    public static Widget Playground(StoryContext ctx, ScriptHost host, ICodeLanguage lang)
    {
        var block = new CsxBlock(
            "// Edit this C# and run it as a normal Gallery Story.\n" +
            "var label = \"Click me\";\n" +
            "Button(_ => Log(\"clicked\"), label)",
            maxWidth: 560, ctx, host, lang);

        return Border(background: Bind.From(() => UiTheme.T.Background), padding: new Thickness(20))[
            VStack(10)[
                Heading("C# Playground"),
                Muted("Edit and run a Luxel Widget inside the Gallery. The native Gallery uses ScriptHost; the web Gallery uses the browser Roslyn runtime."),
                block]];
    }

    [Story("Examples/Scripting/LiveCsx", Height = 520, Order = 2032)]
    public static Widget LiveCsx(StoryContext ctx, ScriptHost host, ICodeLanguage lang)
    {
        var block = new CsxBlock(
            "// 最後の式の Widget が下に実体化される。Log(...) は Log タブへ\n" +
            "var names = new[] { \"Luxel\", \"Roslyn\", \"csx\" };\n" +
            "VStack(6)[\n" +
            "    Label($\"こんにちは {string.Join(\" + \", names)}\"),\n" +
            "    Button(_ => Log(\"クリックされた!\"), \"Click me\")]",
            maxWidth: 440, ctx, host, lang);

        ctx.Play("run", async d =>
        {
            await d.Snap();                          // 未実行 (コード表示のみ)
            await d.Click(block.RunButton);          // コンパイル + 実行 (初回は数秒)
            await d.Step(4);
            await d.Snap("ran");                     // 返した Widget が出た絵
            await d.Expect(() => block.LastRunOk, "スクリプトが Widget を返して実体化される");
        });
        ctx.Play("error", async d =>
        {
            block.SetCode("var x = 1 +\nOops(x)");   // 構文エラー + 未定義シンボル
            await d.Click(block.RunButton);
            await d.Step(4);
            await d.Snap("diag");                    // 行番号付き診断が赤字で出た絵
            await d.Expect(() => !block.LastRunOk, "エラー時は Widget を出さず診断を表示");
        });
        ctx.Play("complete", async d =>
        {
            // TextEditorView 内蔵の Ctrl+Space 補完 — キャレットを "hi". の直後に置いて開く
            block.SetCode("\"hi\".");
            await d.Step(1);
            await d.Click(block.Editor);             // フォーカス
            await d.Key(Key.End);
            await d.Key(Key.Space, ctrl: true);      // 補完ポップアップ
            await d.Step(2);
            await d.Snap("list");                    // フローティング候補の絵
            await d.Expect(() => block.Editor.CompletionOpen, "キャレット位置の補完ポップアップが開く");
        });

        return Border(background: Bind.From(() => UiTheme.T.Background), padding: new Thickness(20))[
            VStack(10)[
                Heading("C# ライブスクリプト (csx)"),
                Muted("TextEditorView でガター/ハイライト/Ctrl+Space 補完/診断波線。Run で最後の式の Widget を実体化。"),
                block]];
    }


    [Story("Examples/Scripting/Repl", Height = 460, Order = 2033)]
    public static Widget Repl(StoryContext ctx, ScriptHost host)
    {
        var repl = new ReplConsole(460, host, new ScriptGlobals { Ctx = ctx });
        ctx.Play(async d =>
        {
            await d.Snap();
            repl.SetInput("var a = 21;");
            await d.Click(repl.SubmitButton);
            repl.SetInput("a * 2");                  // 前の行の変数が見える
            await d.Click(repl.SubmitButton);
            await d.Step(2);
            await d.Snap("session");
            await d.Expect(() => repl.LastOutput == "42", "継続セッションで状態が残る");
        });
        return Border(background: Bind.From(() => UiTheme.T.Background), padding: new Thickness(20))[
            VStack(10)[
                Heading("REPL コンソール (継続セッション)"),
                Muted("行を投入すると前の行で宣言した変数が次に見える — DevTools のスクリプトコンソール相当。"),
                repl]];
    }

    // ---- Jupyter 風ノートブック: 文章 (markdown) + コードセル (TextEditorView + Run + 結果) ----

    /// <summary>ノートブックの 1 コードセル — TextEditorView (ガター/補完/診断/スクロールバー) +
    /// Run + 結果パネル。MarkdownDoc の embed block widget として文章の間に並ぶ。
    /// **クリックが Run に届くのはヒットの深さ優先バブリングのおかげ** (埋め込み widget の
    /// ボタンが親エディタの選択ヒットに勝つ)。</summary>
    private sealed class NotebookCell : CompositeControl, IDisposable
    {
        private readonly StoryContext _ctx;
        private readonly ScriptHost _host;            // DI 注入 (共有シングルトン)
        private readonly Signal<string> _code;
        private readonly Signal<int> _ver = new(0);
        private readonly TextEditorView _editor;
        private readonly float _maxW;
        private Widget? _output;
        private string _outText = "";
        private bool _ok;

        internal Button RunButton { get; }
        internal bool HasOutput => _output is not null || _outText.Length > 0;

        public NotebookCell(string body, float maxWidth, StoryContext ctx, ScriptHost host, ICodeLanguage lang)
        {
            _ctx = ctx;
            _host = host;
            _maxW = MathF.Max(240, maxWidth);
            _code = new Signal<string>(body);
            _editor = TextEditorView(_code, editorHeight: 96f, editorWidth: _maxW - 60);
            _editor.ShowLineNumbers = true;
            _editor.EditorFont = EditorFaces.Value.Mono;
            _editor.LanguageService = lang;
            Func<Theme> th = () => UiTheme.T;
            _editor.Providers.Add(new SyntaxHighlightProvider(Luxel.Highlight.TextMateHighlighter.Instance, "csharp", th));
            _editor.Providers.Add(new DiagnosticsProvider(lang, th));
            _editor.Providers.Add(new CurrentLineProvider(th));
            RunButton = Button(_ => Run(), "▷", variant: Variant.Ghost);
        }

        private void Run()
        {
            ScriptResult r = _host.Run(_code.Value, new ScriptGlobals { Ctx = _ctx });
            (_output as IDisposable)?.Dispose();
            _output = null; _outText = ""; _ok = r.Success;
            if (!r.Success)
                _outText = r.Exception is not null
                    ? $"実行時例外{(r.ExceptionLine is int l ? $" (行 {l})" : "")}: {r.Exception.Message}"
                    : string.Join("\n", r.Diagnostics.Where(d => d.IsError).Select(d => $"行 {d.Line}: {d.Message}"));
            else if (r.ReturnValue is Widget w) _output = w;
            else _outText = r.ReturnValue?.ToString() ?? "(出力なし)";
            _ver.Value++;
        }

        protected override Widget Build()
        {
            _ = _ver.Value;
            var kids = new List<Widget>
            {
                // 実行ガター (▷) + コードエディタ — Jupyter の In[] セル
                HStack(6)[RunButton, _editor],
            };
            // 結果パネル (Out[]) — Widget or テキスト
            if (_output is not null)
                kids.Add(Border(background: Bind.From(() => UiTheme.T.Surface), rounded: 4,
                                padding: new Thickness(10), margin: new Thickness(30, 0, 0, 0), width: _maxW - 30)[_output]);
            else if (_outText.Length > 0)
                kids.Add(Text(_outText, 12, color: _ok ? Tw.Green600 : Tw.Red600, margin: new Thickness(30, 0, 0, 0)));
            return VStack(6)[kids.ToArray()];
        }

        public override string? DebugDetail => $"cell ({_code.Value.Length} 文字)";
        public void Dispose() => (_output as IDisposable)?.Dispose();
    }

    [Story("Examples/Scripting/Notebook", Height = 620, Order = 2034)]
    public static Widget Notebook(StoryContext ctx, ScriptHost host, ICodeLanguage lang)
    {
        // resolver は再描画毎に呼ばれる — 本文キーでキャッシュし**同一インスタンス**を返す
        // (状態=出力が再描画で消えない + play が安定して掴める)
        var cellByBody = new Dictionary<string, NotebookCell>();
        var cells = new List<NotebookCell>();
        NotebookCell CellFor(string body)
        {
            if (!cellByBody.TryGetValue(body, out NotebookCell? cell))
            {
                cell = new NotebookCell(body, 560f, ctx, host, lang);
                cellByBody[body] = cell;
                cells.Add(cell);
            }
            return cell;
        }

        Signal<string> md = ctx.Signal("markdown",
            "# Luxel ノートブック\n" +
            "**文章 + 実行可能なコードセル** の Jupyter 風です。各セルの ▷ で実行、結果が下に出ます。\n\n" +
            "## 数値を返すセル\n" +
            "```csx\nEnumerable.Range(1, 10).Sum(i => i * i)\n```\n" +
            "## Widget を返すセル\n" +
            "セルは最後の式が `Widget` ならその場に描画されます:\n" +
            "```csx\nHStack(6)[\n    Badge(\"Ready\", Intent.Success),\n    Button(_ => Log(\"cell!\"), \"押す\")]\n```\n" +
            "文章とコードが交互に並ぶのが notebook 体験です。\n");

        // 新スタック: markdown 文書レンダラ + ```csx フェンスを embed 扱いし NotebookCell に解決
        TextEditorView doc = MarkdownDoc.Create(md, () => UiTheme.T, width: 600f, height: 520f,
            bold: EditorFaces.Value.Bold, mono: EditorFaces.Value.Mono,
            highlighter: Luxel.Highlight.TextMateHighlighter.Instance,
            embedKinds: new[] { "csx" }, fonts: StoryKit.JpFallback.Value);
        doc.WidgetResolver = key => key is EmbedRef { Key: "csx" } r ? CellFor(r.Body) : null;

        ctx.Play(async d =>
        {
            await d.Snap();                          // 未実行 (セルは空)
            await d.Click(cells[0].RunButton);       // 埋め込みセルの ▷ — バブリング + 正しい WorldPos で届く
            await d.Step(4);
            await d.Snap("ran");                     // 結果パネルに出力が出た絵 (Jupyter の Out[])
            await d.Expect(() => cells[0].HasOutput, "埋め込みセルの Run にクリックが届く");
        });

        return Border(background: Bind.From(() => UiTheme.T.Background), padding: new Thickness(20))[doc];
    }
}
