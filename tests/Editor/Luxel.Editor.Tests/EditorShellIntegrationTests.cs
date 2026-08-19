using Luxel.Controls;
using Luxel.SceneEdit;
using Luxel.UI;
using Luxel.Workbench;

namespace Luxel.Tests;

public sealed class EditorShellIntegrationTests
{
    [Fact]
    public void StandardPhase3PanesResolveToProductionViewsAndCommands()
    {
        var scene = new SceneDocument("scene", SceneDoc.Of(SceneSpace.TwoD, [SceneEntity.Of(1, "Root")]),
            doc => EditorKit.SceneEditorView(source: doc));
        using var session = new EditorSession(new Dictionary<string, IEditorDocument> { ["scene-doc"] = scene },
            DockTree.Single("scene-doc"));

        Assert.IsType<EditorDocumentTabs>(session.ResolveDockItem(EditorPaneIds.Documents).CreateView());
        var hierarchy = Assert.IsType<SceneHierarchyView>(session.ResolveDockItem(EditorPaneIds.Hierarchy).CreateView());
        Assert.IsType<SceneInspector>(session.ResolveDockItem(EditorPaneIds.Inspector).CreateView());
        Assert.IsType<AssetBrowser>(session.ResolveDockItem(EditorPaneIds.Assets).CreateView());
        Assert.IsType<ProblemsView>(session.ResolveDockItem(EditorPaneIds.Problems).CreateView());
        Assert.IsType<OutputView>(session.ResolveDockItem(EditorPaneIds.Output).CreateView());
        Assert.IsType<SettingsView>(session.ResolveDockItem(EditorPaneIds.Settings).CreateView());
        Assert.IsType<KeyBindingsView>(session.ResolveDockItem(EditorPaneIds.KeyBindings).CreateView());
        Assert.NotNull(session.Commands.Find($"window.pane.{EditorPaneIds.Hierarchy}"));
        Assert.NotNull(session.Commands.Find($"window.pane.{EditorPaneIds.Settings}"));
        hierarchy.Controller.Dispose();
    }

    [Fact]
    public void StandardPaneCommandsMakeHiddenPanesReachableFromShellLayout()
    {
        using var session = new EditorSession(new Dictionary<string, IEditorDocument> { ["doc"] = new Doc() }, DockTree.Single("doc"));
        Assert.Null(session.Layout.Peek().GroupOf(EditorPaneIds.Output));

        Assert.True(session.Commands.Run($"window.pane.{EditorPaneIds.Output}"));
        Assert.NotNull(session.Layout.Peek().GroupOf(EditorPaneIds.Output));
        Assert.True(session.Commands.Run($"window.pane.{EditorPaneIds.Output}"));
        Assert.Null(session.Layout.Peek().GroupOf(EditorPaneIds.Output));
    }

    [Fact]
    public void FocusModeCommandUsesTheActiveProductionPaneAndRestoresLayout()
    {
        using var session = new EditorSession(new Dictionary<string, IEditorDocument> { ["doc"] = new Doc() },
            DockTree.Single("doc", EditorPaneIds.Output));
        session.Layout.Value = session.Layout.Peek().ActivateTab(EditorPaneIds.Output);

        Assert.True(session.Commands.Run(EditorCommandIds.FocusMode));
        Assert.True(session.LayoutService.IsFocusMode);
        Assert.Equal([EditorPaneIds.Output], session.Layout.Peek().Groups.Single().Tabs);
        Assert.True(session.Commands.Run(EditorCommandIds.FocusMode));
        Assert.False(session.LayoutService.IsFocusMode);
        Assert.NotNull(session.Layout.Peek().GroupOf("doc"));
    }

    [Fact]
    public void SettingsAndKeybindingViewsPerformProductionMutations()
    {
        using var session = new EditorSession(new Dictionary<string, IEditorDocument> { ["doc"] = new Doc() }, DockTree.Single("doc"));
        var settings = Assert.IsType<SettingsView>(session.ResolveDockItem(EditorPaneIds.Settings).CreateView());
        var keys = Assert.IsType<KeyBindingsView>(session.ResolveDockItem(EditorPaneIds.KeyBindings).CreateView());

        Assert.True(settings.Apply(EditorSettings.Defaults with { Theme = EditorThemePreference.Dark, UiScale = 1.2f }));
        Assert.Equal(EditorThemePreference.Dark, session.Settings.Current.Value.Theme);
        Assert.True(keys.Apply(EditorCommandIds.Save, "Ctrl+K"));
        Assert.Equal("Ctrl+K", KeyGestures.Format(session.Commands.EffectiveGesture(EditorCommandIds.Save)!.Value));
    }

    [Fact]
    public void ExternalChangesAreReachableThroughProductionDialogComposition()
    {
        var files = new MemoryFileStorage();
        var doc = new Doc { Content = "local" };
        using var session = new EditorSession(files, new Dictionary<string, IEditorDocument> { ["doc"] = doc }, DockTree.Single("doc"));
        session.Documents.SaveAs(doc, "doc.txt");
        files.Write("doc.txt", "external");
        var dialogs = new EditorDialogs(session);

        Assert.Same(doc, dialogs.ExternalChangeDocument());
        Assert.True(dialogs.ResolveExternalChange(ExternalChangeDecision.Reload).Applied);
        Assert.Equal("external", doc.Content);
        Assert.Null(dialogs.ExternalChangeDocument());
    }

    [Fact]
    public void EditorDialogs_AreRealizedThroughModalOverlayInfrastructure()
    {
        var files = new MemoryFileStorage();
        var doc = new Doc { Content = "local" };
        using var session = new EditorSession(files, new Dictionary<string, IEditorDocument> { ["doc"] = doc }, DockTree.Single("doc"));
        session.Documents.SaveAs(doc, "doc.txt");
        files.Write("doc.txt", "external");
        using var font = Luxel.Typography.VectorFont.LoadSystem();
        using var canvas = new Luxel.Graphics.TwoD.RetainedCanvas();
        using var host = new UiHost(canvas, font, 320, 200);

        host.SetRoot(new EditorDialogs(session));
        int openNodeCount = canvas.Root.Children.Count;
        Assert.True(openNodeCount >= 1);
        Assert.True(host.KeyDown(Key.Escape));
        Assert.True(canvas.Root.Children.Count < openNodeCount);
    }

    [Fact]
    public void ApplicationShellSwitchesReactivelyBetweenActionableWelcomeAndEditor()
    {
        var host = new Host();
        using var app = new EditorApplication(host, files => new EditorSession(files,
            new Dictionary<string, IEditorDocument> { ["doc"] = new Doc() }, DockTree.Single("doc")));
        int sample = 0, gallery = 0;
        var shell = new EditorApplicationShell(app, new(() => sample++, () => gallery++));

        var welcome = Assert.IsType<WelcomeView>(shell.CurrentView());
        Assert.True(welcome.OpenSample());
        Assert.True(welcome.OpenGallery());
        Assert.Equal(1, sample);
        Assert.Equal(1, gallery);
        welcome.ProjectName.Value = "Game";
        welcome.ProjectLocation.Value = "projects";
        Assert.True(welcome.CreateProject());

        Assert.IsType<EditorShell>(shell.CurrentView());
        Assert.NotNull(app.Session);
    }

    [Fact]
    public void ConfirmExitFalseStillCoordinatesDirtyDocuments()
    {
        var host = new Host();
        var dirty = new Doc();
        using var app = new EditorApplication(host, files =>
        {
            var session = new EditorSession(files, new Dictionary<string, IEditorDocument> { ["doc"] = dirty }, DockTree.Single("doc"));
            session.Settings.Apply(EditorSettings.Defaults with { ConfirmExit = false });
            dirty.Dirty.Value = true;
            return session;
        });
        Assert.True(app.OpenProject("sample"));

        app.RequestExit();

        Assert.False(app.ExitRequested);
        Assert.NotNull(app.Session!.CloseCoordinator.Pending.Value);
        app.Session.CloseCoordinator.Decide(EditorCloseDecision.Discard);
        Assert.True(app.ExitRequested);
        Assert.Null(app.Session);
    }

    private sealed class Host : IEditorHost, IProjectPicker, IBuildService
    {
        public IFileStorage Files { get; } = new MemoryFileStorage();
        public IProjectPicker Projects => this;
        public IEditorSettingsStore Settings { get; } = new MemoryEditorSettingsStore();
        public IBuildService Builds => this;
        public IHostCapabilities Capabilities { get; } = new EditorHostCapabilities(false);
        public bool IsAvailable => false;
        public string? PickProject() => "picked";
        public void Build() { }
    }

    private sealed class Doc : IEditorDocument
    {
        private string _saved = "";
        public string Content { get; set; } = "";
        public string Kind => "fake";
        public string Title => "doc";
        public Signal<bool> Dirty { get; } = new(false);
        public bool CanUndo => false;
        public bool CanRedo => false;
        public Widget CreateView() => new Spacer();
        public void Undo() { }
        public void Redo() { }
        public string Serialize() => Content;
        public void AcceptSavedSnapshot(string content) { _saved = content; Dirty.Value = Content != _saved; }
        public void LoadFrom(string content) { Content = _saved = content; Dirty.Value = false; }
    }
}
