using Luxel.UI;
using Luxel.Workbench;

namespace Luxel.Controls;

public interface IHostCapabilities
{
    bool PersistentStorage { get; }
    bool ProjectPicker { get; }
    bool NativeDialogs { get; }
    bool FileWatching { get; }
    bool ProcessBuild { get; }
    bool RevealInFileManager { get; }
}

public readonly record struct EditorHostCapabilities(
    bool PersistentStorage,
    bool ProjectPicker = false,
    bool NativeDialogs = false,
    bool FileWatching = false,
    bool ProcessBuild = false,
    bool RevealInFileManager = false) : IHostCapabilities;

public interface IProjectPicker
{
    string? PickProject();
}

public interface IEditorSettingsStore
{
    string? Read(string key);
    void Write(string key, string value);
}

public interface IBuildService
{
    bool IsAvailable { get; }
    void Build();
}

public interface IEditorHost
{
    IFileStorage Files { get; }
    IProjectPicker Projects { get; }
    IEditorSettingsStore Settings { get; }
    IBuildService Builds { get; }
    IHostCapabilities Capabilities { get; }
}

/// <summary>Non-serializing tool pane exposed through the shared document/docking model.</summary>
public sealed class EditorToolDocument(string kind, string title, Func<Widget> createView) : IEditorDocument
{
    public string Kind { get; } = kind;
    public string Title { get; } = title;
    public Signal<bool> Dirty { get; } = new(false);
    public bool CanUndo => false;
    public bool CanRedo => false;
    public Widget CreateView() => createView();
    public void Undo() { }
    public void Redo() { }
    public string Serialize() => "";
    public void LoadFrom(string content) { }
}

public sealed record EditorDiagnostic(
    string Severity,
    string Source,
    string Message,
    string? Path = null,
    int Line = 0,
    int Column = 0);

public sealed class EditorSession : IDisposable
{
    private readonly Dictionary<string, IEditorDocument> _documents = new(StringComparer.Ordinal);
    private readonly IDisposable _layoutSync;

    public EditorSession(IFileStorage files, IEnumerable<KeyValuePair<string, IEditorDocument>> documents, DockTree layout)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(documents);
        Files = files;
        Layout = new Signal<DockTree>(layout ?? throw new ArgumentNullException(nameof(layout)));
        Documents = new DocumentStore(Workspace, files);

        foreach ((string id, IEditorDocument document) in documents)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(id);
            ArgumentNullException.ThrowIfNull(document);
            if (!_documents.TryAdd(id, document))
                throw new ArgumentException($"Editor document id '{id}' is registered more than once.", nameof(documents));
            Workspace.Open(document);
        }

        _layoutSync = Reactive.Effect(SynchronizeActiveDocument);
    }

    public EditorSession(IEnumerable<KeyValuePair<string, IEditorDocument>> documents, DockTree layout)
        : this(new MemoryFileStorage(), documents, layout) { }

    public IFileStorage Files { get; }
    public Workspace Workspace { get; } = new();
    public DocumentStore Documents { get; }
    public CommandRegistry Commands { get; } = new();
    public Signal<DockTree> Layout { get; }
    public Signal<object?> Selection { get; } = new(null);
    public Signal<IReadOnlyList<EditorDiagnostic>> Diagnostics { get; } = new([]);
    public Signal<string> Output { get; } = new("");
    public Signal<bool> IsPlaying { get; } = new(false);
    public Signal<string> StatusText { get; } = new("Ready");
    public IReadOnlyDictionary<string, IEditorDocument> OpenDocuments => _documents;
    public IEditorDocument? ActiveDocument => Workspace.Active.Peek();

    public DockItem ResolveDockItem(string id)
    {
        if (!_documents.TryGetValue(id, out IEditorDocument? document))
            throw new KeyNotFoundException($"Unknown Editor document id '{id}'.");
        return new DockItem(document.Title, document.CreateView, document.Dirty);
    }

    public bool ActivateDocument(string id)
    {
        if (!_documents.ContainsKey(id)) return false;
        DockTree next = Layout.Peek().ActivateTab(id);
        if (!next.Groups.SelectMany(group => group.Tabs).Contains(id, StringComparer.Ordinal)) return false;
        Layout.Value = next;
        return ReferenceEquals(ActiveDocument, _documents[id]);
    }

    public bool CloseDocument(string id)
    {
        if (!_documents.TryGetValue(id, out IEditorDocument? document)) return false;
        Layout.Value = Layout.Peek().RemoveTab(id);
        _documents.Remove(id);
        return Workspace.Close(document);
    }

    private void SynchronizeActiveDocument()
    {
        DockGroup? group = Layout.Value.Groups.FirstOrDefault();
        if (group is not { Active: >= 0 } || group.Active >= group.Tabs.Count) return;
        if (_documents.TryGetValue(group.Tabs[group.Active], out IEditorDocument? document))
            Workspace.Activate(document);
    }

    public void Dispose()
    {
        _layoutSync.Dispose();
        Documents.Dispose();
        foreach (IEditorDocument document in _documents.Values)
            (document as IDisposable)?.Dispose();
        _documents.Clear();
    }
}

public sealed class EditorApplication : IDisposable
{
    private const string LastProjectKey = "editor.lastProject";
    private readonly Func<IFileStorage, EditorSession> _createSession;

    public EditorApplication(IEditorHost host, Func<IFileStorage, EditorSession> createSession)
    {
        Host = host ?? throw new ArgumentNullException(nameof(host));
        _createSession = createSession ?? throw new ArgumentNullException(nameof(createSession));
    }

    public IEditorHost Host { get; }
    public EditorSession? Session { get; private set; }
    public string? ProjectId { get; private set; }
    public bool ExitRequested { get; private set; }

    public bool Restore()
    {
        string? project = Host.Settings.Read(LastProjectKey);
        return project is not null && OpenProject(project);
    }

    public bool OpenProject(string projectId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);
        CloseProject();
        Session = _createSession(Host.Files);
        ProjectId = projectId;
        Host.Settings.Write(LastProjectKey, projectId);
        return true;
    }

    public bool OpenPickedProject()
        => Host.Projects.PickProject() is { } project && OpenProject(project);

    public void CloseProject()
    {
        Session?.Dispose();
        Session = null;
        ProjectId = null;
    }

    public void RequestExit()
    {
        ExitRequested = true;
        CloseProject();
    }

    public void Dispose() => CloseProject();
}
