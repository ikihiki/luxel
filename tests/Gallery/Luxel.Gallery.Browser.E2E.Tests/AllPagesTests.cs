using System.Collections.Concurrent;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Luxel.Gallery.Browser.E2E.Tests;

public sealed class AllPagesTests : Microsoft.Playwright.Xunit.BrowserTest
{
    public override async Task InitializeAsync()
    {
        await GalleryTestHost.EnsureStartedAsync();
        await base.InitializeAsync();
    }

    private static readonly string[] NativeOnlyRoutes =
    [
        "Apps/Studio/Shell",
        "Learn/Production/StudioToPlayer",
        "Learn/Production/ValidateAndShip",
        "Learn/Production/Workbench",
        "Examples/Scripting/NativeHotReload",
        "Examples/Scripting/Repl"
    ];

    private static readonly HashSet<string> ApprovedFallbacks =
    [
        "Apps/Player/Basic",
        "Apps/Player/ScriptEditor",
        "Apps/Player/ThreeD",
        "Controls/TextEditorView/Code",
        "Controls/TextEditorView/Completion",
        "Examples/2D/Backends",
        "Examples/3D/TexturedQuad",
        "Game/Cavern",
        "Internals/Authoring",
        "Learn/Graphics/2D/Backends",
        "Reference/Luxel.Controls",
        "Controls/Canvas2D/Basic",
        "Controls/Canvas2D/Overview",
        "Controls/Grid/Basic",
        "Controls/Grid/Overview",
        "Controls/GpuView/Basic",
        "Controls/GpuView/Overview",
        "Controls/ImageBlock/Basic",
        "Controls/ImageBlock/Overview",
        "Controls/KnobsTable/Basic",
        "Controls/KnobsTable/Overview",
        "Controls/ParticleView/Basic",
        "Controls/ParticleView/Overview",
        "Controls/RichTextView/Basic",
        "Controls/RichTextView/Overview",
        "Controls/SceneInspector/Basic",
        "Controls/SceneInspector/Overview"
    ];

    [Fact(Timeout = 20 * 60_000)]
    public async Task EveryBlazorGalleryPageRendersOrReachesBrowserSafeFallback()
    {
        await using var discoveryContext = await NewContext(GalleryTestHost.ContextOptions());
        var discovery = await discoveryContext.NewPageAsync();
        await discovery.GotoAsync("/");
        await GalleryPolling.EventuallyAsync(async () => await discovery.Locator(".story-link").CountAsync() > 0, 90_000);
        var routes = (await discovery.Locator(".story-link").EvaluateAllAsync<string[]>(
                "links => links.map(link => link.title)"))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(505, routes.Length);
        foreach (var route in NativeOnlyRoutes)
            Assert.DoesNotContain(route, routes);

        var failures = new ConcurrentQueue<string>();
        var next = -1;
        // Leave capacity for the focused PageTest class running alongside this audit.
        // Four software-WebGPU contexts keep CI stable without serializing the suite.
        var auditContexts = new IBrowserContext[4];
        for (var index = 0; index < auditContexts.Length; index++)
            auditContexts[index] = await NewContext(GalleryTestHost.ContextOptions());

        async Task AuditPagesAsync(IBrowserContext context)
        {
            context.SetDefaultTimeout(90_000);
            context.SetDefaultNavigationTimeout(90_000);
            while (true)
            {
                var index = Interlocked.Increment(ref next);
                if (index >= routes.Length)
                    return;
                var story = routes[index];
                var page = await context.NewPageAsync();
                try
                {
                    await page.GotoAsync($"/?story={Uri.EscapeDataString(story)}", new()
                    {
                        WaitUntil = WaitUntilState.DOMContentLoaded,
                        Timeout = 90_000
                    });
                    await page.Locator(".gallery-shell,.gallery-compact,.gallery-embed").First.WaitForAsync(new() { Timeout = 90_000 });
                    if (await page.Locator(".markdown-document").CountAsync() > 0)
                    {
                        var unavailable = await page.Locator(".markdown-embed-unavailable").CountAsync();
                        if (unavailable > 0)
                            failures.Enqueue($"{story}: {unavailable} unavailable Markdown embed(s)");
                        var frames = page.Locator(".markdown-story-embed iframe");
                        var frameCount = await frames.CountAsync();
                        for (var frameIndex = 0; frameIndex < frameCount; frameIndex++)
                        {
                            var source = await frames.Nth(frameIndex).GetAttributeAsync("src");
                            var embeddedStory = GetQueryValue(new Uri(new Uri(GalleryTestHost.BaseUrl), source), "story") ?? string.Empty;
                            var status = frames.Nth(frameIndex).ContentFrame.Locator("#status");
                            try
                            {
                                await WaitForRuntimeAsync(status, embeddedStory);
                            }
                            catch (Exception exception)
                            {
                                failures.Enqueue($"{story}: embed {frameIndex}: {exception.Message}");
                            }
                        }
                    }
                    else
                    {
                        try
                        {
                            await WaitForRuntimeAsync(page.FrameLocator(".story-runtime-frame").Locator("#status"), story);
                        }
                        catch (Exception exception)
                        {
                            failures.Enqueue($"{story}: {exception.Message}");
                        }
                    }
                }
                catch (Exception exception)
                {
                    failures.Enqueue($"{story}: {exception.Message}");
                }
                finally
                {
                    await page.CloseAsync();
                }
            }
        }

        try
        {
            await Task.WhenAll(auditContexts.Select(AuditPagesAsync));
            Assert.True(failures.IsEmpty, string.Join(Environment.NewLine, failures));
        }
        finally
        {
            await Task.WhenAll(auditContexts.Select(context => context.DisposeAsync().AsTask()));
        }
    }

    private async Task WaitForRuntimeAsync(ILocator status, string story)
    {
        await Expect(status).ToHaveAttributeAsync("data-story", story, new() { Timeout = 90_000 });
        await Expect(status).ToHaveAttributeAsync("data-status", "pass", new() { Timeout = 90_000 });
        var fallback = await status.EvaluateAsync<string?>(
            "() => { const widget = globalThis.luxelBrowserState?.widgets?.find(widget => widget.type?.endsWith('.StoryCapabilityFallback')); return widget ? JSON.stringify(widget) : null; }");
        if (fallback is not null && !ApprovedFallbacks.Contains(story))
            throw new Xunit.Sdk.XunitException($"unexpected StoryCapabilityFallback: {fallback}");
    }

    private static string? GetQueryValue(Uri uri, string name)
    {
        foreach (var part in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = part.Split('=', 2);
            if (Uri.UnescapeDataString(pair[0]) == name)
                return pair.Length == 2 ? Uri.UnescapeDataString(pair[1]) : string.Empty;
        }
        return null;
    }
}
