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
    public void Csx_defaults_to_csharp_script_while_explicit_languages_are_preserved()
    {
        Assert.Equal("csharp-script", new PlaygroundFile("Main.csx", "return 1;").Language);
        Assert.Equal("csharp", new PlaygroundFile("Helper.cs", "class Helper {}").Language);

        var explicitFile = new PlaygroundFile("id", "Main.csx", "csharp", "return 1;");
        var draft = new PlaygroundDraft("sample", "Sample", explicitFile.Id, explicitFile.Id, [explicitFile], 0);
        Assert.Equal("csharp", draft.RenameFile(explicitFile.Id, "Main.txt").MainFile.Language);
    }

    [Theory]
    [InlineData("/root.csx")]
    [InlineData("C:\\root.csx")]
    [InlineData("https://example.test/main.csx")]
    [InlineData("folder:name/main.csx")]
    [InlineData("folder/../main.csx")]
    [InlineData("folder//main.csx")]
    [InlineData("folder/line\nbreak.csx")]
    [InlineData("folder/nul\0name.csx")]
    public void Playground_and_resource_paths_reject_the_same_unsafe_inputs(string path)
    {
        Assert.Throws<ArgumentException>(() => WorkspacePath.Normalize(path));
        Assert.Throws<ArgumentException>(() => PlaygroundWorkspaceValidation.NormalizePath(path));
    }

    [Fact]
    public void Workspace_validation_uses_case_insensitive_paths_and_utf8_byte_limits()
    {
        var duplicateCase = new[]
        {
            new PlaygroundFile("one", "Main.csx", "csharp-script", ""),
            new PlaygroundFile("two", "main.csx", "csharp", ""),
        };
        Assert.Throws<ArgumentException>(() => PlaygroundWorkspaceValidation.ValidateFiles(duplicateCase));

        string oversizedCSharp = new('é', WorkspaceLimits.MaxCSharpFileBytes / 2 + 1);
        Assert.Throws<ArgumentException>(() => PlaygroundWorkspaceValidation.ValidateFiles(
            [new PlaygroundFile("main", "Main.csx", "csharp-script", oversizedCSharp)]));

        string oversizedWorkspace = new('é', WorkspaceLimits.MaxTotalSourceBytes / 2 + 1);
        Assert.Throws<ArgumentException>(() => PlaygroundWorkspaceValidation.ValidateFiles(
            [new PlaygroundFile("notes", "notes.txt", "text", oversizedWorkspace)]));

        PlaygroundFile[] tooMany = Enumerable.Range(0, WorkspaceLimits.MaxFileCount + 1)
            .Select(index => new PlaygroundFile($"id-{index}", $"file-{index}.txt", "text", ""))
            .ToArray();
        Assert.Throws<ArgumentException>(() => PlaygroundWorkspaceValidation.ValidateFiles(tooMany));
    }

    [Fact]
    public void Sample_catalog_has_stable_valid_workspaces_and_a_3d_slang_sample()
    {
        Assert.Equal(PlaygroundTemplates.All.Count, PlaygroundTemplates.All.Select(template => template.Id).Distinct(StringComparer.Ordinal).Count());
        foreach (PlaygroundTemplate template in PlaygroundTemplates.All)
        {
            PlaygroundDraft draft = template.CreateDraft();
            Assert.Equal("csharp-script", draft.MainFile.Language);
            Assert.Equal(template.Files.Count, template.Files.Select(file => file.Id).Distinct(StringComparer.Ordinal).Count());
        }

        PlaygroundDraft cube = PlaygroundTemplates.SlangCube.CreateDraft();
        Assert.Equal(3, cube.Files.Count);
        Assert.Contains(cube.Files, file => file.Path == "SlangCubeRenderer.cs" && file.Language == "csharp");
        Assert.Contains(cube.Files, file => file.Path == "Shaders/cube.slang" && file.Language == "slang");
        Assert.Contains(cube.Files, file => file.Path == "SlangCubeRenderer.cs" && file.Source.Contains("GpuViewSurface", StringComparison.Ordinal));
        Assert.Contains(cube.Files, file => file.Path == "SlangCubeRenderer.cs" && file.Source.Contains("surface.CopyColorToFramebuffer", StringComparison.Ordinal));
        Assert.DoesNotContain(cube.Files, file => file.Source.Contains("IGpuScene", StringComparison.Ordinal));
        Assert.Contains("WebScriptResources.Get<GpuShaderCode>", cube.MainFile.Source);
        Assert.Contains("Kit.GpuView", cube.MainFile.Source);
    }

    [Fact]
    public void Workspace_vfs_is_case_insensitive_across_platforms()
    {
        var vfs = new WorkspaceFileSystem();
        vfs.Set("Folder/File.txt", Encoding.UTF8.GetBytes("one"));

        Assert.True(vfs.Exists("folder/file.TXT"));
        vfs.Set("FOLDER/FILE.txt", Encoding.UTF8.GetBytes("two"));

        WorkspaceFileSystemSnapshot snapshot = vfs.Snapshot();
        Assert.Single(snapshot.Files);
        Assert.Equal("two", Encoding.UTF8.GetString(snapshot.Files["folder/file.txt"].Span));
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
