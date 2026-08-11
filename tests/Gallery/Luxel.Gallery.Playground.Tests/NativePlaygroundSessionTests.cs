using System.Text.Json;
using Luxel.Controls;
using Luxel.Gallery.Playground;
using Luxel.Settings;

namespace Luxel.Gallery.Playground.Tests;

public sealed class NativePlaygroundSessionTests
{
    private static readonly PlaygroundTemplate Template = new(
        "native", "Native", "", "Main.csx",
        [
            new PlaygroundFile("main-id", "Main.csx", "csharp", "return Helper.Create();", 3),
            new PlaygroundFile("helper-id", "Helper.cs", "csharp", """
                using Luxel.Controls;
                using Luxel.UI;
                public static class Helper
                {
                    public static Widget Create() => Kit.Button(_ => { }, "two files");
                }
                """, 7),
        ]);

    [Fact]
    public void Schema_v2_round_trip_preserves_shared_draft_identity_versions_revision_and_selection()
    {
        var files = new InMemoryFileStore();
        var first = new NativePlaygroundSession(files, Template);

        first.UpdateFile("helper-id", Template.Files[1].Source + "\n// edited");
        first.Activate("helper-id");

        var restored = new NativePlaygroundSession(files, Template);
        AssertDraftEqual(first.Draft, restored.Draft);
        Assert.Equal("Helper.cs", restored.ActiveFileName);
        Assert.Equal("helper-id", restored.Draft.SelectedFileId);
        Assert.Equal(2, restored.Draft.Revision);
        Assert.Equal(8, restored.Draft.SelectedFile.Version);
        Assert.Equal("csharp", restored.Draft.SelectedFile.Language);

        using JsonDocument json = JsonDocument.Parse(files.Read(restored.StorageName)!);
        JsonElement root = json.RootElement;
        Assert.Equal(NativePlaygroundSession.SchemaVersion, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("main-id", root.GetProperty("mainFileId").GetString());
        Assert.Equal("helper-id", root.GetProperty("selectedFileId").GetString());
        Assert.Equal(2, root.GetProperty("revision").GetInt64());
        JsonElement helper = root.GetProperty("files").EnumerateArray().Single(file => file.GetProperty("id").GetString() == "helper-id");
        Assert.Equal("Helper.cs", helper.GetProperty("path").GetString());
        Assert.Equal("csharp", helper.GetProperty("language").GetString());
        Assert.Equal(8, helper.GetProperty("version").GetInt64());
    }

    [Fact]
    public void Add_rename_delete_mutations_persist_stable_identity_and_selection()
    {
        var files = new InMemoryFileStore();
        var session = new NativePlaygroundSession(files, Template);

        PlaygroundFile added = session.AddFile("Extra.cs", "public static class Extra {}", id: "extra-id");
        session.Activate(added.Id);
        session.RenameFile(added.Id, "Renamed.cs");

        var renamed = new NativePlaygroundSession(files, Template);
        Assert.Equal("extra-id", renamed.Draft.SelectedFileId);
        Assert.Equal("Renamed.cs", renamed.ActiveFileName);
        Assert.Equal(1, renamed.Draft.SelectedFile.Version);

        renamed.DeleteFile("extra-id");
        var deleted = new NativePlaygroundSession(files, Template);
        Assert.DoesNotContain(deleted.Draft.Files, file => file.Id == "extra-id");
        Assert.Equal("main-id", deleted.Draft.SelectedFileId);
        Assert.Equal(4, deleted.Draft.Revision);
    }

    [Fact]
    public async Task Native_runner_compiles_and_executes_entry_csx_with_support_cs_as_separate_documents()
    {
        var session = new NativePlaygroundSession(new InMemoryFileStore(), Template);

        NativePlaygroundRunResult result = await new NativePlaygroundRunner().RunAsync(session.Draft);

        Assert.True(result.Success, Format(result));
        Assert.IsType<Button>(result.Widget);
    }

    [Fact]
    public async Task Native_runner_excludes_slang_documents()
    {
        PlaygroundDraft draft = Template.CreateDraft().AddFile(
            "shader.slang", "this is intentionally not C#", language: "slang", id: "shader-id");

        NativePlaygroundRunResult result = await new NativePlaygroundRunner().RunAsync(draft);

        Assert.True(result.Success, Format(result));
        Assert.IsType<Button>(result.Widget);
    }

    [Fact]
    public async Task Native_run_coordinator_cancels_superseded_work_and_only_publishes_the_latest_result()
    {
        using var coordinator = new NativePlaygroundRunCoordinator();
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var published = new List<string>();

        Task<bool> first = coordinator.RunAsync(async cancellationToken =>
        {
            firstStarted.SetResult();
            await releaseFirst.Task;
            return Successful("first");
        }, result => published.Add(result.Widget!.DebugDetail!));
        await firstStarted.Task;

        Task<bool> second = coordinator.RunAsync(
            _ => Task.FromResult(Successful("second")),
            result => published.Add(result.Widget!.DebugDetail!));
        Assert.True(await second);
        releaseFirst.SetResult();

        Assert.False(await first);
        Assert.Equal(["second"], published);
    }

    [Fact]
    public async Task Native_run_coordinator_cancel_prevents_publication()
    {
        using var coordinator = new NativePlaygroundRunCoordinator();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bool published = false;

        Task<bool> run = coordinator.RunAsync(async cancellationToken =>
        {
            started.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return Successful("never");
        }, _ => published = true);
        await started.Task;
        coordinator.Cancel();

        Assert.False(await run);
        Assert.False(published);
    }

    private static NativePlaygroundRunResult Successful(string text)
        => new(true, Kit.Text(text), [], null);

    [Fact]
    public void Invalid_or_old_schema_falls_back_to_template()
    {
        var files = new InMemoryFileStore();
        string name = NativePlaygroundSession.StoragePrefix + Template.Id + ".json";
        files.Write(name, "{ \"schemaVersion\": 1, \"files\": [] }");

        var session = new NativePlaygroundSession(files, Template);

        AssertDraftEqual(Template.CreateDraft(), session.Draft);
    }

    [Fact]
    public void Reset_restores_all_template_metadata_and_persists_it()
    {
        var files = new InMemoryFileStore();
        var session = new NativePlaygroundSession(files, Template);
        session.UpdateFile("main-id", "changed");
        session.Activate("helper-id");

        session.Reset();

        AssertDraftEqual(Template.CreateDraft(), session.Draft);
        var restored = new NativePlaygroundSession(files, Template);
        AssertDraftEqual(Template.CreateDraft(), restored.Draft);
    }

    private static void AssertDraftEqual(PlaygroundDraft expected, PlaygroundDraft actual)
    {
        Assert.Equal(expected.TemplateId, actual.TemplateId);
        Assert.Equal(expected.Title, actual.Title);
        Assert.Equal(expected.MainFileId, actual.MainFileId);
        Assert.Equal(expected.SelectedFileId, actual.SelectedFileId);
        Assert.Equal(expected.Revision, actual.Revision);
        Assert.Equal(expected.Files, actual.Files);
    }

    private static string Format(NativePlaygroundRunResult result)
        => result.Failure?.Message ?? string.Join(Environment.NewLine,
            result.Diagnostics.Select(diagnostic => $"{diagnostic.Id} {diagnostic.FileName}:{diagnostic.Line}:{diagnostic.Column} {diagnostic.Message}"));
}
