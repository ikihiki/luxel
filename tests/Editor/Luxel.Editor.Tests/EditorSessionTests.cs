using Luxel.Controls;
using Luxel.SceneEdit;
using Luxel.UI;
using Luxel.Workbench;

namespace Luxel.Tests;

public sealed class EditorSessionTests
{
    [Fact]
    public void EditorApplicationOwnsHostAndProjectSessionLifecycle()
    {
        var host = new FakeEditorHost();
        int created = 0;
        using var application = new EditorApplication(host, files =>
        {
            created++;
            return new EditorSession(files,
                new Dictionary<string, IEditorDocument> { ["doc"] = new FakeDocument("doc") },
                DockTree.Single("doc"));
        });

        Assert.True(application.OpenProject("sample"));
        Assert.Equal(1, created);
        Assert.Equal("sample", application.ProjectId);
        Assert.Equal("sample", host.Settings.Read("editor.lastProject"));
        Assert.NotNull(application.Session);

        application.RequestExit();
        Assert.False(application.ExitRequested);
        Assert.Equal(EditorCloseScope.Application, application.Session!.CloseCoordinator.Pending.Value?.Scope);
        application.Session.CloseCoordinator.Decide(EditorCloseDecision.Discard);
        Assert.True(application.ExitRequested);
        Assert.Null(application.Session);
    }

    [Fact]
    public void EditorApplicationRestoresTheLastProject()
    {
        var host = new FakeEditorHost();
        host.Settings.Write("editor.lastProject", "restored");
        using var application = new EditorApplication(host, files =>
            new EditorSession(files,
                new Dictionary<string, IEditorDocument> { ["doc"] = new FakeDocument("doc") },
                DockTree.Single("doc")));

        Assert.True(application.Restore());
        Assert.Equal("restored", application.ProjectId);
    }

    [Fact]
    public void EditorApplicationCanOpenAProjectFromTheHostPicker()
    {
        var host = new FakeEditorHost { PickedProject = "picked" };
        using var application = new EditorApplication(host, files =>
            new EditorSession(files,
                new Dictionary<string, IEditorDocument> { ["doc"] = new FakeDocument("doc") },
                DockTree.Single("doc")));

        Assert.True(application.OpenPickedProject());
        Assert.Equal("picked", application.ProjectId);
    }

    [Fact]
    public void CloseProjectDiscardingChangesBypassesDirtyDocumentConfirmation()
    {
        var host = new FakeEditorHost();
        using var application = new EditorApplication(host, files =>
        {
            var document = new FakeDocument("dirty");
            document.Dirty.Value = true;
            return new EditorSession(files,
                new Dictionary<string, IEditorDocument> { ["dirty"] = document }, DockTree.Single("dirty"));
        });
        Assert.True(application.OpenProject("sample"));

        application.CloseProjectDiscardingChanges();

        Assert.Null(application.Session);
        Assert.Null(application.ProjectId);
    }

    [Fact]
    public void RequestExitNeverBypassesDirtyDocumentsWhenCleanConfirmationIsDisabled()
    {
        var host = new FakeEditorHost();
        using var application = new EditorApplication(host, files =>
        {
            var document = new FakeDocument("dirty");
            document.Dirty.Value = true;
            var session = new EditorSession(files,
                new Dictionary<string, IEditorDocument> { ["dirty"] = document }, DockTree.Single("dirty"));
            session.Settings.Apply(session.Settings.Current.Peek() with { ConfirmExit = false });
            return session;
        });
        Assert.True(application.OpenProject("sample"));

        application.RequestExit();

        Assert.False(application.ExitRequested);
        Assert.NotNull(application.Session);
        Assert.Equal(EditorCloseScope.Application, application.Session!.CloseCoordinator.Pending.Value?.Scope);
    }

    [Fact]
    public void CloseProjectNeverBypassesDirtyDocumentsWhenCleanConfirmationIsDisabled()
    {
        var host = new FakeEditorHost();
        using var application = new EditorApplication(host, files =>
        {
            var document = new FakeDocument("dirty");
            document.Dirty.Value = true;
            var session = new EditorSession(files,
                new Dictionary<string, IEditorDocument> { ["dirty"] = document }, DockTree.Single("dirty"));
            session.Settings.Apply(session.Settings.Current.Peek() with { ConfirmExit = false });
            return session;
        });
        Assert.True(application.OpenProject("sample"));

        application.CloseProject();

        Assert.NotNull(application.Session);
        Assert.Equal(EditorCloseScope.Project, application.Session!.CloseCoordinator.Pending.Value?.Scope);
    }

    [Fact]
    public void CleanExitConfirmationCanBeSuppressed()
    {
        var host = new FakeEditorHost();
        using var application = new EditorApplication(host, files =>
        {
            var session = new EditorSession(files,
                new Dictionary<string, IEditorDocument> { ["clean"] = new FakeDocument("clean") }, DockTree.Single("clean"));
            session.Settings.Apply(session.Settings.Current.Peek() with { ConfirmExit = false });
            return session;
        });
        Assert.True(application.OpenProject("sample"));

        application.RequestExit();

        Assert.True(application.ExitRequested);
        Assert.Null(application.Session);
    }

    [Fact]
    public void SelectHierarchyEntitySynchronizesSceneAndEditorSelection()
    {
        SceneDoc source = SceneDoc.Of(SceneSpace.TwoD, [SceneEntity.Of(1, "A"), SceneEntity.Of(2, "B")]);
        var scene = new SceneDocument("scene", source, doc => EditorKit.SceneEditorView(source: doc));
        using var session = new EditorSession(
            new Dictionary<string, IEditorDocument> { ["scene"] = scene },
            DockTree.Single("scene"));

        session.SelectHierarchyEntity(2);

        Assert.Equal(2, session.SelectionService.Current.Peek().MainEntityId);
        Assert.True(scene.View.IsSelected(2));
    }

    [Fact]
    public void LayoutSelectionSynchronizesWorkspaceActiveDocument()
    {
        var first = new FakeDocument("first");
        var second = new FakeDocument("second");
        using var session = new EditorSession(
            new Dictionary<string, IEditorDocument> { ["a"] = first, ["b"] = second },
            DockTree.Single("a", "b"));

        Assert.Same(first, session.ActiveDocument);
        session.Layout.Value = session.Layout.Peek().ActivateTab("b");
        Assert.Same(second, session.ActiveDocument);
    }

    [Fact]
    public void SessionOwnsWorkspaceDocumentStoreCommandsAndSharedState()
    {
        using var session = new EditorSession(
            new Dictionary<string, IEditorDocument> { ["doc"] = new FakeDocument("doc") },
            DockTree.Single("doc"));

        Assert.NotNull(session.Workspace);
        Assert.NotNull(session.Documents);
        Assert.NotNull(session.Commands);
        Assert.NotNull(session.Layout);
#pragma warning disable CS0618
        Assert.IsType<Signal<object?>>(session.Selection);
        Assert.IsType<Signal<IReadOnlyList<EditorDiagnostic>>>(session.Diagnostics);
        Assert.IsType<Signal<string>>(session.Output);
#pragma warning restore CS0618
        Assert.NotNull(session.SelectionService);
        Assert.NotNull(session.DiagnosticsService);
        Assert.NotNull(session.OutputService);
        Assert.False(session.IsPlaying.Value);
    }

    [Fact]
    public void CloseDocumentRemovesItFromLayoutAndWorkspace()
    {
        var first = new FakeDocument("first");
        var second = new FakeDocument("second");
        using var session = new EditorSession(
            new Dictionary<string, IEditorDocument> { ["a"] = first, ["b"] = second },
            DockTree.Single("a", "b"));

        Assert.True(session.CloseDocument("a"));
        Assert.DoesNotContain("a", session.Layout.Peek().Groups.SelectMany(group => group.Tabs));
        Assert.DoesNotContain(first, session.Workspace.Documents);
    }

    [Fact]
    public void DuplicateDocumentIdsAreRejected()
    {
        KeyValuePair<string, IEditorDocument>[] documents =
        [
            new("same", new FakeDocument("first")),
            new("same", new FakeDocument("second")),
        ];

        Assert.Throws<ArgumentException>(() => new EditorSession(documents, DockTree.Single("same")));
    }

    [Fact]
    public void NodeGraphDocumentOwnsHistoryDirtyAndSerializationWithoutAView()
    {
        var graph = new NodeGraphDocument("graph", Luxel.NodeGraph.NodeGraphDoc.Of(
            [new Luxel.NodeGraph.GraphNode(1, "source", "Source", default, [])], []));

        graph.ApplyEdit(new Luxel.NodeGraph.MoveNode(1, new System.Numerics.Vector2(12, 8)));

        Assert.True(graph.Dirty.Value);
        Assert.True(graph.CanUndo);
        Assert.Equal(new System.Numerics.Vector2(12, 8), graph.Doc.Node(1).Pos);

        graph.Undo();
        Assert.Equal(default, graph.Doc.Node(1).Pos);
        Assert.False(graph.Dirty.Value);
        Assert.True(graph.CanRedo);

        graph.Redo();
        string serialized = graph.Serialize();
        Assert.True(graph.Dirty.Value);
        graph.AcceptSavedSnapshot(serialized);
        Assert.False(graph.Dirty.Value);
        Assert.Contains("Source", serialized, StringComparison.Ordinal);
    }

    [Fact]
    public void NodeGraphViewsShareTheDocumentHistory()
    {
        var graph = new NodeGraphDocument("graph", Luxel.NodeGraph.NodeGraphDoc.Of(
            [new Luxel.NodeGraph.GraphNode(1, "source", "Source", default, [])], []));
        var first = Assert.IsType<NodeGraphView>(graph.CreateView());
        var second = Assert.IsType<NodeGraphView>(graph.CreateView());

        first.ApplyEdit(new Luxel.NodeGraph.MoveNode(1, new System.Numerics.Vector2(5, 0)));
        second.Undo();

        Assert.Equal(default, graph.Doc.Node(1).Pos);
        Assert.False(graph.Dirty.Value);
        Assert.True(first.CanRedo);
        Assert.True(second.CanRedo);
    }

    [Fact]
    public void StableEditorIdsAndPortableShellAreSharedContracts()
    {
        using var session = new EditorSession(
            new Dictionary<string, IEditorDocument> { [EditorPaneIds.Documents] = new FakeDocument("doc") },
            DockTree.Single(EditorPaneIds.Documents));

        EditorShell shell = EditorKit.EditorShell(session);

        Assert.Equal("file.save", EditorCommandIds.Save);
        Assert.Equal("scene", EditorPaneIds.Scene);
        Assert.Equal("node-graph", EditorDocumentProviderIds.NodeGraph);
        Assert.NotNull(shell);
    }

    private sealed class FakeEditorHost : IEditorHost, IProjectPicker, IEditorSettingsStore, IBuildService
    {
        private readonly Dictionary<string, string> _settings = new(StringComparer.Ordinal);
        public IFileStorage Files { get; } = new MemoryFileStorage();
        public IProjectPicker Projects => this;
        public IEditorSettingsStore Settings => this;
        public IBuildService Builds => this;
        public IHostCapabilities Capabilities { get; } = new EditorHostCapabilities(PersistentStorage: false);
        public string? PickedProject { get; init; }
        public bool IsAvailable => false;
        public string? PickProject() => PickedProject;
        public string? Read(string key) => _settings.GetValueOrDefault(key);
        public void Write(string key, string value) => _settings[key] = value;
        public void Build() { }
    }

    private sealed class FakeDocument(string title) : IEditorDocument
    {
        public string Kind => "fake";
        public string Title => title;
        public Signal<bool> Dirty { get; } = new(false);
        public bool CanUndo => false;
        public bool CanRedo => false;
        public Widget CreateView() => new Spacer();
        public void Undo() { }
        public void Redo() { }
        public string Serialize() => title;
        public void LoadFrom(string content) { }
    }
}
