using System.Text.Json;
using Microsoft.Playwright;
using Microsoft.Playwright.Xunit;
using static Microsoft.Playwright.Assertions;

namespace Luxel.Editor.Browser.E2E.Tests;

public sealed class EditorBrowserAcceptanceTests : PageTest
{
    [Fact]
    public async Task Scenario_01_demo_boots_with_the_checked_in_project_fixture()
    {
        EditorPageFailures failures = Page.CollectFailures();
        JsonElement snapshot = await Page.OpenEditorAsync();

        Assert.Equal("builtin:demo", snapshot.GetProperty("projectId").GetString());
        Assert.True(snapshot.GetProperty("storagePersistent").GetBoolean());
        Assert.Equal(6, snapshot.GetProperty("files").GetArrayLength());
        Assert.Contains("luxel.project.json", snapshot.GetProperty("files").EnumerateArray().Select(x => x.GetString()));
        Assert.Contains("Scenes/Main.scene", snapshot.GetProperty("files").EnumerateArray().Select(x => x.GetString()));
        await Expect(Page.GetByTestId("storage-mode")).ToContainTextAsync("IndexedDB");
        failures.AssertEmpty();
    }

    [Fact]
    public async Task Scenario_02_hierarchy_selection_is_reflected_by_the_scene_and_inspector()
    {
        EditorPageFailures failures = Page.CollectFailures();
        _ = await Page.OpenEditorAsync();

        JsonElement snapshot = await Page.InvokeEditorAsync("select-entity", "2");

        Assert.Equal(2, snapshot.GetProperty("selection").GetProperty("entityId").GetInt32());
        Assert.True(snapshot.GetProperty("selection").GetProperty("sceneSelected").GetBoolean());
        Assert.Equal(2, snapshot.GetProperty("inspector").GetProperty("entityId").GetInt32());
        Assert.Equal([96f, 112f], snapshot.Position());
        failures.AssertEmpty();
    }

    [Fact]
    public async Task Scenario_03_transform_edit_undo_and_redo_share_one_command_history()
    {
        EditorPageFailures failures = Page.CollectFailures();
        _ = await Page.OpenEditorAsync();
        _ = await Page.InvokeEditorAsync("select-entity", "2");

        JsonElement edited = await Page.InvokeEditorAsync("edit-transform");
        JsonElement undone = await Page.InvokeEditorAsync("undo");
        JsonElement redone = await Page.InvokeEditorAsync("redo");

        Assert.Equal([112f, 120f], edited.Position());
        Assert.Equal([96f, 112f], undone.Position());
        Assert.Equal([112f, 120f], redone.Position());
        failures.AssertEmpty();
    }

    [Fact]
    public async Task Scenario_04_asset_browser_opens_the_script_in_the_document_workspace()
    {
        EditorPageFailures failures = Page.CollectFailures();
        _ = await Page.OpenEditorAsync();

        JsonElement snapshot = await Page.InvokeEditorAsync("open-path", "Scripts/Player.cs");
        snapshot = await Page.InvokeEditorAsync("open-path", "Scripts/Player.cs");
        JsonElement document = snapshot.Document("Scripts/Player.cs");

        Assert.Single(snapshot.GetProperty("documents").EnumerateArray(), candidate =>
            candidate.TryGetProperty("path", out JsonElement path) && path.GetString() == "Scripts/Player.cs");
        Assert.Equal("script", document.GetProperty("id").GetString());
        Assert.Equal("text", document.GetProperty("kind").GetString());
        Assert.True(document.GetProperty("active").GetBoolean());
        Assert.False(document.GetProperty("dirty").GetBoolean());
        Assert.Contains("public sealed class Player", snapshot.GetProperty("activeText").GetString());
        failures.AssertEmpty();
    }

    [Fact]
    public async Task Scenario_05_text_edit_save_and_reload_round_trip_through_indexed_db()
    {
        EditorPageFailures failures = Page.CollectFailures();
        _ = await Page.OpenEditorAsync();
        _ = await Page.InvokeEditorAsync("open-path", "Scripts/Player.cs");
        const string marker = "\n// browser acceptance persisted\n";

        JsonElement edited = await Page.InvokeEditorAsync("edit-active", marker);
        Assert.True(edited.Document("Scripts/Player.cs").GetProperty("dirty").GetBoolean());
        JsonElement saved = await Page.InvokeEditorAsync("save-active");
        Assert.False(saved.Document("Scripts/Player.cs").GetProperty("dirty").GetBoolean());

        await Page.GotoAsync("about:blank");
        await Task.Delay(500);
        failures.Clear();
        _ = await Page.OpenEditorAsync();
        JsonElement reopened = await Page.InvokeEditorAsync("open-path", "Scripts/Player.cs");

        Assert.Contains(marker.Trim(), reopened.GetProperty("activeText").GetString());
        Assert.True(reopened.GetProperty("storagePersistent").GetBoolean());
        failures.AssertEmpty();
    }

    [Fact]
    public async Task Scenario_06_layout_changes_are_restored_when_the_demo_reopens()
    {
        EditorPageFailures failures = Page.CollectFailures();
        JsonElement initial = await Page.OpenEditorAsync();

        JsonElement changed = await Page.InvokeEditorAsync("change-layout");
        string changedLayout = changed.GetProperty("layout").GetString()!;
        Assert.NotEqual(initial.GetProperty("layout").GetString(), changedLayout);
        Assert.Equal(changedLayout, await Page.EvaluateAsync<string>(
            "() => localStorage.getItem('luxel.editor.editor.layout.v1')"));

        JsonElement restored = await Page.InvokeEditorAsync("open-demo");

        Assert.Equal(changedLayout, restored.GetProperty("layout").GetString());
        failures.AssertEmpty();
    }

    [Fact]
    public async Task Scenario_07_reset_demo_restores_seed_content_and_reopens_a_clean_project()
    {
        EditorPageFailures failures = Page.CollectFailures();
        _ = await Page.OpenEditorAsync();
        JsonElement original = await Page.InvokeEditorAsync("open-path", "Scripts/Player.cs");
        string originalText = original.GetProperty("activeText").GetString()!;
        _ = await Page.InvokeEditorAsync("edit-active", "\n// reset me\n");
        _ = await Page.InvokeEditorAsync("save-active");

        JsonElement reset = await Page.InvokeEditorAsync("reset-demo");
        JsonElement reopened = await Page.InvokeEditorAsync("open-path", "Scripts/Player.cs");

        Assert.Equal(1, reset.GetProperty("resetRevision").GetInt32());
        Assert.Equal(originalText, reopened.GetProperty("activeText").GetString());
        Assert.False(reopened.Document("Scripts/Player.cs").GetProperty("dirty").GetBoolean());
        failures.AssertEmpty();
    }

    [Fact]
    public async Task Scenario_08_expected_fixture_warning_is_exposed_without_startup_failure()
    {
        EditorPageFailures failures = Page.CollectFailures();
        JsonElement snapshot = await Page.OpenEditorAsync();

        Assert.Equal(1, snapshot.GetProperty("warningCount").GetInt32());
        Assert.Equal("ready", await Page.GetByTestId("editor-status").GetAttributeAsync("data-status"));
        await Expect(Page.GetByRole(AriaRole.Alert)).ToBeHiddenAsync();
        failures.AssertEmpty();
    }

    [Fact]
    public async Task Scenario_09_material_node_graph_edit_save_and_reload_round_trip()
    {
        EditorPageFailures failures = Page.CollectFailures();
        _ = await Page.OpenEditorAsync();
        JsonElement opened = await Page.InvokeEditorAsync("open-path", "Materials/Coin.material.json");

        Assert.Equal("node-graph", opened.Document("Materials/Coin.material.json").GetProperty("kind").GetString());
        Assert.Equal(3, opened.GetProperty("material").GetProperty("nodeCount").GetInt32());
        Assert.Equal([32f, 48f], opened.MaterialPosition());

        JsonElement edited = await Page.InvokeEditorAsync("edit-material");
        Assert.True(edited.Document("Materials/Coin.material.json").GetProperty("dirty").GetBoolean());
        Assert.Equal([56f, 60f], edited.MaterialPosition());
        JsonElement saved = await Page.InvokeEditorAsync("save-active");
        Assert.False(saved.Document("Materials/Coin.material.json").GetProperty("dirty").GetBoolean());

        await Page.GotoAsync("about:blank");
        await Task.Delay(500);
        failures.Clear();
        _ = await Page.OpenEditorAsync();
        JsonElement reopened = await Page.InvokeEditorAsync("open-path", "Materials/Coin.material.json");

        Assert.Equal([56f, 60f], reopened.MaterialPosition());
        failures.AssertEmpty();
    }

    [Fact]
    public async Task Scenario_10_dock_move_split_resize_and_layout_persistence_are_exercised()
    {
        EditorPageFailures failures = Page.CollectFailures();
        JsonElement initial = await Page.OpenEditorAsync();
        int initialSplits = initial.GetProperty("dock").GetProperty("splitCount").GetInt32();

        JsonElement changed = await Page.InvokeEditorAsync("change-layout");
        JsonElement dock = changed.GetProperty("dock");
        JsonElement[] groups = dock.GetProperty("groups").EnumerateArray().ToArray();
        JsonElement scriptGroup = groups.Single(group => group.GetProperty("tabs").EnumerateArray()
            .Any(tab => tab.GetString() == "script"));

        Assert.True(dock.GetProperty("splitCount").GetInt32() > initialSplits);
        Assert.Contains(scriptGroup.GetProperty("tabs").EnumerateArray().Select(tab => tab.GetString()), tab => tab == "readme");
        Assert.DoesNotContain(scriptGroup.GetProperty("tabs").EnumerateArray().Select(tab => tab.GetString()), tab => tab == "scene");
        Assert.NotEqual(0.5f, dock.GetProperty("rootSizes")[0].GetSingle());
        string changedLayout = changed.GetProperty("layout").GetString()!;

        JsonElement restored = await Page.InvokeEditorAsync("open-demo");

        Assert.Equal(changedLayout, restored.GetProperty("layout").GetString());
        failures.AssertEmpty();
    }

    [Fact]
    public async Task Scenario_11_browser_capability_affordances_and_automation_ids_are_stable()
    {
        EditorPageFailures failures = Page.CollectFailures();
        _ = await Page.OpenEditorAsync();
        JsonElement capabilities = await Page.EvaluateAsync<JsonElement>("() => globalThis.luxelEditorState.capabilities");

        Assert.True(capabilities.GetProperty("indexedDb").GetBoolean());
        Assert.True(capabilities.GetProperty("archive").GetBoolean());
        Assert.False(capabilities.GetProperty("assetImport").GetBoolean());
        Assert.False(capabilities.GetProperty("processBuild").GetBoolean());
        Assert.False(capabilities.GetProperty("reveal").GetBoolean());
        await Expect(Page.GetByTestId("open-demo")).ToBeVisibleAsync();
        await Expect(Page.GetByTestId("reset-demo")).ToBeVisibleAsync();
        await Expect(Page.GetByTestId("open-gallery")).ToHaveAttributeAsync("href", "../../gallery/");
        Assert.True(await Page.EvaluateAsync<bool>("() => typeof globalThis.luxelEditorAutomation?.invoke === 'function'"));
        failures.AssertEmpty();
    }

    [Fact]
    public async Task Nested_subpath_smoke_loads_framework_demo_assets_and_automation_relatively()
    {
        EditorPageFailures failures = Page.CollectFailures();
        var requests = new List<string>();
        Page.Request += (_, request) => requests.Add(request.Url);
        await EditorBrowserTestHost.EnsureStartedAsync();

        JsonElement snapshot = await Page.OpenEditorAsync(EditorBrowserTestHost.NestedBaseUrl);

        Assert.Equal("builtin:demo", snapshot.GetProperty("projectId").GetString());
        Assert.StartsWith(EditorBrowserTestHost.NestedBaseUrl, Page.Url, StringComparison.Ordinal);
        Assert.Contains(requests, url => url.StartsWith(EditorBrowserTestHost.NestedBaseUrl + "_framework/", StringComparison.Ordinal));
        Assert.Contains(requests, url => url.StartsWith(EditorBrowserTestHost.NestedBaseUrl + "demo/", StringComparison.Ordinal));
        Assert.Contains(requests, url => url == EditorBrowserTestHost.NestedBaseUrl + "main.js");
        failures.AssertEmpty();
    }
}
