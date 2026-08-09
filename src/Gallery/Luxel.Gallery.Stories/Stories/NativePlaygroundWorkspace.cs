using Luxel.Controls;
using Luxel.Gallery.Playground;
using Luxel.Settings;
using Luxel.Scripting.Roslyn.Web;
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
    private readonly NativePlaygroundSession _session;
    private readonly INativePlaygroundRunner _runner;
    private readonly ICodeLanguage _language;
    private readonly Workspace _workspace = new();
    private readonly Dictionary<string, TextDocument> _documents = new(StringComparer.Ordinal);
    private readonly Dictionary<IEditorDocument, Widget> _views = new();
    private readonly Dictionary<string, IDisposable> _effects = new(StringComparer.Ordinal);
    private readonly Signal<int> _version = new(0);
    private readonly Signal<string> _status = new("Ready");
    private readonly Signal<string> _filePath = new("NewFile.cs");
    private readonly NativePlaygroundRunCoordinator _runs = new();
    private bool _disposed;
    private readonly float _width;
    private Widget? _output;
    private string _diagnostics = "";

    internal Button RunButton { get; }
    internal Button AddFileButton { get; }
    internal Button RenameFileButton { get; }
    internal Button DeleteFileButton { get; }
    internal IReadOnlyList<string> FileNames => _session.Draft.Files.Select(file => file.Path).ToArray();
    internal string ActiveFileName => _session.ActiveFileName;
    internal bool HasPreview => _output is not null;
    internal bool LastRunOk { get; private set; }

    public NativePlaygroundWorkspace(
        PlaygroundTemplate template,
        float width,
        ICodeLanguage language,
        IFileStore files,
        INativePlaygroundRunner? runner = null,
        NativePlaygroundResourceOptions? resourceOptions = null)
    {
        _width = MathF.Max(420, width);
        _language = language;
        _session = new NativePlaygroundSession(files, template, resourceOptions);
        _runner = runner ?? new NativePlaygroundRunner();

        foreach (PlaygroundFile file in _session.Draft.Files) AddDocument(file);

        Activate(_session.ActiveFileName);
        RunButton = Button(_event => { _ = RunAsync(); }, "Run");
        AddFileButton = Button(_ => AddFile(_filePath.Peek()), "Add", variant: Variant.Ghost);
        RenameFileButton = Button(_ => RenameActiveFile(_filePath.Peek()), "Rename", variant: Variant.Ghost);
        DeleteFileButton = Button(_ => DeleteActiveFile(), "Delete", variant: Variant.Ghost);
    }

    internal void Activate(string fileNameOrId)
    {
        PlaygroundFile? file = _session.Draft.Files.SingleOrDefault(candidate =>
            candidate.Id == fileNameOrId || candidate.Path == fileNameOrId);
        if (file is null || !_documents.TryGetValue(file.Id, out TextDocument? document)) return;
        _workspace.Activate(document);
        _session.Activate(file.Id);
        _filePath.Value = file.Path;
        _status.Value = file.Language == "slang" && !_session.SlangLanguage.Capability.IsAvailable
            ? _session.SlangLanguage.Capability.Message
            : "Ready";
        _version.Value++;
    }

    internal void SetCode(string fileNameOrId, string source)
    {
        PlaygroundFile? file = _session.Draft.Files.SingleOrDefault(candidate =>
            candidate.Id == fileNameOrId || candidate.Path == fileNameOrId);
        if (file is not null && _documents.TryGetValue(file.Id, out TextDocument? document))
            document.Text.Value = source;
    }

    internal void AddFile(string path)
    {
        TryMutation(() =>
        {
            PlaygroundFile file = _session.AddFile(path);
            AddDocument(file);
            Activate(file.Id);
        });
    }

    internal void RenameActiveFile(string newPath)
    {
        TryMutation(() =>
        {
            string id = _session.Draft.SelectedFileId;
            _session.RenameFile(id, newPath);
            _documents[id].Title = _session.Draft.SelectedFile.Path;
            _filePath.Value = _session.Draft.SelectedFile.Path;
            _version.Value++;
        });
    }

    internal void DeleteActiveFile()
    {
        TryMutation(() =>
        {
            string id = _session.Draft.SelectedFileId;
            TextDocument document = _documents[id];
            _session.DeleteFile(id);
            _effects.Remove(id, out IDisposable? effect);
            effect?.Dispose();
            _documents.Remove(id);
            _views.Remove(document);
            _workspace.Close(document);
            Activate(_session.Draft.SelectedFileId);
        });
    }

    private void AddDocument(PlaygroundFile file)
    {
        string id = file.Id;
        var document = new TextDocument(file.Language, file.Path, text => CreateEditor(text, file.Language, id), file.Source);
        _documents.Add(id, document);
        _workspace.Open(document);
        _views.Add(document, document.CreateView());
        _effects.Add(id, Reactive.Effect(() =>
        {
            string source = document.Text.Value;
            PlaygroundFile? current = _session.Draft.Files.SingleOrDefault(candidate => candidate.Id == id);
            if (current is not null && current.Source != source)
            {
                _runs.Cancel();
                _session.UpdateFile(id, source);
            }
        }));
    }

    private void TryMutation(Action mutation)
    {
        try
        {
            _runs.Cancel();
            mutation();
            _diagnostics = "";
            _status.Value = "Ready";
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            _diagnostics = exception.Message;
            _status.Value = "Failed";
        }
        _version.Value++;
    }

    private TextEditorView CreateEditor(Signal<string> text, string language, string fileId)
    {
        ICodeLanguage service = language == "slang"
            ? _session.SlangLanguage.ForFile(() => _session.Draft.Files.Single(file => file.Id == fileId).Path)
            : _language;
        TextEditorView editor = TextEditorView(text, editorHeight: 250f, editorWidth: _width - 28);
        editor.ShowLineNumbers = true;
        editor.EditorFont = EditorFaces.Value.Mono;
        editor.LanguageService = service;
        Func<Theme> theme = () => UiTheme.T;
        editor.Providers.Add(new SyntaxHighlightProvider(Luxel.Highlight.TextMateHighlighter.Instance, language, theme));
        editor.Providers.Add(new DiagnosticsProvider(service, theme));
        editor.Providers.Add(new CurrentLineProvider(theme));
        return editor;
    }

    private async Task RunAsync()
    {
        try
        {
            foreach (PlaygroundFile file in _session.Draft.Files.ToArray())
            {
                string source = _documents[file.Id].Text.Peek();
                if (file.Source != source) _session.UpdateFile(file.Id, source);
            }

            _status.Value = "Running";
            _version.Value++;
            await _runs.RunAsync(
                cancellationToken => _runner.RunAsync(_session, cancellationToken),
                PublishRunResult);
        }
        catch (Exception exception) when (!_disposed)
        {
            LastRunOk = false;
            _diagnostics = $"Runtime error: {exception.Message}";
            _status.Value = "Failed";
            _version.Value++;
        }
    }

    private void PublishRunResult(NativePlaygroundRunResult result)
    {
        LastRunOk = result.Success && result.Widget is not null;
        if (!LastRunOk)
        {
            _diagnostics = result.Failure is not null
                ? $"Runtime error{(result.Failure.FileName is { } file ? $" in {file}" : "")}{(result.Failure.Line is int line ? $" (line {line})" : "")}: {result.Failure.Message}"
                : string.Join("\n", result.Diagnostics.Where(diagnostic => diagnostic.Severity == WebScriptDiagnosticSeverity.Error)
                    .Select(diagnostic => $"{diagnostic.FileName ?? _session.Draft.MainFileName}:{diagnostic.Line}:{diagnostic.Column} {diagnostic.Message}"));
            _status.Value = "Failed";
        }
        else
        {
            (_output as IDisposable)?.Dispose();
            _output = result.Widget;
            _diagnostics = "";
            _status.Value = "Succeeded";
        }
        _version.Value++;
    }

    private void Reset()
    {
        _runs.Cancel();
        _session.Reset();
        foreach ((string id, TextDocument document) in _documents.ToArray())
        {
            if (_session.Draft.Files.All(file => file.Id != id))
            {
                _effects.Remove(id, out IDisposable? effect);
                effect?.Dispose();
                _documents.Remove(id);
                _views.Remove(document);
                _workspace.Close(document);
            }
        }
        foreach (PlaygroundFile file in _session.Draft.Files)
        {
            if (_documents.TryGetValue(file.Id, out TextDocument? document))
            {
                document.Title = file.Path;
                document.LoadFrom(file.Source);
            }
            else AddDocument(file);
        }
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
            HStack(6)[
                TextField(_filePath, placeholder: "File.cs", width: 180),
                AddFileButton,
                RenameFileButton,
                DeleteFileButton],
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
        if (_disposed) return;
        _disposed = true;
        _runs.Dispose();
        foreach (IDisposable effect in _effects.Values) effect.Dispose();
        _effects.Clear();
        foreach (IEditorDocument document in _workspace.Documents.ToArray()) _workspace.Close(document);
        (_output as IDisposable)?.Dispose();
        _session.Dispose();
    }
}
