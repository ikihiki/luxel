using System.Text.Json;
using Luxel.Gallery.Playground;
using Luxel.Settings;

namespace Luxel.Gallery.Playground.Tests;

public sealed class NativePlaygroundSessionTests
{
    private static readonly PlaygroundTemplate Template = new(
        "native", "Native", "", "Main.csx",
        [
            new PlaygroundFile("Main.csx", "return Helper.Value;"),
            new PlaygroundFile("Helper.cs", "static class Helper { public const int Value = 1; }"),
        ]);

    [Fact]
    public void Edits_and_active_file_round_trip_through_schema_v2()
    {
        var files = new InMemoryFileStore();
        var first = new NativePlaygroundSession(files, Template);

        first.UpdateFile("Helper.cs", "static class Helper { public const int Value = 2; }");
        first.Activate("Helper.cs");

        var restored = new NativePlaygroundSession(files, Template);
        Assert.Equal("Helper.cs", restored.ActiveFileName);
        Assert.Equal(2, restored.Draft.Files.Count);
        Assert.Contains("Value = 2", restored.Draft.Files.Single(file => file.FileName == "Helper.cs").Source);

        using JsonDocument json = JsonDocument.Parse(files.Read(restored.StorageName)!);
        Assert.Equal(NativePlaygroundSession.SchemaVersion,
            json.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("Helper.cs", json.RootElement.GetProperty("activeFileName").GetString());
    }

    [Fact]
    public void Execution_source_orders_supporting_files_before_main_and_preserves_logical_names()
    {
        var session = new NativePlaygroundSession(new InMemoryFileStore(), Template);

        string source = session.CreateExecutionSource();

        int helper = source.IndexOf("#line 1 \"Helper.cs\"", StringComparison.Ordinal);
        int main = source.IndexOf("#line 1 \"Main.csx\"", StringComparison.Ordinal);
        Assert.True(helper >= 0 && main > helper);
        Assert.Contains("static class Helper", source);
        Assert.EndsWith("return Helper.Value;", source);
    }

    [Fact]
    public void Invalid_or_old_schema_falls_back_to_template()
    {
        var files = new InMemoryFileStore();
        string name = NativePlaygroundSession.StoragePrefix + Template.Id + ".json";
        files.Write(name, "{ \"schemaVersion\": 1, \"files\": [] }");

        var session = new NativePlaygroundSession(files, Template);

        Assert.Equal(Template.MainFileName, session.ActiveFileName);
        Assert.Equal(Template.Files, session.Draft.Files);
    }

    [Fact]
    public void Reset_restores_all_template_files_and_persists_them()
    {
        var files = new InMemoryFileStore();
        var session = new NativePlaygroundSession(files, Template);
        session.UpdateFile("Main.csx", "changed");
        session.Activate("Helper.cs");

        session.Reset();

        Assert.Equal(Template.Files, session.Draft.Files);
        Assert.Equal("Main.csx", session.ActiveFileName);
        var restored = new NativePlaygroundSession(files, Template);
        Assert.Equal(Template.Files, restored.Draft.Files);
        Assert.Equal("Main.csx", restored.ActiveFileName);
    }
}
