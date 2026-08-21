using Luxel.Controls;
using Luxel.Editor.Browser;
using Luxel.Workbench;

namespace Luxel.Editor.Browser.Tests;

public sealed class BrowserHostServiceTests
{
    [Fact]
    public async Task IndexedDbMirrorHydratesPersistsMovesAndWatches()
    {
        var persistence = new Persistence(new Dictionary<string, string> { ["README.md"] = "seed" });
        var storage = new BrowserWorkspaceStorage(persistence, "workspace");
        await storage.InitializeAsync();
        int changed = 0; using IDisposable watch = storage.Watch("README.md", () => changed++);
        storage.Write("README.md", "changed");
        storage.Move("README.md", "Docs/README.md");
        await storage.FlushAsync();
        Assert.Equal(BrowserStorageDurability.Durable, storage.State.Durability);
        Assert.False(storage.State.RequiresUnloadWarning);
        Assert.Equal(2, changed);
        Assert.Equal("changed", storage.Read("Docs/README.md"));
        Assert.Equal("changed", persistence.Files["Docs/README.md"]);
    }

    [Fact]
    public async Task PendingPersistenceKeepsUnloadWarningUntilTheWriteIsDurable()
    {
        var persistence = new ControlledPersistence();
        var storage = new BrowserWorkspaceStorage(persistence, "workspace");
        await storage.InitializeAsync();
        storage.Write("file.txt", "content");
        Assert.Equal(BrowserStorageDurability.Pending, storage.State.Durability);
        Assert.Contains("Saving", storage.State.StatusText);
        Assert.True(storage.State.RequiresUnloadWarning);
        persistence.Complete();
        await storage.FlushAsync();
        Assert.Equal(BrowserStorageDurability.Durable, storage.State.Durability);
        Assert.False(storage.State.RequiresUnloadWarning);
    }

    [Fact]
    public async Task FailedPersistenceKeepsTemporaryStatusAndUnloadWarning()
    {
        var persistence = new ControlledPersistence();
        var storage = new BrowserWorkspaceStorage(persistence, "workspace");
        await storage.InitializeAsync();
        storage.Write("file.txt", "content");
        persistence.Fail(new InvalidOperationException("QuotaExceededError"));
        await storage.FlushAsync();
        Assert.Equal(BrowserStorageDurability.Temporary, storage.State.Durability);
        Assert.Equal(BrowserStorageFailureKind.Quota, storage.State.Failure);
        Assert.True(storage.State.RequiresUnloadWarning);
        Assert.Contains("Temporary session", storage.State.StatusText);
        Assert.Equal("content", storage.Read("file.txt"));
    }

    [Theory]
    [InlineData("QuotaExceededError", BrowserStorageFailureKind.Quota)]
    [InlineData("NotAllowedError permission", BrowserStorageFailureKind.Permission)]
    [InlineData("InvalidStateError unavailable", BrowserStorageFailureKind.Unavailable)]
    public void StorageErrorsHaveDistinctVisibleKinds(string message, BrowserStorageFailureKind expected)
        => Assert.Equal(expected, BrowserWorkspaceStorage.Classify(new InvalidOperationException(message)));

    [Fact]
    public async Task InitializationFailureKeepsMemoryStorageAndTemporaryStatus()
    {
        var storage = new BrowserWorkspaceStorage(new FailingPersistence(), "workspace");
        await storage.InitializeAsync(); storage.Write("file.txt", "local");
        Assert.False(storage.State.Persistent); Assert.Contains("Temporary session", storage.State.StatusText);
        Assert.True(storage.State.RequiresUnloadWarning);
        Assert.Equal("local", storage.Read("file.txt"));
    }

    [Fact]
    public void ProjectIdentitiesAreDistinctAndStorageFactoriesAreIsolated()
    {
        var projects = new BrowserProjectStorageProvider();
        string first = projects.RegisterSnapshot("archive", "Game", new Dictionary<string, string> { ["a"] = "1" });
        string second = projects.RegisterSnapshot("archive", "Game", new Dictionary<string, string> { ["a"] = "2" });
        Assert.NotEqual(first, second);
        IFileStorage firstStorage = projects.CreateStorage(first);
        IFileStorage secondStorage = projects.CreateStorage(second);
        firstStorage.Write("a", "changed");
        Assert.Equal("2", secondStorage.Read("a"));
    }

    [Fact]
    public void FailedProjectReplacementLeavesTheActiveSessionAndStorageUntouched()
    {
        var projects = new BrowserProjectStorageProvider();
        projects.Register("good", () => Storage("good"));
        projects.Register("next", () => Storage("next"));
        projects.Register("bad", () => throw new IOException("cannot open"));
        var host = new Host(projects);
        using var app = new EditorApplication(host, files => new EditorSession(files,
            new Dictionary<string, IEditorDocument> { ["doc"] = new Document(files.Read("id")!) }, DockTree.Single("doc")));
        Assert.True(app.OpenProject("good"));
        EditorSession original = app.Session!;
        IFileStorage originalStorage = projects.ActiveStorage!;
        Assert.False(app.OpenProject("bad"));
        Assert.Same(original, app.Session);
        Assert.Same(originalStorage, projects.ActiveStorage);
        Assert.Equal("good", app.ProjectId);
        Assert.True(app.OpenProject("next"));
        Assert.NotSame(original, app.Session);
        Assert.Equal("next", app.ProjectId);
        Assert.Equal("next", projects.ActiveStorage!.Read("id"));
    }

    [Fact]
    public void SettingsFallbackRetainsValuesWhenBrowserStorageFails()
    {
        var settings = new BrowserSettingsStore(_ => throw new InvalidOperationException(), (_, _) => throw new InvalidOperationException());
        settings.Write("layout", "value");
        Assert.False(settings.IsPersistent); Assert.Equal("value", settings.Read("layout"));
    }

    [Fact]
    public void FailedActivationRollsBackBrowserBackendIdentityStorageAndSource()
    {
        var projects = new BrowserProjectStorageProvider();
        projects.Register("first", () => Storage("first"), "source:first");
        projects.Register("second", () => Storage("second"), "source:second");
        IFileStorage first = projects.CreateStorage("first");
        projects.ProjectActivated("first", first);
        projects.Activated += (id, _) => { if (id == "second") throw new InvalidOperationException("activation failed"); };

        Assert.Throws<InvalidOperationException>(() => projects.ProjectActivated("second", projects.CreateStorage("second")));
        Assert.Equal("first", projects.ActiveProjectId);
        Assert.Equal("source:first", projects.ActiveSourceId);
        Assert.Same(first, projects.ActiveStorage);
    }

    [Fact]
    public void BrowserAssetImportIsExplicitlyDisabledWithARecoveryReason()
    {
        var capabilities = new EditorHostCapabilities(false, AssetImport: false,
            AssetImportUnavailableReason: "Use a project archive.");
        using EditorSession session = EditorProductSessionFactory.Create(new MemoryFileStorage(), capabilities: capabilities);
        Assert.False(session.Assets.ImportCapability.CanExecute);
        Assert.Contains("project archive", session.Assets.ImportCapability.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void StartupFailureIncludesReasonAndFallback()
    {
        string message = BrowserStartupDiagnostics.Describe(new InvalidOperationException("WebGPU adapter unavailable"));
        Assert.Contains("Reason:", message);
        Assert.Contains("WebGPU adapter unavailable", message);
        Assert.Contains("Fallback:", message);
        Assert.Contains("native Editor", message);
    }

    private static MemoryFileStorage Storage(string id) { var storage = new MemoryFileStorage(); storage.Write("id", id); return storage; }

    private sealed class Persistence(Dictionary<string, string> files) : IBrowserWorkspacePersistence
    {
        public Dictionary<string, string> Files { get; } = files;
        public Task<IReadOnlyDictionary<string, string>> LoadAsync(string workspace) => Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>(Files));
        public Task SaveAsync(string workspace, string path, string content) { Files[path] = content; return Task.CompletedTask; }
        public Task DeleteAsync(string workspace, string path) { Files.Remove(path); return Task.CompletedTask; }
    }
    private sealed class ControlledPersistence : IBrowserWorkspacePersistence
    {
        private TaskCompletionSource _write = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<IReadOnlyDictionary<string, string>> LoadAsync(string workspace) => Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>());
        public Task SaveAsync(string workspace, string path, string content) => _write.Task;
        public Task DeleteAsync(string workspace, string path) => _write.Task;
        public void Complete() => _write.TrySetResult();
        public void Fail(Exception error) => _write.TrySetException(error);
    }
    private sealed class FailingPersistence : IBrowserWorkspacePersistence
    {
        public Task<IReadOnlyDictionary<string, string>> LoadAsync(string workspace) => throw new InvalidOperationException("QuotaExceededError");
        public Task SaveAsync(string workspace, string path, string content) => Task.CompletedTask;
        public Task DeleteAsync(string workspace, string path) => Task.CompletedTask;
    }
    private sealed class Host(BrowserProjectStorageProvider projects) : IEditorHost, IProjectPicker, IBuildService
    {
        public IFileStorage Files { get; } = new MemoryFileStorage();
        public IProjectPicker Projects => this;
        public IEditorSettingsStore Settings { get; } = new MemoryEditorSettingsStore();
        public IBuildService Builds => this;
        public IHostCapabilities Capabilities { get; } = new EditorHostCapabilities(false);
        public IEditorProjectStorageProvider ProjectStorage => projects;
        public IEditorProjectBackend ProjectBackend => projects;
        public bool IsAvailable => false;
        public string? PickProject() => null;
        public void Build() { }
    }
    private sealed class Document(string title) : IEditorDocument
    {
        public string Kind => "test"; public string Title => title; public Luxel.UI.Signal<bool> Dirty { get; } = new(false);
        public bool CanUndo => false; public bool CanRedo => false; public Luxel.UI.Widget CreateView() => Kit.Spacer();
        public void Undo() { } public void Redo() { } public string Serialize() => ""; public void LoadFrom(string content) { }
    }
}
