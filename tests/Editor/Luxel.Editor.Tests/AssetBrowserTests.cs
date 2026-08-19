using System.Reflection;
using Luxel.Controls;
using Luxel.UI;
using Luxel.Workbench;
using Xunit;

namespace Luxel.Tests;

/// <summary>WB: AssetBrowser.BuildTree + IFileStorage.List (ADR-0014 S(C4))。GPU 不要。</summary>
public class AssetBrowserTests
{
    [Fact]
    public void BuildTree_FoldersFirst_Sorted_NestedKeys()
    {
        var roots = AssetBrowser.BuildTree(["readme.md", "src/Main.cs", "src/App.cs", "assets/img/logo.png"]);

        Assert.Equal(["assets", "src", "readme.md"], roots.Select(n => n.Label).ToArray());   // フォルダ優先 + 名前順
        TreeNode assets = roots[0];
        Assert.Null(assets.Tag);                          // フォルダ = 見出し (開閉)
        Assert.Equal("assets", assets.Key);
        TreeNode img = Assert.Single(assets.Children!);
        Assert.Equal("assets/img", img.Key);
        Assert.Equal("assets/img/logo.png", img.Children![0].Key);
        Assert.Equal("assets/img/logo.png", img.Children[0].Tag);   // ファイル = Tag に path

        TreeNode src = roots[1];
        Assert.Equal(["App.cs", "Main.cs"], src.Children!.Select(n => n.Label).ToArray());
    }

    [Fact]
    public void BuildTree_Empty_ReturnsEmpty()
    {
        Assert.Empty(AssetBrowser.BuildTree([]));
    }

    [Fact]
    public void MemoryFileStorage_List_ReturnsAllPaths()
    {
        var fs = new MemoryFileStorage();
        fs.Write("a.txt", "1");
        fs.Write("dir/b.txt", "2");
        Assert.Equal(["a.txt", "dir/b.txt"], fs.List().OrderBy(p => p).ToArray());
    }
}

public class AssetOperationsTests
{
    [Fact]
    public void CreateRenameMoveDuplicateDeleteAndImportValidateCollisions()
    {
        var files = new MemoryFileStorage();
        var operations = new AssetOperations(new FileAssetStorage(files));
        Assert.Equal("assets/a.txt", operations.Create("assets/a.txt", "a"));
        Assert.Throws<IOException>(() => operations.Create("assets/a.txt"));
        Assert.Equal("assets/b.txt", operations.Rename("assets/a.txt", "b.txt"));
        Assert.Equal("other/b.txt", operations.Move("assets/b.txt", "other"));
        string copy = operations.Duplicate("other/b.txt");
        Assert.Contains("copy", copy);
        Assert.Equal(["imports/c.txt"], operations.Import("imports", [("c.txt", "c")]));
        operations.Delete([copy, "imports/c.txt"]);
        Assert.False(files.Exists(copy));
        Assert.Throws<ArgumentException>(() => operations.Create("../bad.txt"));
    }

    [Fact]
    public void BrowserModelSeparatesFoldersFiltersAndPreservesValidSelection()
    {
        var files = new MemoryFileStorage();
        files.Write("a/root.txt", ""); files.Write("a/sub/deep.txt", ""); files.Write("b.txt", "");
        var model = new AssetBrowserModel(new AssetOperations(new FileAssetStorage(files)));
        Assert.True(model.CurrentItems()[0].IsFolder);
        model.CurrentFolder.Value = "a";
        model.Select("a/root.txt");
        model.Filter.Value = "root";
        Assert.Single(model.CurrentItems());
        model.Refresh();
        Assert.Contains("a/root.txt", model.Selection);
        files.Delete("a/root.txt");
        model.Refresh();
        Assert.Empty(model.Selection);
    }

    [Fact]
    public void ProductionAssetPaneWiresHostImportAndRevealActions()
    {
        var files = new MemoryFileStorage();
        var host = new AssetHost();
        using var session = new EditorSession(files,
            new Dictionary<string, IEditorDocument> { ["doc"] = new AssetDoc() }, DockTree.Single("doc"),
            assetHost: host,
            capabilities: new EditorHostCapabilities(false, NativeDialogs: true, RevealInFileManager: true));
        var browser = Assert.IsType<AssetBrowser>(session.ResolveDockItem(EditorPaneIds.Assets).CreateView());

        browser.OnImportRequest.Invoke(browser);
        Assert.Equal("imported", files.Read("imported.txt"));
        browser.CreateAsset("selected.txt", "x");
        Assert.True(browser.SelectAsset("selected.txt"));
        browser.OnRevealRequest.Invoke(browser, browser.Selected);
        Assert.Equal("selected.txt", host.Revealed);
    }

    [Fact]
    public void ProductionPaneComposesFolderTreeAndSeparateCurrentFolderSurface()
    {
        var files = new MemoryFileStorage();
        files.Write("assets/a.txt", "a");
        files.Write("assets/sub/b.txt", "b");
        files.Write("root.txt", "root");
        var operations = new AssetOperations(new FileAssetStorage(files));
        AssetBrowser browser = EditorKit.AssetBrowser(storage: files, operations: operations,
            expanded: new HashSet<string>(StringComparer.Ordinal));

        Widget[] composed = Flatten(Compose(browser)).ToArray();
        Assert.Contains(composed, x => x is TreeView);
        AssetItemsView surface = Assert.Single(composed.OfType<AssetItemsView>());
        Assert.Equal(AssetBrowserViewMode.List, surface.Mode);
        Assert.Equal(["assets", "root.txt"], surface.Items.Select(x => x.Name).ToArray());
        Assert.All(browser.FolderTree, x => Assert.True(x.Tag is string));

        browser.OpenFolder("assets");
        browser.SetViewMode(AssetBrowserViewMode.Grid);
        surface = Assert.Single(Flatten(Compose(browser)).OfType<AssetItemsView>());
        Assert.Equal(AssetBrowserViewMode.Grid, surface.Mode);
        Assert.Equal(["sub", "a.txt"], surface.Items.Select(x => x.Name).ToArray());
    }

    [Fact]
    public void MultiSelectionDeleteAndDropImportProduceBindingReadyMutationResults()
    {
        var files = new MemoryFileStorage();
        files.Write("a.txt", "a");
        files.Write("b.txt", "b");
        var operations = new AssetOperations(new FileAssetStorage(files));
        var results = new List<AssetMutationResult>();
        operations.Mutated += results.Add;
        AssetBrowser browser = EditorKit.AssetBrowser(storage: files, operations: operations,
            expanded: new HashSet<string>(StringComparer.Ordinal));

        Assert.True(browser.SelectAsset("a.txt"));
        Assert.True(browser.SelectAsset("b.txt", additive: true));
        Assert.Equal(2, browser.SelectedPaths.Count);
        Assert.True(browser.DeleteSelected());
        AssetMutationResult deleted = results[^1];
        Assert.Equal(AssetMutationKind.Delete, deleted.Kind);
        Assert.Equal(["a.txt", "b.txt"], deleted.RemovedPaths.Order().ToArray());

        Assert.True(browser.HandleDrop(new AssetImportPayload([("drop.txt", "drop")])));
        AssetMutationResult imported = results[^1];
        Assert.Equal(AssetMutationKind.Import, imported.Kind);
        Assert.Equal(["drop.txt"], imported.CreatedPaths);
        Assert.Equal("drop", files.Read("drop.txt"));
    }

    [Fact]
    public void RefreshFailureIsRetainedAsUserVisibleModelError()
    {
        var operations = new AssetOperations(new ThrowingAssetStorage());
        AssetBrowser browser = EditorKit.AssetBrowser(operations: operations,
            expanded: new HashSet<string>(StringComparer.Ordinal));

        Assert.False(browser.Refresh());
        Assert.Equal("list failed", browser.LastError.Peek());
        Assert.Equal("list failed", browser.Model.Error.Peek());
    }

    [Fact]
    public void PartialDeleteReportsPerItemResultsAndRefreshesBrowserState()
    {
        var storage = new SelectiveFailAssetStorage(deleteFailures: ["b.txt"]);
        storage.Inner.Write("a.txt", "a");
        storage.Inner.Write("b.txt", "b");
        var operations = new AssetOperations(storage);
        AssetMutationResult? observed = null;
        operations.Mutated += result => observed = result;
        AssetBrowser browser = EditorKit.AssetBrowser(operations: operations,
            expanded: new HashSet<string>(StringComparer.Ordinal));
        browser.SelectAsset("a.txt");
        browser.SelectAsset("b.txt", additive: true);

        Assert.False(browser.DeleteSelected());

        AssetMutationResult result = Assert.IsType<AssetMutationResult>(observed);
        Assert.False(result.Succeeded);
        Assert.Equal(["a.txt"], result.RemovedPaths);
        Assert.Equal("b.txt", Assert.Single(result.Failures).Path);
        Assert.False(storage.Inner.Exists("a.txt"));
        Assert.True(storage.Inner.Exists("b.txt"));
        Assert.Equal(["b.txt"], browser.Model.Paths);
        Assert.Contains("b.txt", browser.LastError.Peek());
    }

    [Fact]
    public void PartialImportReportsCreatedFilesAndFailures()
    {
        var storage = new SelectiveFailAssetStorage(writeFailures: ["imports/b.txt"]);
        var operations = new AssetOperations(storage);
        AssetMutationResult? observed = null;
        operations.Mutated += result => observed = result;

        AssetMutationResult result = operations.ImportAssets("imports",
            [("a.txt", "a"), ("b.txt", "b"), ("c.txt", "c")]);

        Assert.Same(result, observed);
        Assert.False(result.Succeeded);
        Assert.Equal(["imports/a.txt", "imports/c.txt"], result.CreatedPaths);
        Assert.Equal("imports/b.txt", Assert.Single(result.Failures).Path);
        Assert.Equal("a", storage.Inner.Read("imports/a.txt"));
        Assert.False(storage.Inner.Exists("imports/b.txt"));
        Assert.Equal("c", storage.Inner.Read("imports/c.txt"));
    }

    [Fact]
    public void SessionCoordinatesDocumentBindingsAfterPartialDelete()
    {
        var storage = new SelectiveFailFileStorage(deleteFailures: ["b.txt"]);
        storage.Inner.Write("a.txt", "a");
        storage.Inner.Write("b.txt", "b");
        var a = new AssetDoc();
        var b = new AssetDoc();
        using var session = new EditorSession(storage,
            new Dictionary<string, IEditorDocument> { ["a"] = a, ["b"] = b }, DockTree.Single("a"));
        session.Documents.Rebind(a, "a.txt");
        session.Documents.Rebind(b, "b.txt");

        AssetMutationResult result = session.Assets.DeleteAssets(["a.txt", "b.txt"]);

        Assert.Equal(["a.txt"], result.RemovedPaths);
        Assert.Equal("b.txt", Assert.Single(result.Failures).Path);
        Assert.Null(session.Documents.BindingOf(a));
        Assert.True(a.Dirty.Peek());
        Assert.Equal("b.txt", session.Documents.BindingOf(b)!.Path);
        Assert.False(b.Dirty.Peek());
        Assert.Contains(session.DiagnosticsService.Items, x => x.Message.Contains("b.txt", StringComparison.Ordinal));
    }

    private static Widget Compose(AssetBrowser browser)
        => Assert.IsAssignableFrom<Widget>(typeof(AssetBrowser)
            .GetMethod("Build", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(browser, null));

    private static IEnumerable<Widget> Flatten(Widget root)
    {
        yield return root;
        foreach (Widget child in root.DebugChildren())
            foreach (Widget nested in Flatten(child)) yield return nested;
    }

    private sealed class SelectiveFailAssetStorage(
        IEnumerable<string>? deleteFailures = null, IEnumerable<string>? writeFailures = null) : IAssetStorage
    {
        private readonly HashSet<string> _deleteFailures = new(deleteFailures ?? [], StringComparer.Ordinal);
        private readonly HashSet<string> _writeFailures = new(writeFailures ?? [], StringComparer.Ordinal);
        public MemoryFileStorage Inner { get; } = new();
        public IEnumerable<string> List() => Inner.List();
        public bool Exists(string path) => Inner.Exists(path);
        public string? Read(string path) => Inner.Read(path);
        public void Write(string path, string content)
        {
            if (_writeFailures.Contains(path)) throw new IOException("write failed");
            Inner.Write(path, content);
        }
        public void Delete(string path)
        {
            if (_deleteFailures.Contains(path)) throw new IOException("delete failed");
            Inner.Delete(path);
        }
        public void Move(string sourcePath, string destinationPath) => Inner.Move(sourcePath, destinationPath);
    }

    private sealed class SelectiveFailFileStorage(IEnumerable<string>? deleteFailures = null) : IFileStorage
    {
        private readonly HashSet<string> _deleteFailures = new(deleteFailures ?? [], StringComparer.Ordinal);
        public MemoryFileStorage Inner { get; } = new();
        public IEnumerable<string> List() => Inner.List();
        public bool Exists(string path) => Inner.Exists(path);
        public string? Read(string path) => Inner.Read(path);
        public void Write(string path, string content) => Inner.Write(path, content);
        public IDisposable? Watch(string path, Action onChanged) => Inner.Watch(path, onChanged);
        public void Delete(string path)
        {
            if (_deleteFailures.Contains(path)) throw new IOException("delete failed");
            Inner.Delete(path);
        }
        public void Move(string sourcePath, string destinationPath) => Inner.Move(sourcePath, destinationPath);
    }

    private sealed class ThrowingAssetStorage : IAssetStorage
    {
        public IEnumerable<string> List() => throw new IOException("list failed");
        public bool Exists(string path) => false;
        public string? Read(string path) => null;
        public void Write(string path, string content) => throw new NotSupportedException();
        public void Delete(string path) => throw new NotSupportedException();
        public void Move(string sourcePath, string destinationPath) => throw new NotSupportedException();
    }

    private sealed class AssetHost : IEditorAssetHost
    {
        public string? Revealed { get; private set; }
        public IReadOnlyList<(string Name, string Content)> PickImportFiles() => [("imported.txt", "imported")];
        public void Reveal(string path) => Revealed = path;
    }

    private sealed class AssetDoc : IEditorDocument
    {
        public string Kind => "fake"; public string Title => "doc"; public Signal<bool> Dirty { get; } = new(false);
        public bool CanUndo => false; public bool CanRedo => false; public Widget CreateView() => Kit.Spacer();
        public void Undo() { } public void Redo() { } public string Serialize() => ""; public void LoadFrom(string content) { }
    }

    [Fact]
    public void CapabilityStatesDistinguishUnsupportedAndDisabled()
    {
        var operations = new AssetOperations(new FileAssetStorage(new MemoryFileStorage()),
            reveal: new(EditorCapabilityAvailability.Unsupported, "no shell"),
            import: new(EditorCapabilityAvailability.Disabled, "read only"));
        Assert.False(operations.RevealCapability.CanExecute);
        Assert.Equal(EditorCapabilityAvailability.Disabled, operations.ImportCapability.Availability);
        Assert.Throws<NotSupportedException>(() => operations.Import("", [("a", "b")]));
    }
}
