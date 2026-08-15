using Luxel.Controls;
using Luxel.Scripting.Gallery;
using Luxel.Scripting.Roslyn.Web;
using Luxel.UI;
using Luxel.UI.Tailwind;
using static Luxel.Controls.Kit;
using static Luxel.Gallery.Stories.StoryKit;

namespace Luxel.Gallery.Stories;

/// <summary>Browser Roslyn successful-swap demo: failed compilation keeps the last runnable Widget.</summary>
[StoryMeta("Examples/Scripting")]
public static class BrowserScriptHotReloadStory
{
    private const string InitialCode = "return Kit.Badge(\"version 1\", Intent.Success);";

    private sealed class HotReloadBlock : CompositeControl, IDisposable
    {
        private readonly BrowserRoslynGalleryRuntime _runtime;
        private readonly Signal<string> _code = new(InitialCode);
        private readonly Signal<int> _version = new(0);
        private readonly TextEditorView _editor;
        private Widget? _active;
        private string _diagnostics = "";
        private readonly float _width;

        public HotReloadBlock(float width, BrowserRoslynGalleryRuntime runtime, ICodeLanguage language)
        {
            _width = width;
            _runtime = runtime;
            _editor = TextEditorView(_code, editorHeight: 100, editorWidth: width - 92);
            _editor.ShowLineNumbers = true;
            _editor.EditorFont = EditorFaces.Value.Mono;
            _editor.LanguageService = language;
            Func<Theme> theme = () => UiTheme.T;
            _editor.Providers.Add(new SyntaxHighlightProvider(Luxel.Highlight.TextMateHighlighter.Instance, "csharp", theme));
            _editor.Providers.Add(new DiagnosticsProvider(language, theme));
            ApplyButton = Button(button => { _ = ApplyAsync(); }, "Apply");
        }

        internal Button ApplyButton { get; }
        internal bool HasError => _diagnostics.Length > 0;
        internal bool HasActivePreview => _active is not null;
        internal void SetCode(string code) => _code.Value = code;

        private async Task ApplyAsync()
        {
            BrowserRoslynRunResult result = await _runtime.RunAsync(_code.Value);
            if (result.Success && result.Widget is not null)
            {
                (_active as IDisposable)?.Dispose();
                _active = result.Widget;
                _diagnostics = "";
            }
            else
            {
                _diagnostics = result.Failure?.Message ?? string.Join("\n", result.Diagnostics
                    .Where(diagnostic => diagnostic.Severity == WebScriptDiagnosticSeverity.Error)
                    .Select(diagnostic => $"{diagnostic.Line}:{diagnostic.Column} {diagnostic.Message}"));
            }
            _version.Value++;
        }

        protected override Widget Build()
        {
            _ = _version.Value;
            var children = new List<Widget>
            {
                HStack(8)[_editor, ApplyButton],
                Border(background: Bind.From(() => UiTheme.T.SurfaceAlt), rounded: 6,
                    padding: new Thickness(12), width: _width)[
                    _active ?? Muted("Applyでbrowser RoslynがWidgetをcompileします。")],
            };
            if (_diagnostics.Length > 0)
                children.Add(Text($"compile failed; previous preview retained: {_diagnostics}", 11, color: Tw.Red600));
            return VStack(8)[children.ToArray()];
        }

        public void Dispose() => (_active as IDisposable)?.Dispose();
    }

    [Story]
    public static Widget HotReload(StoryContext ctx, BrowserRoslynGalleryRuntime runtime, ICodeLanguage language)
    {
        var block = new HotReloadBlock(520, runtime, language);
        ctx.Play(async driver =>
        {
            block.SetCode("return Kit.Badge(\"version 2\", Intent.Primary);");
            await driver.Click(block.ApplyButton);
            await driver.Step(4);
            await driver.Expect(() => block.HasActivePreview && !block.HasError, "browser Roslyn swaps a successful preview");
            block.SetCode("return Missing.Widget;");
            await driver.Click(block.ApplyButton);
            await driver.Step(4);
            await driver.Expect(() => block.HasActivePreview && block.HasError, "failed compile retains the last successful preview");
        });
        return Border(background: Bind.From(() => UiTheme.T.Background), padding: new Thickness(20))[
            VStack(10)[
                Heading("Browser Roslyn hot reload"),
                Muted("Luxel.Scripting.Roslyn.Web compiles a new Widget and swaps only successful results."),
                block]];
    }
}
