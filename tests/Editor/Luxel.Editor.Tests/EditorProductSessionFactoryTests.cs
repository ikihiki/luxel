using Luxel.Controls;
using Luxel.Workbench;

namespace Luxel.Tests;

public sealed class EditorProductSessionFactoryTests
{
    [Fact]
    public void ProductionFactorySeedsSharedDocumentsPanesAndCommands()
    {
        var files = new MemoryFileStorage();
        using EditorSession session = EditorProductSessionFactory.Create(files, builds: new UnsupportedBuild());
        Assert.True(files.Exists(EditorProductSessionFactory.ProjectFile));
        Assert.Equal(["readme", "scene", "script"], session.OpenDocuments.Keys.Order().ToArray());
        Assert.IsType<SceneEditorView>(session.ResolveDockItem(EditorPaneIds.Scene).CreateView());
        Assert.IsType<AssetBrowser>(session.ResolveDockItem(EditorPaneIds.Assets).CreateView());
        Assert.NotNull(session.Commands.Find(EditorCommandIds.Build));
        Assert.False(session.Commands.Find(EditorCommandIds.Build)!.IsEnabled);
    }

    private sealed class UnsupportedBuild : IBuildService
    {
        public bool IsAvailable => false;
        public void Build() => throw new NotSupportedException();
    }
}
