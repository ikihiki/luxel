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
}
