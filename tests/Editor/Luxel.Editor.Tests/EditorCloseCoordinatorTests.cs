using Luxel.Controls;
using Luxel.UI;
using Luxel.Workbench;

namespace Luxel.Tests;

public sealed class EditorCloseCoordinatorTests
{
    [Fact]
    public void DirtyDocumentCloseWaitsForDecisionAndSaveAs()
    {
        var doc = new TestDocument("draft") { Content = "changed" };
        doc.Dirty.Value = true;
        var picker = new SavePicker("draft.txt");
        var files = new MemoryFileStorage();
        using var session = new EditorSession(files, new Dictionary<string, IEditorDocument> { ["draft"] = doc },
            DockTree.Single("draft"), savePaths: picker);

        Assert.True(session.CloseDocument("draft"));
        Assert.NotNull(session.CloseCoordinator.Pending.Value);
        Assert.Contains("draft", session.OpenDocuments.Keys);

        session.CloseCoordinator.Decide(EditorCloseDecision.SaveAndContinue);

        Assert.Null(session.CloseCoordinator.Pending.Value);
        Assert.Empty(session.OpenDocuments);
        Assert.Equal("changed", files.Read("draft.txt"));
    }

    [Fact]
    public void ProjectCloseProcessesAllDirtyDocumentsAndStopsOnFailure()
    {
        var first = new TestDocument("first") { Content = "one" };
        var second = new ThrowingDocument("second");
        first.Dirty.Value = second.Dirty.Value = true;
        var files = new MemoryFileStorage();
        files.Write("first.txt", "old");
        files.Write("second.txt", "old");
        using var session = new EditorSession(files,
            new Dictionary<string, IEditorDocument> { ["first"] = first, ["second"] = second }, DockTree.Single("first", "second"));
        session.Documents.SaveAs(first, "first.txt"); first.Dirty.Value = true;
        session.Documents.SaveAs(second, "second.txt"); second.Dirty.Value = true;
        bool closed = false;

        session.CloseCoordinator.BeginProject(() => closed = true);
        session.CloseCoordinator.Decide(EditorCloseDecision.SaveAndContinue);
        Assert.False(closed);
        session.CloseCoordinator.Decide(EditorCloseDecision.SaveAndContinue);

        Assert.False(closed);
        Assert.NotNull(session.CloseCoordinator.Pending.Value?.Error);
        Assert.Contains(session.DiagnosticsService.Items, x => x.Source == "save");
    }

    [Fact]
    public void ExternalChangeSupportsReloadKeepLocalAndDisabledCompare()
    {
        var files = new MemoryFileStorage();
        files.Write("a.txt", "one");
        var doc = new TestDocument("a");
        using var session = new EditorSession(files, new Dictionary<string, IEditorDocument> { ["a"] = doc }, DockTree.Single("a"));
        session.Documents.SaveAs(doc, "a.txt");
        files.Write("a.txt", "two");

        Assert.True(session.Documents.BindingOf(doc)!.ExternalChange.Value);
        Assert.True(session.ResolveExternalChange(doc, ExternalChangeDecision.Reload).Applied);
        Assert.Equal("two", doc.Content);
        files.Write("a.txt", "three");
        Assert.True(session.ResolveExternalChange(doc, ExternalChangeDecision.KeepLocal).Applied);
        Assert.False(session.Documents.BindingOf(doc)!.ExternalChange.Value);
        Assert.False(session.CompareExternalChange(doc).Enabled);
        Assert.NotNull(session.CompareExternalChange(doc).DisabledReason);
    }

    private class TestDocument(string title) : IEditorDocument
    {
        public string Content { get; set; } = "";
        public string Kind => "text";
        public string Title => title;
        public Signal<bool> Dirty { get; } = new(false);
        public bool CanUndo => false;
        public bool CanRedo => false;
        public Widget CreateView() => new Spacer();
        public void Undo() { }
        public void Redo() { }
        public virtual string Serialize() { Dirty.Value = false; return Content; }
        public void LoadFrom(string content) { Content = content; Dirty.Value = false; }
    }
    private sealed class ThrowingDocument(string title) : TestDocument(title)
    {
        private int _writes;
        public override string Serialize() => ++_writes == 1 ? base.Serialize() : throw new IOException("save failed");
    }
    private sealed class SavePicker(string path) : IEditorSavePathPicker { public string? PickSavePath(IEditorDocument document) => path; }
}
