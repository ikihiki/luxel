using System.Runtime.InteropServices.JavaScript;
using System.Text.Json;
using Luxel.Controls;
using Luxel.Workbench;

namespace Luxel.Editor.Browser;

public sealed class BrowserProjectPicker(Func<string?> pick) : IProjectPicker
{
    public const string BuiltInDemo = "builtin:demo";
    public const string IndexedDbWorkspace = "indexeddb:default";
    public string? PickProject() => pick();
}

public sealed class BrowserSettingsStore(Func<string, string?> read, Action<string, string> write) : IEditorSettingsStore
{
    private readonly Dictionary<string, string> _fallback = new(StringComparer.Ordinal);
    public bool IsPersistent { get; private set; } = true;
    public string? Read(string key)
    {
        if (!IsPersistent) return _fallback.GetValueOrDefault(key);
        try { return read(key); } catch { IsPersistent = false; return _fallback.GetValueOrDefault(key); }
    }
    public void Write(string key, string value)
    {
        _fallback[key] = value;
        if (!IsPersistent) return;
        try { write(key, value); } catch { IsPersistent = false; }
    }
}

public sealed class UnsupportedBrowserBuildService : IBuildService
{
    public bool IsAvailable => false;
    public void Build() => throw new NotSupportedException("Build processes are unavailable in the browser host. Export the project and build it with the native host or CLI.");
}

public sealed class BrowserProjectStorageProvider : IEditorProjectStorageProvider, IEditorProjectBackend
{
    private sealed record Registration(Func<IFileStorage> CreateStorage, string? SourceId);
    private readonly Dictionary<string, Registration> _projects = new(StringComparer.Ordinal);
    private int _sequence;

    public IReadOnlyList<EditorProjectTemplate> Templates { get; } = [];
    public string? ActiveProjectId { get; private set; }
    public string? ActiveSourceId { get; private set; }
    public IFileStorage? ActiveStorage { get; private set; }
    public event Action<string, IFileStorage>? Activated;

    public void Register(string projectId, Func<IFileStorage> createStorage, string? sourceId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);
        _projects[projectId] = new(createStorage ?? throw new ArgumentNullException(nameof(createStorage)), sourceId);
    }

    public string RegisterSnapshot(string kind, string name, IReadOnlyDictionary<string, string> files, string? sourceId = null)
    {
        string safeName = string.Concat(name.Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '-')).Trim('-');
        string id = $"{kind}:{(safeName.Length == 0 ? "project" : safeName)}:{Interlocked.Increment(ref _sequence)}";
        Dictionary<string, string> snapshot = new(files, StringComparer.Ordinal);
        Register(id, () => CreateMemory(snapshot), sourceId);
        return id;
    }

    public string Open(string projectId)
        => _projects.ContainsKey(projectId) ? projectId : throw new DirectoryNotFoundException($"Browser project '{projectId}' is not available in this session.");
    public string Create(NewProjectRequest request) => throw new NotSupportedException("Create Project is not available in the browser host; open the demo, IndexedDB workspace, a folder, or an archive.");
    public IFileStorage CreateStorage(string projectId) => _projects.TryGetValue(projectId, out Registration? registration)
        ? registration.CreateStorage() : throw new DirectoryNotFoundException($"Browser project '{projectId}' is not registered.");
    public void ProjectActivated(string projectId, IFileStorage storage)
    {
        if (!_projects.TryGetValue(projectId, out Registration? registration))
            throw new DirectoryNotFoundException($"Browser project '{projectId}' is not registered.");
        string? previousId = ActiveProjectId;
        string? previousSourceId = ActiveSourceId;
        IFileStorage? previousStorage = ActiveStorage;
        ActiveProjectId = projectId;
        ActiveSourceId = registration.SourceId;
        ActiveStorage = storage;
        try { Activated?.Invoke(projectId, storage); }
        catch
        {
            ActiveProjectId = previousId;
            ActiveSourceId = previousSourceId;
            ActiveStorage = previousStorage;
            throw;
        }
    }

    public void SetActiveSource(string sourceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        if (ActiveProjectId is null || !_projects.TryGetValue(ActiveProjectId, out Registration? registration))
            throw new InvalidOperationException("No browser project is active.");
        _projects[ActiveProjectId] = registration with { SourceId = sourceId };
        ActiveSourceId = sourceId;
    }

    private static MemoryFileStorage CreateMemory(IReadOnlyDictionary<string, string> files)
    {
        var storage = new MemoryFileStorage();
        foreach ((string path, string content) in files) storage.Write(path, content);
        return storage;
    }
}

public sealed record BrowserProjectPayload(string Name, Dictionary<string, string> Files, string? SourceId = null);

[System.Runtime.Versioning.SupportedOSPlatform("browser")]
public sealed partial class BrowserJsServices
{
    public string? PickProject() => PickProjectCore();
    public string? ReadSetting(string key) => ReadSettingCore(key);
    public void WriteSetting(string key, string value) => WriteSettingCore(key, value);
    public bool FileSystemAccessAvailable => HasFileSystemAccessCore();
    public Task<string> ImportArchiveAsync() => ImportArchiveCore();
    public Task ExportArchiveAsync(string workspace, string filesJson) => ExportArchiveCore(workspace, filesJson);
    public Task<string> OpenFileSystemFolderAsync() => OpenFileSystemFolderCore();
    public Task<string> SaveFileSystemFolderAsync(string? sourceId, string filesJson) => SaveFileSystemFolderCore(sourceId, filesJson);
    public void SetDirty(bool dirty) => SetDirtyCore(dirty);
    public void SetFailure(string message) => SetFailureCore(message);
    [JSImport("pickProject", "luxel-editor-storage")] private static partial string? PickProjectCore();
    [JSImport("readSetting", "luxel-editor-storage")] private static partial string? ReadSettingCore(string key);
    [JSImport("writeSetting", "luxel-editor-storage")] private static partial void WriteSettingCore(string key, string value);
    [JSImport("hasFileSystemAccess", "luxel-editor-storage")] private static partial bool HasFileSystemAccessCore();
    [JSImport("importArchive", "luxel-editor-storage")] private static partial Task<string> ImportArchiveCore();
    [JSImport("exportArchive", "luxel-editor-storage")] private static partial Task ExportArchiveCore(string workspace, string filesJson);
    [JSImport("openFileSystemFolder", "luxel-editor-storage")] private static partial Task<string> OpenFileSystemFolderCore();
    [JSImport("saveFileSystemFolder", "luxel-editor-storage")] private static partial Task<string> SaveFileSystemFolderCore(string? sourceId, string filesJson);
    [JSImport("setDirty", "luxel-editor-storage")] private static partial void SetDirtyCore(bool dirty);
    [JSImport("setFailure", "luxel-editor-host")] private static partial void SetFailureCore(string message);
}

[System.Runtime.Versioning.SupportedOSPlatform("browser")]
public sealed class BrowserProjectCoordinator(BrowserProjectStorageProvider projects, BrowserJsServices js)
{
    private EditorApplication? _application;

    public bool RequiresUnloadWarning
        => _application?.Session?.Workspace.AnyDirty.Value == true
           || projects.ActiveStorage is BrowserWorkspaceStorage storage && storage.State.RequiresUnloadWarning;

    public void Attach(EditorApplication application) => _application = application;

    public void AttachIndexedDb(BrowserWorkspaceStorage storage)
    {
        storage.StateChanged += state =>
        {
            if (ReferenceEquals(projects.ActiveStorage, storage)) ApplyStorageStatus(state);
        };
        storage.PersistenceError += error => _application?.Session?.OutputService.Write("Storage", error.Message, EditorOutputLevel.Error);
    }

    public void ConfigureSession(EditorSession session)
    {
        session.Commands.Register("project.browser.demo", "Open Built-in Demo", OpenDemo, menuPath: "File/Projects/Open Built-in Demo");
        session.Commands.Register("project.browser.indexeddb", "Open IndexedDB Workspace", OpenIndexedDb, menuPath: "File/Projects/Open IndexedDB Workspace");
        session.Commands.Register("project.browser.importArchive", "Import Project Archive…", () => Run(ImportArchiveAsync), menuPath: "File/Projects/Import Archive");
        session.Commands.Register("project.browser.exportArchive", "Export Project Archive…", () => Run(ExportArchiveAsync), menuPath: "File/Projects/Export Archive");
        bool fsa = js.FileSystemAccessAvailable;
        session.Commands.Register("project.browser.openFolder",
            fsa ? "Open Browser Folder…" : "Open Browser Folder (unsupported — use Import Archive)",
            () => Run(OpenFolderAsync), enabled: () => fsa, menuPath: "File/Projects/Open Folder");
        session.Commands.Register("project.browser.saveFolder",
            fsa ? "Save Project to Browser Folder…" : "Save to Browser Folder (unsupported — use Export Archive)",
            () => Run(SaveFolderAsync), enabled: () => fsa, menuPath: "File/Projects/Save Folder");
    }

    public void ProjectActivated(string projectId, IFileStorage storage)
    {
        if (storage is BrowserWorkspaceStorage persistent) ApplyStorageStatus(persistent.State);
        else if (_application?.Session is { } session)
            session.StatusText.Value = projectId.StartsWith("builtin:", StringComparison.Ordinal)
                ? "Built-in demo — temporary until exported"
                : "Imported project — temporary until exported or saved to a folder";
    }

    public void OpenDemo() => Open(BrowserProjectPicker.BuiltInDemo);
    public void OpenIndexedDb() => Open(BrowserProjectPicker.IndexedDbWorkspace);
    public void ImportArchive() => Run(ImportArchiveAsync);
    public void OpenFolder() => Run(OpenFolderAsync);

    private void Open(string id)
    {
        if (_application?.OpenProject(id) != true && _application?.Session is { } session)
            session.StatusText.Value = _application.WelcomeError.Peek() ?? $"Could not open {id}.";
    }

    private async Task ImportArchiveAsync()
    {
        BrowserProjectPayload payload = ParsePayload(await js.ImportArchiveAsync(), "archive");
        string id = projects.RegisterSnapshot("archive", payload.Name, payload.Files);
        Open(id);
    }

    private async Task OpenFolderAsync()
    {
        if (!js.FileSystemAccessAvailable) throw new NotSupportedException("File System Access API is unavailable; use Import Project Archive.");
        BrowserProjectPayload payload = ParsePayload(await js.OpenFileSystemFolderAsync(), "folder");
        if (string.IsNullOrWhiteSpace(payload.SourceId))
            throw new InvalidDataException("The selected browser folder did not provide a source identity.");
        string id = projects.RegisterSnapshot("fsa", payload.Name, payload.Files, payload.SourceId);
        Open(id);
    }

    private async Task ExportArchiveAsync()
    {
        IFileStorage storage = projects.ActiveStorage ?? throw new InvalidOperationException("No project is open.");
        await js.ExportArchiveAsync(projects.ActiveProjectId ?? "project", SerializeFiles(storage));
        if (_application?.Session is { } session) session.StatusText.Value = "Project archive exported";
    }

    private async Task SaveFolderAsync()
    {
        if (!js.FileSystemAccessAvailable) throw new NotSupportedException("File System Access API is unavailable; use Export Project Archive.");
        IFileStorage storage = projects.ActiveStorage ?? throw new InvalidOperationException("No project is open.");
        string sourceId = await js.SaveFileSystemFolderAsync(projects.ActiveSourceId, SerializeFiles(storage));
        projects.SetActiveSource(sourceId);
        if (_application?.Session is { } session) session.StatusText.Value = "Project replaced in browser folder";
    }

    private void ApplyStorageStatus(BrowserStorageState state)
    {
        if (_application?.Session is not { } session) return;
        session.StatusText.Value = state.StatusText;
        if (state.Durability == BrowserStorageDurability.Temporary)
            session.OutputService.Write("Storage", state.StatusText, EditorOutputLevel.Warning);
    }

    private static BrowserProjectPayload ParsePayload(string json, string fallbackName)
    {
        BrowserProjectPayload? payload = JsonSerializer.Deserialize<BrowserProjectPayload>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (payload?.Files is null) throw new InvalidDataException("The selected project payload is invalid.");
        return payload with { Name = string.IsNullOrWhiteSpace(payload.Name) ? fallbackName : payload.Name };
    }

    private static string SerializeFiles(IFileStorage storage)
        => JsonSerializer.Serialize(storage.List().ToDictionary(path => path, path => storage.Read(path) ?? "", StringComparer.Ordinal));

    private async void Run(Func<Task> action)
    {
        try { await action(); }
        catch (OperationCanceledException)
        {
            if (_application?.Session is { } session) session.StatusText.Value = "Project operation cancelled";
            else if (_application is { } application) application.WelcomeError.Value = "Project operation cancelled";
        }
        catch (Exception error)
        {
            if (_application?.Session is { } session) session.ReportFailure("browser-project", error);
            else if (_application is { } application) application.WelcomeError.Value = error.Message;
        }
    }
}
