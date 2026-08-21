using System.Numerics;
using System.Text.Json;
using Luxel.Controls;
using Luxel.SceneEdit;
using Luxel.Workbench;

namespace Luxel.Editor.Browser;

public static class BrowserAutomationContract
{
    public const string StateObject = "luxelEditorState";
    public const string InvokeFunction = "luxelEditorAutomation.invoke";
    public static IReadOnlyList<string> Actions { get; } =
    [
        "open-demo", "select-entity", "edit-transform", "undo", "redo", "open-path",
        "edit-active", "save-active", "change-layout", "reset-demo"
    ];
}

public sealed class BrowserDemoSeed
{
    private readonly IReadOnlyDictionary<string, string> _files;

    public BrowserDemoSeed(IReadOnlyDictionary<string, string> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        if (!files.ContainsKey(EditorProductSessionFactory.ProjectFile))
            throw new ArgumentException($"Demo seed must include {EditorProductSessionFactory.ProjectFile}.", nameof(files));
        _files = new Dictionary<string, string>(files, StringComparer.Ordinal);
    }

    public IReadOnlyDictionary<string, string> Files => _files;

    public bool EnsureSeeded(IFileStorage storage)
    {
        ArgumentNullException.ThrowIfNull(storage);
        if (storage.Exists(EditorProductSessionFactory.ProjectFile)) return false;
        Write(storage);
        return true;
    }

    public void Reset(IFileStorage storage)
    {
        ArgumentNullException.ThrowIfNull(storage);
        foreach (string path in storage.List().ToArray()) storage.Delete(path);
        Write(storage);
    }

    private void Write(IFileStorage storage)
    {
        foreach ((string path, string content) in _files.OrderBy(x => x.Key, StringComparer.Ordinal))
            storage.Write(path, content);
    }
}

public interface IBrowserDemoProjectProvider
{
    string ProjectId => BrowserProjectPicker.BuiltInDemo;
    string GalleryUrl => "../../gallery/";
    string StorageDescription => "Temporary built-in demo";
    Task InitializeAsync();
    IFileStorage Storage { get; }
    Task ResetAsync();
    void ConfigureSession(EditorSession session) { }
}

internal sealed class DefaultBrowserDemoProjectProvider : IBrowserDemoProjectProvider
{
    private readonly MemoryFileStorage _storage = new();
    public IFileStorage Storage => _storage;
    public Task InitializeAsync() { EditorProductSessionFactory.SeedIfEmpty(_storage); return Task.CompletedTask; }
    public Task ResetAsync()
    {
        foreach (string path in _storage.List().ToArray()) _storage.Delete(path);
        EditorProductSessionFactory.SeedIfEmpty(_storage);
        return Task.CompletedTask;
    }
}

[System.Runtime.Versioning.SupportedOSPlatform("browser")]
public sealed class BrowserDemoAutomation(
    BrowserProjectStorageProvider projects,
    IBrowserDemoProjectProvider demo)
{
    private EditorApplication? _application;
    private int _resetRevision;

    public void Attach(EditorApplication application) => _application = application;

    public async Task ResetAsync()
    {
        _application?.Session?.SaveAll();
        if (projects.ActiveStorage is BrowserWorkspaceStorage activeStorage)
            await activeStorage.FlushAsync();
        await demo.ResetAsync();
        if (_application?.OpenProject(demo.ProjectId) != true)
            throw new InvalidOperationException(_application?.WelcomeError.Peek() ?? "The demo project could not be reopened after reset.");
        Interlocked.Increment(ref _resetRevision);
    }

    public async Task<string> InvokeAsync(string action, string? value)
    {
        if (action == "reset-demo")
        {
            await ResetAsync();
            return Snapshot();
        }

        string snapshot = Invoke(action, value);
        if (action == "save-active" && projects.ActiveStorage is BrowserWorkspaceStorage activeStorage)
        {
            await activeStorage.FlushAsync();
            snapshot = Snapshot();
        }
        return snapshot;
    }

    public string Invoke(string action, string? value)
    {
        EditorSession session = _application?.Session ?? throw new InvalidOperationException("Editor session is not ready.");
        switch (action)
        {
            case "open-demo": _application?.OpenProject(demo.ProjectId); break;
            case "select-entity":
            {
                int id = int.Parse(value ?? "2", System.Globalization.CultureInfo.InvariantCulture);
                SceneDocument scene = session.SceneDocument ?? throw new InvalidOperationException("Scene document is unavailable.");
                scene.View.SelectEntity(id);
                session.SelectionService.SelectEntities(session.IdOf(scene) ?? "scene", [id], id);
                break;
            }
            case "edit-transform":
            {
                SceneDocument scene = session.SceneDocument ?? throw new InvalidOperationException("Scene document is unavailable.");
                session.ActivateDocument(scene);
                session.Workspace.Activate(scene);
                int id = session.SelectionService.Current.Peek().MainEntityId;
                if (id < 0) id = 2;
                Vector2 old = scene.Doc.Entity(id).Component("transform2d")?.Get("pos")?.AsVec2() ?? Vector2.Zero;
                scene.View.ApplyEdit(new SetField(id, "transform2d", "pos", SceneValue.Of(old + new Vector2(16, 8))));
                break;
            }
            case "undo": session.Commands.Run(EditorCommandIds.Undo); break;
            case "redo": session.Commands.Run(EditorCommandIds.Redo); break;
            case "open-path":
            {
                string path = value ?? EditorProductSessionFactory.ScriptFile;
                if (!session.OpenAsset(path) && session.Documents.DocAt(path) is null)
                    throw new InvalidOperationException($"The demo asset could not be opened: {path}");
                if (session.Documents.DocAt(path) is { } opened) session.Workspace.Activate(opened);
                break;
            }
            case "edit-active":
                if (session.ActiveDocument is TextDocument text)
                {
                    text.Text.Value += value ?? "\n// edited by acceptance test\n";
                    text.Dirty.Value = true;
                }
                else throw new InvalidOperationException("The active document is not text-editable.");
                break;
            case "save-active": session.Save(); break;
            case "change-layout":
            {
                DockTree layout = session.Layout.Peek();
                DockSplit? split = layout.Root as DockSplit;
                if (split is not null)
                {
                    float[] sizes = split.Sizes.Select((_, index) => index == 0 ? 1.4f : 1f).ToArray();
                    layout = layout.WithSizes(split.Id, sizes);
                }
                DockGroup target = layout.Groups.Last();
                layout = layout.MoveTab("script", target.Id);
                session.Layout.Value = layout;
                session.SettingsStore.Write(EditorLayoutService.SettingsKey, layout.Serialize());
                break;
            }
            case "reset-demo": throw new InvalidOperationException("Reset Demo must be invoked through InvokeAsync.");
            default: throw new ArgumentOutOfRangeException(nameof(action), action, "Unknown Editor browser automation action.");
        }
        return Snapshot();
    }

    public string Snapshot()
    {
        EditorSession? session = _application?.Session;
        var documents = session?.OpenDocuments.Select(pair => new
        {
            id = pair.Key,
            title = pair.Value.Title,
            kind = pair.Value.Kind,
            dirty = pair.Value.Dirty.Peek(),
            active = ReferenceEquals(pair.Value, session.ActiveDocument),
            path = session.Documents.BindingOf(pair.Value)?.Path
        }).OrderBy(x => x.id, StringComparer.Ordinal).ToArray() ?? [];
        SceneDocument? scene = session?.SceneDocument;
        int selected = session?.SelectionService.Current.Peek().MainEntityId ?? -1;
        Vector2? position = selected >= 0
            ? scene?.Doc.Entity(selected).Component("transform2d")?.Get("pos")?.AsVec2()
            : null;
        return JsonSerializer.Serialize(new
        {
            contractVersion = 1,
            projectId = _application?.ProjectId ?? "",
            status = session?.StatusText.Peek() ?? _application?.WelcomeError.Peek() ?? "",
            storage = demo.StorageDescription,
            storagePersistent = projects.ActiveStorage is BrowserWorkspaceStorage { State.Persistent: true },
            documents,
            activeText = session?.ActiveDocument is TextDocument activeText ? activeText.Text.Peek() : null,
            selection = new { entityId = selected, sceneSelected = scene?.View.IsSelected(selected) == true },
            inspector = new { entityId = selected, position = position is { } p ? new[] { p.X, p.Y } : null },
            layout = session?.Layout.Peek().Serialize() ?? "",
            files = projects.ActiveStorage?.List().Order(StringComparer.Ordinal).ToArray() ?? [],
            warningCount = session?.DiagnosticsService.Items.Count(x => x.Severity == EditorDiagnosticSeverity.Warning) ?? 0,
            resetRevision = Volatile.Read(ref _resetRevision)
        });
    }
}
