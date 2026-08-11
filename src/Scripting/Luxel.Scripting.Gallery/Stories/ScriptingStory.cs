using Luxel.Controls;
using Luxel.Scripting;
using Luxel.Scripting.Gallery;
using Luxel.Scripting.Roslyn.Web;
using Luxel.Gallery.Playground;
using Luxel.Graphics.TwoD;
using Luxel.UI;
using Luxel.UI.Tailwind;
using static Luxel.Controls.Kit;
using static Luxel.Gallery.Stories.StoryKit;

namespace Luxel.Gallery.Stories;

/// <summary>
/// C# スクリプト — docs に埋め込めるbrowser Roslynライブブロックの実演。
/// エディタで編集 → Run = Roslyn Webコンパイル → 返したWidgetをその場に実体化。
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
        private readonly BrowserRoslynGalleryRuntime _runtime;
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

        public CsxBlock(string initialCode, float maxWidth, StoryContext ctx, BrowserRoslynGalleryRuntime runtime, ICodeLanguage lang)
        {
            _ctx = ctx;
            _runtime = runtime;
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
            RunButton = Button(button => { _ = RunAsync(); }, "Run");
        }

        /// <summary>コードを差し替える (play からエラー例の検証に使う)。</summary>
        internal void SetCode(string code) => _code.Value = code;

        private async Task RunAsync()
        {
            BrowserRoslynRunResult result = await _runtime.RunAsync(_code.Value, _ctx.Log);
            LastRunOk = false;
            if (!result.Success || result.Widget is null)
            {
                _output = null;
                _diags = result.Failure is not null
                    ? $"実行時エラー: {result.Failure.Message}"
                    : string.Join("\n", result.Diagnostics
                        .Where(diagnostic => diagnostic.Severity == WebScriptDiagnosticSeverity.Error)
                        .Select(diagnostic => $"行 {diagnostic.Line}:{diagnostic.Column} {diagnostic.Message}"));
                _status.Value = "✗";
            }
            else
            {
                (_output as IDisposable)?.Dispose();
                _output = result.Widget;
                _diags = "";
                _status.Value = "✓";
                LastRunOk = true;
            }
            _ver.Value++;
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

    [Story(PlaygroundContract.StoryPath, Height = 700, Order = 2031)]
    public static Widget Playground(StoryContext ctx, ICodeLanguage lang, Luxel.Settings.IFileStore files, INativePlaygroundRunner runner)
    {
        var template = new PlaygroundTemplate(
            PlaygroundTemplates.Button.Id,
            PlaygroundTemplates.Button.Title,
            PlaygroundTemplates.Button.Description,
            PlaygroundTemplates.Button.MainFileName,
            [
                new PlaygroundFile(PlaygroundTemplates.Button.MainFileName, """
                    // The entry .csx can reference declarations from supporting .cs documents.
                    return Kit.Button(_ => Log("Button clicked."), PlaygroundLabels.Ready);
                    """),
                new PlaygroundFile("Helpers.cs", """
                    static class PlaygroundLabels
                    {
                        public const string Ready = "Workspace ready";
                    }
                    """),
            ]);
        NativePlaygroundResourceOptions? resourceOptions = null;
        try { resourceOptions = NativePlaygroundResourceOptions.ForGpu(ctx.Device); }
        catch (InvalidOperationException) { /* Headless/export hosts do not expose a GPU context. */ }
        var workspace = new NativePlaygroundWorkspace(template, 620, lang, files, runner, resourceOptions);

        ctx.Play("workspace", async driver =>
        {
            await driver.Expect(() => workspace.FileNames.Count == 2, "native Playground opens a multi-file workspace");
            workspace.AddFile("Temporary.cs");
            await driver.Expect(() => workspace.ActiveFileName == "Temporary.cs", "added file becomes active");
            workspace.RenameActiveFile("Renamed.cs");
            await driver.Expect(() => workspace.ActiveFileName == "Renamed.cs", "active file can be renamed");
            workspace.DeleteActiveFile();
            await driver.Expect(() => workspace.FileNames.Count == 2, "supporting file can be deleted");
            workspace.Activate("Helpers.cs");
            await driver.Expect(() => workspace.ActiveFileName == "Helpers.cs", "supporting file tab activates");
            workspace.Activate(PlaygroundTemplates.Button.MainFileName);
            await driver.Click(workspace.RunButton);
            await driver.Step(4);
            await driver.Expect(() => workspace.LastRunOk, "entry csx compiles with supporting cs and returns a Widget");
            workspace.SetCode(PlaygroundTemplates.Button.MainFileName, "return Missing.Widget;");
            await driver.Click(workspace.RunButton);
            await driver.Step(4);
            await driver.Expect(() => !workspace.LastRunOk && workspace.HasPreview,
                "failed runs retain the last successful preview");
            workspace.SetCode(PlaygroundTemplates.Button.MainFileName, template.Files[0].Source);
        });

        return Border(background: Bind.From(() => UiTheme.T.Background), padding: new Thickness(20))[
            VStack(10)[
                Heading("C# Playground"),
                Muted("A browser-safe multi-file Workbench workspace powered by Luxel.Scripting.Roslyn.Web. Run compiles supporting C# files before the main script."),
                workspace]];
    }

    [Story("Examples/Scripting/LiveCsx", Height = 520, Order = 2032)]
    public static Widget LiveCsx(StoryContext ctx, BrowserRoslynGalleryRuntime runtime, ICodeLanguage lang)
    {
        var block = new CsxBlock(
            "// 最後の式の Widget が下に実体化される。Log(...) は Log タブへ\n" +
            "var names = new[] { \"Luxel\", \"Roslyn\", \"csx\" };\n" +
            "return Kit.VStack(6)[\n" +
            "    Kit.Label($\"こんにちは {string.Join(\" + \", names)}\"),\n" +
            "    Kit.Button(_ => Log(\"クリックされた!\"), \"Click me\")];",
            maxWidth: 440, ctx, runtime, lang);

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


        return Border(background: Bind.From(() => UiTheme.T.Background), padding: new Thickness(20))[
            VStack(10)[
                Heading("C# ライブスクリプト (csx)"),
                Muted("TextEditorViewでガター/ハイライト/診断波線。RunでRoslyn Webが返したWidgetを実体化。"),
                block]];
    }


    // ---- Jupyter 風ノートブック: 文章 + browser Roslynコードセル + 結果 ----

    /// <summary>ノートブックの1コードセル。Web compilerで非同期実行し、成功したWidgetまたは診断を表示する。</summary>
    private sealed class NotebookCell : CompositeControl, IDisposable
    {
        private readonly StoryContext _ctx;
        private readonly BrowserRoslynGalleryRuntime _runtime;
        private readonly Signal<string> _code;
        private readonly Signal<int> _ver = new(0);
        private readonly TextEditorView _editor;
        private readonly float _maxW;
        private Widget? _output;
        private string _outText = "";
        private bool _ok;

        internal Button RunButton { get; }
        internal bool HasOutput => _output is not null || _outText.Length > 0;

        public NotebookCell(string body, float maxWidth, StoryContext ctx, BrowserRoslynGalleryRuntime runtime, ICodeLanguage lang)
        {
            _ctx = ctx;
            _runtime = runtime;
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
            RunButton = Button(button => { _ = RunAsync(); }, "▷", variant: Variant.Ghost);
        }

        private async Task RunAsync()
        {
            BrowserRoslynRunResult result = await _runtime.RunAsync(_code.Value, _ctx.Log);
            (_output as IDisposable)?.Dispose();
            _output = result.Widget;
            _ok = result.Success && result.Widget is not null;
            _outText = _ok ? "" : result.Failure?.Message ?? string.Join("\n", result.Diagnostics
                .Where(diagnostic => diagnostic.Severity == WebScriptDiagnosticSeverity.Error)
                .Select(diagnostic => $"行 {diagnostic.Line}: {diagnostic.Message}"));
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

    private const string SumCellCode =
        "return Kit.Label($\"sum = {Enumerable.Range(1, 10).Sum(i => i * i)}\");";
    private const string WidgetCellCode =
        "return Kit.HStack(6)[Kit.Badge(\"Ready\", Intent.Success), " +
        "Kit.Button(_ => Log(\"cell!\"), \"押す\")];";

    [Story("Examples/Scripting/Notebook", Height = 620, Order = 2034)]
    public static Widget Notebook(StoryContext ctx, BrowserRoslynGalleryRuntime runtime, ICodeLanguage lang)
    {
        var sumCell = new NotebookCell(SumCellCode, 560f, ctx, runtime, lang);
        var widgetCell = new NotebookCell(WidgetCellCode, 560f, ctx, runtime, lang);

        ctx.Play(async driver =>
        {
            await driver.Click(sumCell.RunButton);
            await driver.Step(4);
            await driver.Expect(() => sumCell.HasOutput, "the numeric browser Roslyn cell renders its result");
        });

        return Border(background: Bind.From(() => UiTheme.T.Background), padding: new Thickness(20))[
            VStack(10)[
                Heading("Luxel Notebook"),
                Muted("文章と実行可能コードセルを並べ、各セルをLuxel.Scripting.Roslyn.Webでcompileします。"),
                Heading("数値を返すセル"),
                sumCell,
                Heading("Widgetを返すセル"),
                widgetCell]];
    }
}
