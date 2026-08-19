using System.Text.Json.Nodes;
using Microsoft.Playwright;
using Microsoft.Playwright.Xunit;
using static Microsoft.Playwright.Assertions;

namespace Luxel.Gallery.Browser.E2E.Tests;

public sealed class NodeGraphPlaygroundTests : PageTest
{
    [Fact]
    public async Task Json_arg_edited_before_runtime_ready_is_replayed_to_the_canvas()
    {
        await GalleryTestHost.EnsureStartedAsync();
        PageFailures failures = Page.CollectFailures(responses: true);
        const string story = "Controls/Editor/NodeGraphView/Playground";

        // Keep the outer host bridge unavailable so the edit occurs before configure/ready wiring.
        await Page.RouteAsync("**/gallery-host.js", async route =>
        {
            await Task.Delay(TimeSpan.FromSeconds(15));
            await route.ContinueAsync();
        });
        await Page.GotoAsync($"{GalleryTestHost.BaseUrl}{story.StoryPath(embed: false)}");
        ILocator argsTab = Page.GetByRole(AriaRole.Tab, new() { Name = "Args" });
        await Expect(argsTab).ToBeVisibleAsync(new() { Timeout = 90_000 });
        await argsTab.ClickAsync();

        ILocator textarea = Page.Locator("#story-arg-graph");
        await Expect(textarea).ToBeEditableAsync();
        await Expect(Page.Locator(".arg-row")).ToHaveCountAsync(1);
        await Expect(Page.Locator("#story-arg-viewWidth")).ToHaveCountAsync(0);
        await Expect(Page.Locator("#story-arg-document")).ToHaveCountAsync(0);

        JsonObject graph = JsonNode.Parse(await textarea.InputValueAsync())!.AsObject();
        JsonArray nodes = graph["nodes"]!.AsArray();
        while (nodes.Count > 2) nodes.RemoveAt(nodes.Count - 1);
        graph["edges"] = new JsonArray();
        string replacement = graph.ToJsonString();

        // Commit immediately, while a cold WebAssembly runtime may still be starting.
        await textarea.FillAsync(replacement);
        await textarea.BlurAsync();

        IFrameLocator runtime = Page.FrameLocator("iframe.story-runtime-frame");
        await runtime.ExpectRuntimeStoryAsync(story, webGpu: true);
        await GalleryPolling.EventuallyAsync(() => runtime.Locator("html").EvaluateAsync<bool>("""
            () => globalThis.luxelBrowserState?.revision >= 1
                && globalThis.luxelBrowserState?.args?.graph?.nodes?.length === 2
                && globalThis.luxelBrowserState?.args?.graph?.edges?.length === 0
                && globalThis.luxelBrowserState?.schema?.map(arg => arg.name).join(',') === 'graph'
                && globalThis.luxelBrowserState?.widgets?.some(widget => widget.type?.endsWith('.NodeGraphView') && widget.detail === '2 ノード')
            """), timeoutMilliseconds: 90_000,
            message: "The graph edit made before runtime readiness was not replayed to the NodeGraph canvas.");

        await Expect(textarea).ToHaveValueAsync(replacement);
        failures.AssertEmpty();
    }

    [Fact]
    public async Task Invalid_json_draft_stays_inline_then_valid_correction_advances_runtime_once()
    {
        await GalleryTestHost.EnsureStartedAsync();
        PageFailures failures = Page.CollectFailures(responses: true);
        const string story = "Controls/Editor/NodeGraphView/Playground";

        await Page.GotoAsync($"{GalleryTestHost.BaseUrl}{story.StoryPath(embed: false)}");
        ILocator argsTab = Page.GetByRole(AriaRole.Tab, new() { Name = "Args" });
        await Expect(argsTab).ToBeVisibleAsync(new() { Timeout = 90_000 });
        await argsTab.ClickAsync();

        ILocator textarea = Page.Locator("#story-arg-graph");
        IFrameLocator runtime = Page.FrameLocator("iframe.story-runtime-frame");
        await runtime.ExpectRuntimeStoryAsync(story, webGpu: true);
        int initialRevision = await runtime.Locator("html").EvaluateAsync<int>(
            "() => globalThis.luxelBrowserState?.revision ?? 0");
        int initialOutputCount = await Page.Locator(".output-list li").CountAsync();
        string accepted = await textarea.InputValueAsync();
        const string invalid = "{\n  \"nodes\": [\n}";

        await textarea.FillAsync(invalid);
        await textarea.BlurAsync();

        await Expect(textarea).ToHaveValueAsync(invalid);
        await Expect(Page.Locator(".raw-json-editor")).ToHaveAttributeAsync("data-status", "invalid");
        await Expect(Page.GetByRole(AriaRole.Alert)).ToContainTextAsync("行");
        await Expect(Page.GetByRole(AriaRole.Alert)).ToContainTextAsync("列");
        await Expect(argsTab).ToHaveAttributeAsync("aria-selected", "true");
        Assert.Equal(initialOutputCount, await Page.Locator(".output-list li").CountAsync());
        Assert.Equal(initialRevision, await runtime.Locator("html").EvaluateAsync<int>(
            "() => globalThis.luxelBrowserState?.revision ?? 0"));

        JsonObject graph = JsonNode.Parse(accepted)!.AsObject();
        JsonArray nodes = graph["nodes"]!.AsArray();
        while (nodes.Count > 2) nodes.RemoveAt(nodes.Count - 1);
        graph["edges"] = new JsonArray();
        string correction = graph.ToJsonString();

        await textarea.FillAsync(correction);
        await textarea.BlurAsync();

        await GalleryPolling.EventuallyAsync(() => runtime.Locator("html").EvaluateAsync<bool>($$"""
            () => globalThis.luxelBrowserState?.revision === {{initialRevision + 1}}
                && globalThis.luxelBrowserState?.args?.graph?.nodes?.length === 2
                && globalThis.luxelBrowserState?.args?.graph?.edges?.length === 0
            """), timeoutMilliseconds: 90_000,
            message: "The corrected JSON arg was not accepted at exactly one new runtime revision.");
        await Task.Delay(500);
        Assert.Equal(initialRevision + 1, await runtime.Locator("html").EvaluateAsync<int>(
            "() => globalThis.luxelBrowserState?.revision ?? 0"));
        await Expect(Page.Locator(".raw-json-editor")).ToHaveAttributeAsync("data-status", "valid");
        await Expect(textarea).ToHaveValueAsync(correction);
        failures.AssertEmpty();
    }
}
