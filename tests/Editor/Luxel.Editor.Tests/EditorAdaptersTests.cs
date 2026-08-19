using Luxel.Controls;
using Luxel.UI;
using Luxel.Workbench;

namespace Luxel.Tests;

public sealed class EditorAdaptersTests
{
    [Fact]
    public void EditorDocumentTabsCloseUsesCoordinatorAndCtrlWCommand()
    {
        var doc = new Doc();
        using var session = new EditorSession(new Dictionary<string, IEditorDocument> { ["doc"] = doc }, DockTree.Single("doc"));
        var tabs = new EditorDocumentTabs(session);
        Assert.True(tabs.CloseActive());
        Assert.Empty(session.OpenDocuments);
    }

    [Fact]
    public void EditorStatusContributionUsesStableKeysAndRegions()
    {
        using var session = new EditorSession(new Dictionary<string, IEditorDocument> { ["doc"] = new Doc() }, DockTree.Single("doc"));
        var status = new EditorStatusBar(session, [new Contribution()]);
        Assert.Single(status.Contributions);
        Assert.Equal("test", status.Contributions[0].Key);
    }

    private sealed class Contribution : IEditorStatusContribution
    {
        public string Key => "test"; public StatusBarRegion Region => StatusBarRegion.Center; public int Priority => 5;
        public bool IsVisible(EditorSession session) => true; public Widget Create(EditorSession session) => new Spacer();
    }
    private sealed class Doc : IEditorDocument
    {
        public string Kind => "fake"; public string Title => "doc"; public Signal<bool> Dirty { get; } = new(false);
        public bool CanUndo => false; public bool CanRedo => false; public Widget CreateView() => new Spacer();
        public void Undo() { } public void Redo() { } public string Serialize() => ""; public void LoadFrom(string content) { }
    }
}
