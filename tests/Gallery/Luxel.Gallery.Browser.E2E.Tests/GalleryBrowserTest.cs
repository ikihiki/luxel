using System.Text.Json;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Luxel.Gallery.Browser.E2E.Tests;

[Collection(BrowserCollection.Name)]
public abstract class GalleryBrowserTest(BrowserFixture fixture)
{
    protected BrowserFixture Fixture { get; } = fixture;

    protected async Task RunAsync(string testName, Func<IPage, Task> test)
    {
        await using var context = await Fixture.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = Fixture.BaseUrl
        });
        context.SetDefaultTimeout(90_000);
        context.SetDefaultNavigationTimeout(90_000);
        await context.Tracing.StartAsync(new TracingStartOptions
        {
            Screenshots = true,
            Snapshots = true,
            Sources = true
        });
        var page = await context.NewPageAsync();
        try
        {
            await test(page);
            await context.Tracing.StopAsync();
        }
        catch
        {
            var artifactDirectory = Path.Combine(
                Fixture.RepositoryRoot,
                "artifacts",
                "gallery-browser-e2e",
                Sanitize(testName));
            Directory.CreateDirectory(artifactDirectory);
            try
            {
                await page.ScreenshotAsync(new PageScreenshotOptions
                {
                    FullPage = true,
                    Path = Path.Combine(artifactDirectory, "failure.png")
                });
            }
            catch { }
            try
            {
                await context.Tracing.StopAsync(new TracingStopOptions
                {
                    Path = Path.Combine(artifactDirectory, "trace.zip")
                });
            }
            catch { }
            throw;
        }
    }

    protected static string StoryPath(string story, bool embed = true) =>
        $"/?story={Uri.EscapeDataString(story)}{(embed ? "&embed=1" : string.Empty)}";

    protected static PageFailures CollectFailures(IPage page, bool responses = false) => new(page, responses);

    protected static async Task ExpectRuntimeStoryAsync(
        IPage page,
        string story,
        bool webGpu = false,
        bool gpuView = false,
        bool statusText = false,
        bool noCapabilityFallback = false) =>
        await ExpectRuntimeStoryAsync(new PageTarget(page), story, webGpu, gpuView, statusText, noCapabilityFallback);

    protected static async Task ExpectRuntimeStoryAsync(
        IFrameLocator frame,
        string story,
        bool webGpu = false,
        bool gpuView = false,
        bool statusText = false,
        bool noCapabilityFallback = false) =>
        await ExpectRuntimeStoryAsync(new FrameTarget(frame), story, webGpu, gpuView, statusText, noCapabilityFallback);

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
        await EventuallyAsync(async () => await root.EvaluateAsync<int>("() => globalThis.luxelBrowserState?.renderRevision || 0") > 0);
        await EventuallyAsync(async () => await root.EvaluateAsync<int>("() => globalThis.luxelBrowserState?.widgets?.length || 0") > 0);
        if (noCapabilityFallback)
        {
            await EventuallyAsync(async () => !await root.EvaluateAsync<bool>(
                "() => globalThis.luxelBrowserState?.widgets?.some(widget => widget.type?.endsWith('.StoryCapabilityFallback')) || false"));
        }
        if (gpuView)
        {
            await EventuallyAsync(async () => (await root.EvaluateAsync<string>(
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

    protected static async Task ClickCanvasWidgetAsync(IPage page, string? detail = null, string type = "Button", int index = 0)
    {
        var widget = await FindCanvasWidgetAsync(page, detail, type, index);
        await page.Locator("#luxel-canvas").ClickAsync(new LocatorClickOptions
        {
            Position = new Position { X = widget.X + widget.Width / 2, Y = widget.Y + widget.Height / 2 }
        });
    }

    protected static async Task<CanvasWidget> FindCanvasWidgetAsync(IPage page, string? detail = null, string type = "Button", int index = 0)
    {
        CanvasWidget? widget = null;
        await EventuallyAsync(async () =>
        {
            widget = await page.EvaluateAsync<CanvasWidget?>(
                "query => globalThis.luxelBrowserState?.widgets?.filter(widget => widget.type?.endsWith(`.${query.type}`) && (query.detail === null || widget.detail === query.detail))[query.index] || null",
                new { detail, type, index });
            return widget is not null;
        });
        return widget!;
    }

    protected static async Task ExpectWidgetDetailAsync(IPage page, string text, int timeoutMilliseconds = 90_000) =>
        await EventuallyAsync(() => page.EvaluateAsync<bool>(
            "expected => globalThis.luxelBrowserState?.widgets?.some(widget => widget.detail?.includes(expected)) || false", text),
            timeoutMilliseconds);

    protected static async Task EventuallyAsync(Func<Task<bool>> condition, int timeoutMilliseconds = 30_000, string? message = null)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMilliseconds);
        Exception? lastError = null;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                if (await condition())
                    return;
            }
            catch (Exception exception)
            {
                lastError = exception;
            }
            await Task.Delay(100);
        }
        throw new Xunit.Sdk.XunitException(message ?? $"Condition was not satisfied within {timeoutMilliseconds} ms.{(lastError is null ? string.Empty : $" Last error: {lastError.Message}")}");
    }

    private static string Sanitize(string value) => string.Concat(value.Select(character =>
        Path.GetInvalidFileNameChars().Contains(character) || character is '/' or '\\' or ':' ? '_' : character));

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

public sealed class PageFailures
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

public sealed record CanvasWidget(
    [property: System.Text.Json.Serialization.JsonPropertyName("x")] float X,
    [property: System.Text.Json.Serialization.JsonPropertyName("y")] float Y,
    [property: System.Text.Json.Serialization.JsonPropertyName("width")] float Width,
    [property: System.Text.Json.Serialization.JsonPropertyName("height")] float Height);
