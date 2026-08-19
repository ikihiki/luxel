using Luxel.Controls;
using Luxel.UI;
using Luxel.Workbench;

namespace Luxel.Tests;

public sealed class WelcomeProjectTests
{
    [Fact]
    public void NewProjectValidationAndRecentProjectsAreOrderedAndUnique()
    {
        var store = new MemoryEditorSettingsStore();
        var projects = new EditorProjectService(store);
        Assert.False(projects.Validate(new("", "", "missing")).IsValid);

        Assert.True(projects.TryCreate(new("Game", "projects", "empty"), out string? created));
        projects.Remember("other");
        projects.Remember(created!);

        Assert.Equal(created, projects.RecentProjects[0]);
        Assert.Equal(2, projects.RecentProjects.Count);
        projects.RemoveRecent("other");
        Assert.DoesNotContain("other", projects.RecentProjects);
    }

    [Fact]
    public void OpenFailurePreservesCurrentSessionAndReportsWelcomeError()
    {
        var host = new Host(new FailingBackend());
        using var app = new EditorApplication(host, files => new EditorSession(files,
            new Dictionary<string, IEditorDocument> { ["doc"] = new Doc() }, DockTree.Single("doc")));
        Assert.False(app.OpenProject("broken"));
        Assert.Null(app.Session);
        Assert.Contains("cannot open", app.WelcomeError.Value);
    }

    [Fact]
    public void ApplicationShellChoosesWelcomeUntilProjectIsOpen()
    {
        var host = new Host(PassthroughEditorProjectBackend.Instance);
        using var app = new EditorApplication(host, files => new EditorSession(files,
            new Dictionary<string, IEditorDocument> { ["doc"] = new Doc() }, DockTree.Single("doc")));
        Assert.IsType<EditorApplicationShell>(new EditorApplicationShell(app));
        Assert.True(app.OpenProject("sample"));
        Assert.NotNull(app.Session);
    }

    [Fact]
    public void DirtySessionBlocksProjectReplacementWithoutChangingApplicationState()
    {
        var backend = new RecordingBackend();
        var host = new Host(backend);
        int sessions = 0;
        using var app = new EditorApplication(host, files =>
        {
            sessions++;
            return new EditorSession(files,
                new Dictionary<string, IEditorDocument> { ["doc"] = new Doc() }, DockTree.Single("doc"));
        });
        Assert.True(app.OpenProject("first"));
        EditorSession original = app.Session!;
        original.OpenDocuments["doc"].Dirty.Value = true;

        Assert.False(app.OpenProject("second"));

        Assert.Same(original, app.Session);
        Assert.Equal("first", app.ProjectId);
        Assert.Equal(1, sessions);
        Assert.Equal(["first"], backend.Opened);
        Assert.Contains("dirty", app.WelcomeError.Value!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DirtySessionBlocksProjectCreationBeforeBackendSideEffects()
    {
        var backend = new RecordingBackend();
        var host = new Host(backend);
        using var app = new EditorApplication(host, files => new EditorSession(files,
            new Dictionary<string, IEditorDocument> { ["doc"] = new Doc() }, DockTree.Single("doc")));
        Assert.True(app.OpenProject("first"));
        app.Session!.OpenDocuments["doc"].Dirty.Value = true;

        Assert.False(app.CreateProject(new("New", "projects", "empty")));

        Assert.Equal(0, backend.Created);
        Assert.Equal("first", app.ProjectId);
        Assert.NotNull(app.Session);
    }

    private sealed class FailingBackend : IEditorProjectBackend
    {
        public IReadOnlyList<EditorProjectTemplate> Templates => [new("empty", "Empty", "")];
        public string Create(NewProjectRequest request) => throw new IOException("cannot create");
        public string Open(string projectId) => throw new IOException("cannot open");
    }
    private sealed class RecordingBackend : IEditorProjectBackend
    {
        public IReadOnlyList<EditorProjectTemplate> Templates => [new("empty", "Empty", "")];
        public List<string> Opened { get; } = [];
        public int Created { get; private set; }
        public string Create(NewProjectRequest request) { Created++; return $"{request.Location}/{request.Name}"; }
        public string Open(string projectId) { Opened.Add(projectId); return projectId; }
    }

    private sealed class Host(IEditorProjectBackend backend) : IEditorHost, IProjectPicker, IBuildService
    {
        public IFileStorage Files { get; } = new MemoryFileStorage();
        public IProjectPicker Projects => this;
        public IEditorSettingsStore Settings { get; } = new MemoryEditorSettingsStore();
        public IBuildService Builds => this;
        public IHostCapabilities Capabilities { get; } = new EditorHostCapabilities(false);
        public IEditorProjectBackend ProjectBackend => backend;
        public bool IsAvailable => false;
        public string? PickProject() => null;
        public void Build() { }
    }
    private sealed class Doc : IEditorDocument
    {
        public string Kind => "fake"; public string Title => "doc"; public Signal<bool> Dirty { get; } = new(false);
        public bool CanUndo => false; public bool CanRedo => false; public Widget CreateView() => new Spacer();
        public void Undo() { } public void Redo() { } public string Serialize() => ""; public void LoadFrom(string content) { }
    }
}
