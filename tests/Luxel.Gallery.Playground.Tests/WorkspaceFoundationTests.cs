using System.Text;
using Luxel.Gallery.Playground;
using Luxel.Resources;

namespace Luxel.Gallery.Playground.Tests;

public sealed class WorkspaceFoundationTests
{
    [Fact]
    public void Draft_operations_preserve_identity_and_advance_versions_and_revisions()
    {
        var main = new PlaygroundFile("main-id", "Main.csx", "csharp", "return 1;");
        var draft = new PlaygroundDraft("sample", "Sample", main.Id, main.Id, [main], 0);

        PlaygroundDraft added = draft.AddFile("lib/Helper.cs", "class Helper {}", id: "helper-id", expectedRevision: 0);
        PlaygroundDraft selected = added.SelectFile("helper-id", expectedRevision: 1);
        PlaygroundDraft updated = selected.UpdateFile("helper-id", "class Helper { }", expectedRevision: 2);
        PlaygroundDraft renamed = updated.RenameFile("helper-id", "lib/Renamed.cs", expectedRevision: 3);
        PlaygroundDraft deleted = renamed.DeleteFile("helper-id", expectedRevision: 4);

        PlaygroundFile renamedFile = renamed.SelectedFile;
        Assert.Equal("helper-id", renamedFile.Id);
        Assert.Equal("lib/Renamed.cs", renamedFile.Path);
        Assert.Equal("csharp", renamedFile.Language);
        Assert.Equal(2, renamedFile.Version);
        Assert.Equal(5, deleted.Revision);
        Assert.Equal(main.Id, deleted.SelectedFileId);
        Assert.Throws<StalePlaygroundRevisionException>(() => deleted.UpdateFile(main.Id, "stale", expectedRevision: 4));
    }

    [Fact]
    public void Draft_validation_rejects_invalid_paths_duplicates_and_main_deletion()
    {
        var main = new PlaygroundFile("main", "Main.csx", "csharp", "");
        var draft = new PlaygroundDraft("sample", "Sample", main.Id, main.Id, [main], 0);

        Assert.Throws<ArgumentException>(() => draft.AddFile("../escape.cs"));
        Assert.Throws<ArgumentException>(() => draft.AddFile("Main.csx"));
        Assert.Throws<InvalidOperationException>(() => draft.DeleteFile(main.Id));
    }

    [Fact]
    public async Task Workspace_vfs_applies_batches_atomically_and_notifies_changed_paths()
    {
        var vfs = new WorkspaceFileSystem();
        int oldNotifications = 0;
        int newNotifications = 0;
        using IReloadToken oldWatch = vfs.Watch("a.txt", () => oldNotifications++);
        using IReloadToken newWatch = vfs.Watch("b.txt", () => newNotifications++);

        long first = vfs.ApplyBatch(
        [
            new WorkspaceSetOperation("a.txt", Encoding.UTF8.GetBytes("one")),
            new WorkspaceRenameOperation("a.txt", "b.txt"),
        ], expectedRevision: 0);

        Assert.Equal(1, first);
        Assert.False(vfs.Exists("a.txt"));
        Assert.Equal("one", Encoding.UTF8.GetString(await vfs.ReadAsync("b.txt", default)));
        Assert.Equal(1, oldNotifications);
        Assert.Equal(1, newNotifications);
        WorkspaceFileSystemSnapshot snapshot = vfs.Snapshot();
        Assert.Equal(1, snapshot.Revision);
        Assert.Equal("one", Encoding.UTF8.GetString(snapshot.Files["b.txt"].Span));

        Assert.Throws<StaleWorkspaceRevisionException>(() =>
            vfs.Set("stale.txt", [], expectedRevision: 0));
        Assert.False(vfs.Exists("stale.txt"));
    }

    [Fact]
    public void Workspace_vfs_rolls_back_the_whole_batch_when_an_operation_fails()
    {
        var vfs = new WorkspaceFileSystem();
        vfs.Set("kept.txt", Encoding.UTF8.GetBytes("kept"));

        Assert.Throws<FileNotFoundException>(() => vfs.ApplyBatch(
        [
            new WorkspaceSetOperation("temporary.txt", []),
            new WorkspaceDeleteOperation("missing.txt"),
        ], expectedRevision: 1));

        Assert.Equal(1, vfs.Revision);
        Assert.True(vfs.Exists("kept.txt"));
        Assert.False(vfs.Exists("temporary.txt"));
    }

    [Fact]
    public async Task Workspace_source_handles_only_workspace_scheme()
    {
        var vfs = new WorkspaceFileSystem();
        vfs.Set("Main.csx", Encoding.UTF8.GetBytes("return 1;"));
        var source = new WorkspaceSource(vfs);

        Assert.Equal(["workspace"], source.Schemes);
        using var system = new ResourceSystem(sources: [source]);
        using ResourceHandle<byte[]> handle = system.Load<byte[]>("workspace://Main.csx");
        await handle.Ready;
        Assert.Equal("return 1;", Encoding.UTF8.GetString(handle.Value));
    }

    [Theory]
    [InlineData("https://example.test/image.tex?token=abc", ".tex", "token=abc", "")]
    [InlineData("https://example.test/image.tex?token=abc#sprite", ".tex", "token=abc", "sprite")]
    public void Resource_uri_extension_ignores_http_query_and_fragment(
        string raw, string extension, string query, string fragment)
    {
        var uri = new ResourceUri(raw);

        Assert.Equal(extension, uri.Extension);
        Assert.Equal(query, uri.Query);
        Assert.Equal(fragment, uri.Fragment);
        Assert.Equal(raw, uri.Url);
    }
}
