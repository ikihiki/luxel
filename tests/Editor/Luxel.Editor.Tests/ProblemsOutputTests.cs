using Luxel.Controls;
using Luxel.Graphics.TwoD;
using Luxel.Typography;
using Luxel.UI;
using Luxel.Workbench;

namespace Luxel.Tests;

public sealed class ProblemsOutputTests
{
    [Fact]
    public void DiagnosticsReplaceFilterGroupAndNavigate()
    {
        var diagnostics = new EditorDiagnosticsService();
        diagnostics.Add(new("a", EditorDiagnosticSeverity.Warning, "build", "warning", "a.cs", 3, 4, "doc"));
        diagnostics.Add(new("b", EditorDiagnosticSeverity.Error, "save", "failed", "b.cs"));
        diagnostics.ReplaceSource("build", [new("c", EditorDiagnosticSeverity.Error, "build", "compile failed", "a.cs")]);

        Assert.DoesNotContain(diagnostics.Items, x => x.Id == "a");
        Assert.Single(diagnostics.Query(new(EditorDiagnosticSeverity.Error, "build", "compile")));
        Assert.Equal(2, diagnostics.Group(EditorDiagnosticGroup.Source).Count);

        using var session = new EditorSession(new Dictionary<string, IEditorDocument> { ["doc"] = new Doc() }, DockTree.Single("doc"));
        EditorDiagnosticItem item = diagnostics.Items.Single(x => x.Id == "c") with { DocumentId = "doc", Line = 5, Column = 2 };
        int line = 0;
        Assert.True(diagnostics.Navigate(item, session, (_, l, _) => line = l));
        Assert.Equal(5, line);
    }

    [Fact]
    public void OutputSupportsChannelsCopyClearAndStates()
    {
        var output = new EditorOutputService();
        output.Write("Build", "first");
        output.Write("Build", "second", EditorOutputLevel.Warning);
        output.Write("Play", "running");
        output.SelectedChannel.Value = "Build";
        Assert.Equal(2, output.Current().Count);
        Assert.Contains("second", output.Copy("Build"));
        output.Clear("Build");
        Assert.Empty(output.Current());
        output.SetError("boom");
        Assert.Equal(EditorOutputState.Error, output.State.Value);
        Assert.Contains(output.Entries, x => x.Level == EditorOutputLevel.Error);
    }

    [Fact]
    public void SessionFailuresReachProblemsAndOutput()
    {
        using var session = new EditorSession(new Dictionary<string, IEditorDocument> { ["doc"] = new Doc() }, DockTree.Single("doc"));
        session.ReportFailure("layout", new InvalidOperationException("bad layout"));
        Assert.Contains(session.DiagnosticsService.Items, x => x.Source == "layout");
        Assert.Contains(session.OutputService.Entries, x => x.Channel == "layout");
    }

    [Fact]
    public void ProductionProblemsViewRevealsLocationsAndOutputChannelSelectionIsImmediate()
    {
        var reveal = new RevealDoc();
        using var session = new EditorSession(new Dictionary<string, IEditorDocument> { ["doc"] = reveal }, DockTree.Single("doc"));
        var problems = new ProblemsView(session.DiagnosticsService, session);
        var item = new EditorDiagnosticItem("p", EditorDiagnosticSeverity.Error, "build", "bad", DocumentId: "doc", Line: 7, Column: 3);
        session.DiagnosticsService.Add(item);

        Assert.True(problems.Navigate(item));
        Assert.Equal((7, 3), reveal.LastReveal);

        session.OutputService.Write("Build", "compile");
        session.OutputService.Write("Play", "running");
        var output = new OutputView(session.OutputService);
        session.OutputService.SelectedChannel.Value = "Play";
        Assert.Equal(["running"], session.OutputService.Current().Select(x => x.Message));
        Assert.Equal("running", output.CopyCurrent());
    }

    [Fact]
    public void ProblemsProductionGroupingAndFilteringAreStableAndCaseInsensitive()
    {
        var diagnostics = new EditorDiagnosticsService();
        diagnostics.Add(new("warning", EditorDiagnosticSeverity.Warning, "Build", "Unused value", "src/B.cs", 8, 2));
        diagnostics.Add(new("error-b", EditorDiagnosticSeverity.Error, "build", "Compile failed", "src/B.cs", 3, 1));
        diagnostics.Add(new("error-a", EditorDiagnosticSeverity.Error, "Analyzer", "Compile failed", "src/A.cs", 2, 4));
        diagnostics.Add(new("info", EditorDiagnosticSeverity.Info, "Play", "Started", null));

        Assert.Equal(["error-a", "error-b", "warning", "info"], diagnostics.Query().Select(x => x.Id));
        Assert.Equal(["error-b", "warning"], diagnostics.Query(new(Source: "BUILD")).Select(x => x.Id));
        Assert.Equal(["error-a", "error-b"], diagnostics.Query(new(Text: "compile")).Select(x => x.Id));
        Assert.Equal(["Error", "Warning", "Info"], diagnostics.Grouped(EditorDiagnosticGroup.Severity).Select(x => x.Key));
        Assert.Equal(["Analyzer", "build", "Play"], diagnostics.Grouped(EditorDiagnosticGroup.Source).Select(x => x.Key));

        var view = new ProblemsView(diagnostics);
        view.SetGrouping(EditorDiagnosticGroup.Path);
        view.SetFilter(new(EditorDiagnosticSeverity.Warning, Text: "src"));
        using VectorFont font = VectorFont.LoadSystem();
        using var host = new UiHost(new RetainedCanvas(), font, 800, 600);
        host.SetRoot(view);
        Assert.Equal(EditorDiagnosticGroup.Path, view.Grouping);
        Assert.Equal(EditorDiagnosticSeverity.Warning, view.Filter.MinimumSeverity);
    }

    [Fact]
    public void OutputCopiesThroughClipboardAndAutoScrollTracksRealLayout()
    {
        var service = new EditorOutputService();
        for (int i = 0; i < 30; i++) service.Write("Build", $"line {i}");
        service.SelectChannel("build");
        var clipboard = new FakeClipboard();
        var view = new OutputView(service, clipboard);
        using VectorFont font = VectorFont.LoadSystem();
        using var host = new UiHost(new RetainedCanvas(), font, 800, 600);

        host.SetRoot(view);
        Assert.True(view.MaxScroll > 0);
        Assert.Equal(view.MaxScroll, view.ScrollOffset);
        Assert.Equal(string.Join(Environment.NewLine, Enumerable.Range(0, 30).Select(i => $"line {i}")), view.CopyCurrent());
        Assert.Equal(view.CopiedText.Value, clipboard.Text);

        view.ScrollTo(0);
        Assert.False(service.AutoScroll.Value);
        service.Write("Build", "not forced to end");
        host.SetRoot(view);
        Assert.Equal(0, view.ScrollOffset);

        service.AutoScroll.Value = true;
        host.SetRoot(view);
        Assert.Equal(view.MaxScroll, view.ScrollOffset);
        service.BeginLoading();
        Assert.Equal(EditorOutputState.Loading, service.State.Value);
        service.SetReady();
        Assert.Equal(EditorOutputState.Ready, service.State.Value);
    }

    [Fact]
    public void OutputClipboardFailureIsReportedWithoutLosingCopiedText()
    {
        var service = new EditorOutputService();
        service.Write("General", "copy me");
        var view = new OutputView(service, new ThrowingClipboard());

        Assert.Equal("copy me", view.CopyCurrent());
        Assert.Contains("clipboard failed", view.ActionError.Value);
    }

    private sealed class FakeClipboard : IEditorClipboard
    {
        public string? Text { get; private set; }
        public void SetText(string text) => Text = text;
    }

    private sealed class ThrowingClipboard : IEditorClipboard
    {
        public void SetText(string text) => throw new InvalidOperationException("clipboard failed");
    }

    private sealed class RevealDoc : IEditorDocument, IEditorLocationReveal
    {
        public (int Line, int Column) LastReveal { get; private set; }
        public string Kind => "fake"; public string Title => "doc"; public Signal<bool> Dirty { get; } = new(false);
        public bool CanUndo => false; public bool CanRedo => false; public Widget CreateView() => new Spacer();
        public void Undo() { } public void Redo() { } public string Serialize() => ""; public void LoadFrom(string content) { }
        public void Reveal(int line, int column) => LastReveal = (line, column);
    }

    private sealed class Doc : IEditorDocument
    {
        public string Kind => "fake"; public string Title => "doc"; public Signal<bool> Dirty { get; } = new(false);
        public bool CanUndo => false; public bool CanRedo => false; public Widget CreateView() => new Spacer();
        public void Undo() { } public void Redo() { } public string Serialize() => ""; public void LoadFrom(string content) { }
    }
}
