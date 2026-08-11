using System.Text.Json;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Luxel.Gallery.Browser.E2E.Tests;

internal static class GalleryPageExtensions
{
    public static string StoryPath(this string story, bool embed = true) =>
        $"/?story={Uri.EscapeDataString(story)}{(embed ? "&embed=1" : string.Empty)}";

    public static PageFailures CollectFailures(this IPage page, bool responses = false) => new(page, responses);

    public static Task ExpectRuntimeStoryAsync(
        this IPage page,
        string story,
        bool webGpu = false,
        bool gpuView = false,
        bool statusText = false,
        bool noCapabilityFallback = false) =>
        ExpectRuntimeStoryAsync(new PageTarget(page), story, webGpu, gpuView, statusText, noCapabilityFallback);

    public static Task ExpectRuntimeStoryAsync(
        this IFrameLocator frame,
        string story,
        bool webGpu = false,
        bool gpuView = false,
        bool statusText = false,
        bool noCapabilityFallback = false) =>
        ExpectRuntimeStoryAsync(new FrameTarget(frame), story, webGpu, gpuView, statusText, noCapabilityFallback);

    public static async Task ClickCanvasWidgetAsync(this IPage page, string? detail = null, string type = "Button", int index = 0)
    {
        var widget = await page.FindCanvasWidgetAsync(detail, type, index);
        await page.Locator("#luxel-canvas").ClickAsync(new LocatorClickOptions
        {
            Position = new Position { X = widget.X + widget.Width / 2, Y = widget.Y + widget.Height / 2 }
        });
    }

    public static async Task<CanvasWidget> FindCanvasWidgetAsync(this IPage page, string? detail = null, string type = "Button", int index = 0)
    {
        CanvasWidget? widget = null;
        await GalleryPolling.EventuallyAsync(async () =>
        {
            widget = await page.EvaluateAsync<CanvasWidget?>(
                "query => globalThis.luxelBrowserState?.widgets?.filter(widget => widget.type?.endsWith(`.${query.type}`) && (query.detail === null || widget.detail === query.detail))[query.index] || null",
                new { detail, type, index });
            return widget is not null;
        });
        return widget!;
    }

    public static Task ExpectWidgetDetailAsync(this IPage page, string text, int timeoutMilliseconds = 90_000) =>
        GalleryPolling.EventuallyAsync(() => page.EvaluateAsync<bool>(
            "expected => globalThis.luxelBrowserState?.widgets?.some(widget => widget.detail?.includes(expected)) || false", text),
            timeoutMilliseconds);

    private static async Task ExpectRuntimeStoryAsync(
        ITarget target,
        string story,
        bool webGpu,
        bool gpuView,
        bool statusText,
        bool noCapabilityFallback)
    {
        var status = target.Locator("#status");
        await Expect(status).ToHaveAttributeAsync("data-story", story, new() { Timeout = 90_000 });
        await Expect(status).ToHaveAttributeAsync("data-status", "pass", new() { Timeout = 90_000 });
        if (statusText)
            await Expect(status).ToContainTextAsync($"story={story}");
        await Expect(target.Locator("#error")).ToBeHiddenAsync();
        await Expect(target.Locator("#luxel-canvas")).ToBeVisibleAsync();
        var root = target.Locator("html");
        await GalleryPolling.EventuallyAsync(async () => await root.EvaluateAsync<int>("() => globalThis.luxelBrowserState?.renderRevision || 0") > 0);
        await GalleryPolling.EventuallyAsync(async () => await root.EvaluateAsync<int>("() => globalThis.luxelBrowserState?.widgets?.length || 0") > 0);
        if (noCapabilityFallback)
        {
            await GalleryPolling.EventuallyAsync(async () => !await root.EvaluateAsync<bool>(
                "() => globalThis.luxelBrowserState?.widgets?.some(widget => widget.type?.endsWith('.StoryCapabilityFallback')) || false"));
        }
        if (gpuView)
        {
            await GalleryPolling.EventuallyAsync(async () => (await root.EvaluateAsync<string>(
                "() => globalThis.luxelBrowserState?.widgets?.find(widget => widget.type?.endsWith('.GpuView'))?.detail || ''")).Contains("Ready", StringComparison.Ordinal),
                timeoutMilliseconds: 90_000);
        }
        if (webGpu)
        {
            var gpu = await root.EvaluateAsync<JsonElement>("() => globalThis.luxelBrowserState?.webGpu");
            Assert.True(gpu.TryGetProperty("adapter", out var adapter) && adapter.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined);
            Assert.Equal("ready", gpu.GetProperty("device").GetProperty("status").GetString());
            Assert.True(gpu.GetProperty("surface").GetProperty("presentCount").GetInt32() > 0);
            Assert.Equal(JsonValueKind.Null, gpu.GetProperty("lastError").ValueKind);
        }
    }


    private interface ITarget
    {
        ILocator Locator(string selector);
    }

    private sealed class PageTarget(IPage page) : ITarget
    {
        public ILocator Locator(string selector) => page.Locator(selector);
    }

    private sealed class FrameTarget(IFrameLocator frame) : ITarget
    {
        public ILocator Locator(string selector) => frame.Locator(selector);
    }
}

internal static class GalleryPolling
{
    public static async Task EventuallyAsync(Func<Task<bool>> condition, int timeoutMilliseconds = 30_000, string? message = null)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMilliseconds);
        while (DateTime.UtcNow < deadline)
        {
            if (await condition())
                return;
            await Task.Delay(100);
        }
        throw new Xunit.Sdk.XunitException(message ?? $"Condition was not satisfied within {timeoutMilliseconds} ms.");
    }
}

internal sealed class PageFailures
{
    public PageFailures(IPage page, bool responses)
    {
        page.Console += (_, message) =>
        {
            if (message.Type == "error")
                ConsoleErrors.Add(message.Text);
        };
        page.PageError += (_, error) => PageErrors.Add(error);
        if (responses)
        {
            page.Response += (_, response) =>
            {
                if (response.Status >= 400)
                    FailedResponses.Add($"{response.Status} {response.Url}");
            };
        }
    }

    public List<string> ConsoleErrors { get; } = [];
    public List<string> PageErrors { get; } = [];
    public List<string> FailedResponses { get; } = [];

    public void AssertEmpty()
    {
        Assert.Empty(ConsoleErrors);
        Assert.Empty(PageErrors);
        Assert.Empty(FailedResponses);
    }
}

internal sealed record CanvasWidget(
    [property: System.Text.Json.Serialization.JsonPropertyName("x")] float X,
    [property: System.Text.Json.Serialization.JsonPropertyName("y")] float Y,
    [property: System.Text.Json.Serialization.JsonPropertyName("width")] float Width,
    [property: System.Text.Json.Serialization.JsonPropertyName("height")] float Height);
