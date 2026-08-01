using Luxel.Controls;
using Luxel.Gallery.Playground;
using Luxel.Scripting;
using Luxel.Settings;
using Luxel.UI;
using Luxel.Workbench;
using static Luxel.Controls.Kit;
using static Luxel.Gallery.Stories.StoryKit;

namespace Luxel.Gallery.Stories;

/// <summary>
/// Native, reusable multi-file Playground surface. Workbench owns document activation/undo while
/// <see cref="NativePlaygroundSession"/> owns schema-v2 persistence independently of the UI.
/// </summary>
internal sealed class NativePlaygroundWorkspace : CompositeControl, IDisposable
{
    private readonly StoryContext _ctx;
    private readonly ScriptHost _host;
    private readonly NativePlaygroundSession _session;
    private readonly Workspace _workspace = new();
    private readonly Dictionary<string, TextDocument> _documents = new(StringComparer.Ordinal);
    private readonly Dictionary<IEditorDocument, Widget> _views = new();
    private readonly List<IDisposable> _effects = [];
    private readonly Signal<int> _version = new(0);
    private readonly Signal<string> _status = new("Ready");
    private readonly float _width;
    private Widget? _output;
    private string _diagnostics = "";

    internal Button RunButton { get; }
    internal IReadOnlyList<string> FileNames => _session.Draft.Files.Select(file => file.FileName).ToArray();
    internal string ActiveFileName => _session.ActiveFileName;
    internal bool LastRunOk { get; private set; }

    public NativePlaygroundWorkspace(
        PlaygroundTemplate template,
        float width,
        StoryContext ctx,
        ScriptHost host,
        ICodeLanguage language,
        IFileStore files)
    {
        _ctx = ctx;
        _host = host;
        _width = MathF.Max(420, width);
        _session = new NativePlaygroundSession(files, template);

        foreach (PlaygroundFile file in _session.Draft.Files)
        {
            string fileName = file.FileName;
            var document = new TextDocument("csharp", fileName, text => CreateEditor(text, language), file.Source);
            _documents.Add(fileName, document);
            _workspace.Open(document);
            Widget view = document.CreateView();
            _views.Add(document, view);
            _effects.Add(Reactive.Effect(() =>
            {
                string source = document.Text.Value;
                if (_session.Draft.Files.First(candidate => candidate.FileName == fileName).Source != source)
                    _session.UpdateFile(fileName, source);
            }));
        }

        Activate(_session.ActiveFileName);
        RunButton = Button(_ => Run(), "Run");
    }

    internal void Activate(string fileName)
    {
        if (!_documents.TryGetValue(fileName, out TextDocument? document)) return;
        _workspace.Activate(document);
        _session.Activate(fileName);
        _version.Value++;
    }

    internal void SetCode(string fileName, string source)
    {
        if (_documents.TryGetValue(fileName, out TextDocument? document)) document.Text.Value = source;
    }

    private TextEditorView CreateEditor(Signal<string> text, ICodeLanguage language)
    {
        TextEditorView editor = TextEditorView(text, editorHeight: 250f, editorWidth: _width - 28);
        editor.ShowLineNumbers = true;
        editor.EditorFont = EditorFaces.Value.Mono;
        editor.LanguageService = language;
        Func<Theme> theme = () => UiTheme.T;
        editor.Providers.Add(new SyntaxHighlightProvider(Luxel.Highlight.TextMateHighlighter.Instance, "csharp", theme));
        editor.Providers.Add(new DiagnosticsProvider(language, theme));
        editor.Providers.Add(new CurrentLineProvider(theme));
        return editor;
    }

    private void Run()
    {
        foreach ((string fileName, TextDocument document) in _documents)
        {
            string source = document.Text.Peek();
            if (_session.Draft.Files.First(file => file.FileName == fileName).Source != source)
                _session.UpdateFile(fileName, source);
        }
        ScriptResult result = _host.Run(_session.CreateExecutionSource(), new ScriptGlobals { Ctx = _ctx });
        LastRunOk = false;
        (_output as IDisposable)?.Dispose();
        _output = null;
        if (!result.Success)
        {
            _diagnostics = result.Exception is not null
                ? $"Runtime error{(result.ExceptionLine is int line ? $" (line {line})" : "")}: {result.Exception.Message}"
                : string.Join("\n", result.Diagnostics.Where(diagnostic => diagnostic.IsError)
                    .Select(diagnostic => $"Line {diagnostic.Line}:{diagnostic.Column} {diagnostic.Message}"));
            _status.Value = "Failed";
        }
        else
        {
            _output = result.ReturnValue as Widget;
            _diagnostics = _output is null && result.ReturnValue is not null
                ? $"The return value is not a Widget: {result.ReturnValue.GetType().Name}"
                : "";
            LastRunOk = _output is not null;
            _status.Value = LastRunOk ? "Succeeded" : "Succeeded (no output)";
        }
        _version.Value++;
    }

    private void Reset()
    {
        _session.Reset();
        foreach (PlaygroundFile file in _session.Draft.Files)
            _documents[file.FileName].LoadFrom(file.Source);
        Activate(_session.ActiveFileName);
        (_output as IDisposable)?.Dispose();
        _output = null;
        _diagnostics = "";
        LastRunOk = false;
        _status.Value = "Ready";
        _version.Value++;
    }

    protected override Widget Build()
    {
        _ = _version.Value;
        IEditorDocument active = _workspace.Active.Peek() ?? _workspace.Documents[0];
        var tabs = new List<Widget>();
        foreach (IEditorDocument document in _workspace.Documents)
        {
            IEditorDocument captured = document;
            tabs.Add(Button(_ => Activate(captured.Title),
                ReferenceEquals(active, document) ? $"● {document.Title}" : document.Title,
                variant: ReferenceEquals(active, document) ? Variant.Tonal : Variant.Ghost));
        }

        Func<string> status = () => _status.Value;
        var children = new List<Widget>
        {
            HStack(4)[tabs.ToArray()],
            _views[active],
            HStack(8)[
                RunButton,
                Button(_ => Reset(), "Reset", variant: Variant.Ghost),
                Text(status, 12, color: Bind.From(() => UiTheme.T.TextMuted))],
        };
        if (_diagnostics.Length > 0)
            children.Add(Text(_diagnostics, 11, color: Luxel.UI.Tailwind.Tw.Red600));
        if (_output is not null)
            children.Add(Border(background: Bind.From(() => UiTheme.T.SurfaceAlt), rounded: 6,
                padding: new Thickness(10), width: _width)[_output]);
        return VStack(6)[children.ToArray()];
    }

    public override string? DebugDetail => $"native playground ({_workspace.Documents.Count} files)";

    public void Dispose()
    {
        foreach (IDisposable effect in _effects) effect.Dispose();
        foreach (IEditorDocument document in _workspace.Documents.ToArray()) _workspace.Close(document);
        (_output as IDisposable)?.Dispose();
    }
}
