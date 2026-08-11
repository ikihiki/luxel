using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Luxel.Gallery.Browser.E2E.Tests;

public sealed class Gallery2DStoryTests : Microsoft.Playwright.Xunit.PageTest
{
    public override BrowserNewContextOptions ContextOptions() => GalleryTestHost.ContextOptions();

    public override async Task InitializeAsync()
    {
        await GalleryTestHost.EnsureStartedAsync();
        await base.InitializeAsync();
        await Context.StartGalleryTracingAsync();
    }

    public override async Task DisposeAsync()
    {
        if (Context is not null)
            await Context.FinishGalleryTracingAsync(Page, TestOk, GetType().Name);
        await base.DisposeAsync();
    }

    public static TheoryData<string> TwoDStories => Data(
        "Examples/2D/SceneRender", "Examples/2D/Shapes", "Examples/2D/VectorPaths", "Examples/2D/CameraRig",
        "Examples/2D/Sprites", "Examples/2D/Rasterizer/InputPathsLive", "Examples/2D/Rasterizer/EncodedSceneLive",
        "Examples/2D/Rasterizer/BoundsLive", "Examples/2D/Rasterizer/TileBinsLive", "Examples/2D/Rasterizer/CoverageLive",
        "Examples/2D/Rasterizer/StrokeLive", "Examples/2D/Rasterizer/CompositeLive", "Examples/2D/Rasterizer/DispatchLive",
        "Examples/2D/Rasterizer/RetainedUpdatesLive");

    public static TheoryData<string> EcsStories => Data(
        "Examples/3D/EcsCubes", "Examples/3D/PhysicsFalling", "Examples/3D/PhysicsPlayground",
        "Examples/3D/PhysicsGizmos", "Examples/3D/PhysicsTrigger", "Examples/3D/PhysicsMesh");

    public static TheoryData<string> PipelineStateStories => Data(
        "Examples/3D/PipelineState/Topology", "Examples/3D/PipelineState/Rasterizer", "Examples/3D/PipelineState/Depth",
        "Examples/3D/PipelineState/Blend", "Examples/3D/PipelineState/Stencil", "Examples/3D/PipelineState/ViewportScissor",
        "Examples/3D/PipelineState/Separation", "Examples/3D/Depth", "Examples/3D/Blend");

    public static TheoryData<string> AnimationStories => Data(
        "Examples/Animation/Curves", "Examples/Animation/Tween", "Examples/Animation/CssKeyframes",
        "Examples/Animation/StateMachine", "Examples/Animation/EcsClip", "Examples/Animation/Graph");

    private static readonly HashSet<string> EcsGpuViewStories =
        ["Examples/3D/EcsCubes", "Examples/3D/PhysicsFalling", "Examples/3D/PhysicsPlayground"];
    private static readonly HashSet<string> AnimationGpuStories =
        ["Examples/Animation/CssKeyframes", "Examples/Animation/StateMachine"];
    private static readonly HashSet<string> AnimationMotionStories =
        ["Examples/Animation/Curves", "Examples/Animation/Tween", "Examples/Animation/CssKeyframes", "Examples/Animation/EcsClip", "Examples/Animation/Graph"];

    [Fact]
    public async Task ShowsLoadingProgressBeforeWebAssemblyStarts()
    {
        await Page.RouteAsync("**/_framework/blazor.webassembly*.js", route => route.AbortAsync());
        await Page.GotoAsync("/");
        var loading = Page.Locator(".loading-progress");
        await Expect(loading).ToBeVisibleAsync();
        await Expect(loading).ToHaveAttributeAsync("role", "status");
        await Expect(loading).ToContainTextAsync("Galleryを読み込んでいます");
        await Expect(loading.Locator("svg")).ToBeVisibleAsync();
        await Expect(loading.Locator(".loading-progress-text")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task RendersMarkdownOverviewsWithNavigationAndSearch()
    {
        const string story = "Controls/Accordion/Overview";
        var failures = Page.CollectFailures();
        await Page.GotoAsync($"/?story={Uri.EscapeDataString(story)}");
        await Expect(Page.Locator(".gallery-sidebar")).ToBeVisibleAsync();
        await Expect(Page.Locator(".story-link.active")).ToHaveTextAsync(new Regex("Overview"));
        await Expect(Page.Locator(".story-tree summary").Filter(new() { HasText = "Accordion" })).ToBeVisibleAsync();
        await Expect(Page.Locator(".markdown-document h1")).ToHaveTextAsync("Accordion");
        await Expect(Page.Locator(".markdown-document")).ToContainTextAsync("Implementation");
        await Expect(Page.Locator(".markdown-story-embed iframe")).ToHaveCountAsync(1);

        var main = Page.Locator(".gallery-main-document");
        var article = Page.Locator(".markdown-document");
        await GalleryPolling.EventuallyAsync(() => main.EvaluateAsync<bool>("element => element.scrollHeight > element.clientHeight"));
        await Expect(article).ToHaveCSSAsync("overflow", "visible");
        var toolbarTop = await Page.Locator(".story-toolbar").EvaluateAsync<float>("element => element.getBoundingClientRect().top");
        await main.EvaluateAsync("element => { element.scrollTop = 320; }");
        await GalleryPolling.EventuallyAsync(async () => await main.EvaluateAsync<float>("element => element.scrollTop") > 0);
        await GalleryPolling.EventuallyAsync(async () => Math.Abs(await Page.Locator(".story-toolbar").EvaluateAsync<float>("element => element.getBoundingClientRect().top") - toolbarTop) < 1);
        Assert.Equal(0, await article.EvaluateAsync<int>("element => element.scrollTop"));

        var embedded = Page.FrameLocator(".markdown-story-embed iframe");
        await Expect(embedded.GetByRole(AriaRole.Tab, new() { Name = "引数" })).ToBeVisibleAsync();
        await Expect(embedded.GetByRole(AriaRole.Tab, new() { Name = "出力" })).ToBeVisibleAsync();
        await Expect(embedded.GetByRole(AriaRole.Tab, new() { Name = "ソース" })).ToBeVisibleAsync();
        await Expect(embedded.Locator("#status")).ToHaveAttributeAsync("data-status", "pass", new() { Timeout = 90_000 });
        await Expect(embedded.Locator("#status")).ToHaveAttributeAsync("data-story", "Controls/Accordion/Basic");

        var search = Page.GetByRole(AriaRole.Searchbox, new() { Name = "Storyを検索" });
        await search.FillAsync("Accordion");
        await Expect(Page.Locator(".story-link")).ToHaveCountAsync(2);
        Assert.Contains("Overview", await Page.Locator(".story-link").Nth(0).InnerTextAsync() + await Page.Locator(".story-link").Nth(1).InnerTextAsync());
        Assert.Contains("Basic", await Page.Locator(".story-link").Nth(0).InnerTextAsync() + await Page.Locator(".story-link").Nth(1).InnerTextAsync());
        await Page.Locator(".story-link[title=\"Controls/Accordion/Basic\"]").ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex("story=Controls%2FAccordion%2FBasic"));
        await Expect(search).ToHaveValueAsync("Accordion");
        await Expect(Page.Locator(".story-toolbar h1")).ToHaveTextAsync("Basic");
        await Expect(Page.Locator(".gallery-sidebar")).ToBeVisibleAsync();
        var runtime = Page.FrameLocator(".story-runtime-frame");
        await Expect(runtime.Locator("#status")).ToHaveAttributeAsync("data-status", "pass", new() { Timeout = 90_000 });
        await Expect(runtime.Locator("#status")).ToHaveAttributeAsync("data-story", "Controls/Accordion/Basic");
        await Page.GoBackAsync();
        await Expect(Page).ToHaveURLAsync(new Regex("story=Controls%2FAccordion%2FOverview"));
        await Expect(search).ToHaveValueAsync("Accordion");
        await Expect(Page.Locator(".markdown-document h1")).ToHaveTextAsync("Accordion");
        await search.FillAsync("no-such-luxel-story");
        await Expect(Page.Locator(".empty-search")).ToBeVisibleAsync();
        Assert.Empty(failures.ConsoleErrors);
        Assert.Empty(failures.PageErrors);
    }

    [Fact]
    public async Task ExposesArgsOutputSourceAndResizablePreview()
    {
        const string story = "Controls/Button/Counter";
        var failures = Page.CollectFailures();
        await Page.GotoAsync($"/?story={Uri.EscapeDataString(story)}");
        var runtime = Page.FrameLocator(".story-runtime-frame");
        await Expect(runtime.Locator("#status")).ToHaveAttributeAsync("data-status", "pass", new() { Timeout = 90_000 });
        await Expect(Page.GetByRole(AriaRole.Tab, new() { Name = "引数" })).ToHaveAttributeAsync("aria-selected", "true");
        var count = Page.Locator("#story-arg-count");
        await Expect(count).ToHaveValueAsync("0");
        await count.FillAsync("7");
        await count.BlurAsync();
        await GalleryPolling.EventuallyAsync(async () => await runtime.Locator("html").EvaluateAsync<int>("() => globalThis.luxelBrowserState?.count") == 7);
        await Page.GetByRole(AriaRole.Tab, new() { Name = "出力" }).ClickAsync();
        await Expect(Page.Locator(".output-list")).ToContainTextAsync("引数を変更しました");
        await Page.GetByRole(AriaRole.Tab, new() { Name = "ソース" }).ClickAsync();
        await Expect(Page.Locator(".story-source")).ToContainTextAsync("ButtonCounter");

        var splitter = Page.GetByRole(AriaRole.Separator, new() { Name = "Storyプレビューと詳細の大きさを変更" });
        var panel = Page.Locator(".story-lower-panel");
        var before = await panel.BoundingBoxAsync();
        var handle = await splitter.BoundingBoxAsync();
        Assert.NotNull(before);
        Assert.NotNull(handle);
        await Page.Mouse.MoveAsync(handle.X + handle.Width / 2, handle.Y + handle.Height / 2);
        await Page.Mouse.DownAsync();
        await Page.Mouse.MoveAsync(handle.X + handle.Width / 2, handle.Y - 70, new() { Steps = 4 });
        await Page.Mouse.UpAsync();
        var after = await panel.BoundingBoxAsync();
        Assert.NotNull(after);
        Assert.True(after.Height > before.Height + 40);
        Assert.Empty(failures.PageErrors);

        await Page.Locator(".story-link[title=\"Controls/Button/Primary\"]").ClickAsync();
        await Expect(Page.Locator(".story-toolbar h1")).ToHaveTextAsync("Primary");
        await Expect(runtime.Locator("#status")).ToHaveAttributeAsync("data-status", "pass", new() { Timeout = 90_000 });
        await Expect(runtime.Locator("#status")).ToHaveAttributeAsync("data-story", "Controls/Button/Primary");
        await Expect(Page.GetByRole(AriaRole.Tab, new() { Name = "ソース" })).ToHaveAttributeAsync("aria-selected", "true");
        await Page.GetByRole(AriaRole.Tab, new() { Name = "出力" }).ClickAsync();
        await Expect(Page.Locator(".output-list")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task CompactStoriesExposeInteractivePanels()
    {
        const string story = "Controls/Button/Counter";
        await Page.GotoAsync($"/?story={Uri.EscapeDataString(story)}&compact=1");
        await Expect(Page.Locator(".gallery-compact")).ToBeVisibleAsync();
        await Expect(Page.Locator(".gallery-sidebar")).ToHaveCountAsync(0);
        await Expect(Page.Locator("#status")).ToHaveAttributeAsync("data-status", "pass", new() { Timeout = 90_000 });
        var count = Page.Locator("#story-arg-count");
        await count.FillAsync("4");
        await count.BlurAsync();
        await GalleryPolling.EventuallyAsync(async () => await Page.EvaluateAsync<int>("() => globalThis.luxelBrowserState?.count") == 4);
        await Page.GetByRole(AriaRole.Tab, new() { Name = "出力" }).ClickAsync();
        await Expect(Page.Locator(".output-list")).ToContainTextAsync("引数を変更しました");
        await Page.GetByRole(AriaRole.Tab, new() { Name = "ソース" }).ClickAsync();
        await Expect(Page.Locator(".story-source")).ToContainTextAsync("ButtonCounter");
    }

    [Fact]
    public async Task EmbeddedWidgetStoriesRemainCanvasOnly()
    {
        await Page.GotoAsync("Controls/Button/Counter".StoryPath());
        await Expect(Page.Locator(".gallery-embed")).ToBeVisibleAsync();
        await Expect(Page.Locator(".gallery-sidebar")).ToHaveCountAsync(0);
        await Expect(Page.GetByRole(AriaRole.Tab)).ToHaveCountAsync(0);
        await Expect(Page.GetByRole(AriaRole.Separator)).ToHaveCountAsync(0);
    }

    [Theory]
    [MemberData(nameof(TwoDStories))]
    public Task RendersTwoDStory(string story) => VerifyGpuStoryAsync(story);

    [Theory]
    [MemberData(nameof(EcsStories))]
    public Task RendersEcsStory(string story) => VerifyGpuStoryAsync(story, EcsGpuViewStories.Contains(story));

    [Theory]
    [MemberData(nameof(PipelineStateStories))]
    public Task RendersPipelineStateStory(string story) => VerifyGpuStoryAsync(story, gpuView: true);

    [Theory]
    [MemberData(nameof(AnimationStories))]
    public async Task RendersAnimationStory(string story)
    {
        var failures = Page.CollectFailures();
        await Page.GotoAsync(story.StoryPath());
        await Page.ExpectRuntimeStoryAsync(story, webGpu: true, statusText: true);
        if (AnimationGpuStories.Contains(story))
            await ExpectGpuViewReadyAsync(Page);
        if (AnimationMotionStories.Contains(story))
        {
            for (var sample = 0; sample < 4; sample++)
            {
                var revision = await Page.EvaluateAsync<int>("() => globalThis.luxelBrowserState.renderRevision");
                await GalleryPolling.EventuallyAsync(async () => await Page.EvaluateAsync<int>("() => globalThis.luxelBrowserState.renderRevision") > revision + 9);
            }
        }
        Assert.Empty(failures.ConsoleErrors);
        Assert.Empty(failures.PageErrors);
    }

    [Fact]
    public async Task StateMachineRespondsToPressAndDoneTriggers()
    {
        var failures = Page.CollectFailures();
        const string story = "Examples/Animation/StateMachine";
        await Page.GotoAsync(story.StoryPath());
        await Page.ExpectRuntimeStoryAsync(story, webGpu: true, statusText: true);
        var canvas = Page.Locator("#luxel-canvas");
        var idle = await canvas.ScreenshotAsync();
        var press = await Page.FindCanvasWidgetAsync("press");
        await Page.Mouse.ClickAsync(press.X + press.Width / 2, press.Y + press.Height / 2);
        var pressRevision = await Page.EvaluateAsync<int>("() => globalThis.luxelBrowserState.renderRevision");
        await GalleryPolling.EventuallyAsync(async () => await Page.EvaluateAsync<int>("() => globalThis.luxelBrowserState.renderRevision") > pressRevision + 2);
        var jumping = await canvas.ScreenshotAsync();
        Assert.False(idle.SequenceEqual(jumping), "press should change the StateMachine rendering");
        var done = await Page.FindCanvasWidgetAsync("done");
        await Page.Mouse.ClickAsync(done.X + done.Width / 2, done.Y + done.Height / 2);
        await GalleryPolling.EventuallyAsync(() => Page.EvaluateAsync<bool>("() => globalThis.luxelBrowserState.events.some(entry => String(entry.message || entry).includes('done'))"));
        Assert.Empty(failures.ConsoleErrors);
        Assert.Empty(failures.PageErrors);
    }

    private async Task VerifyGpuStoryAsync(string story, bool gpuView = false)
    {
        var failures = Page.CollectFailures();
        await Page.GotoAsync(story.StoryPath());
        await Page.ExpectRuntimeStoryAsync(story, webGpu: true, statusText: true);
        if (gpuView)
            await ExpectGpuViewReadyAsync(Page);
        Assert.Empty(failures.ConsoleErrors);
        Assert.Empty(failures.PageErrors);
    }

    private static Task ExpectGpuViewReadyAsync(IPage page) => GalleryPolling.EventuallyAsync(async () =>
        (await page.EvaluateAsync<string>("() => globalThis.luxelBrowserState?.widgets?.find(widget => widget.type?.endsWith('.GpuView'))?.detail || ''"))
        .Contains("Ready", StringComparison.Ordinal), 90_000);

    private static TheoryData<string> Data(params string[] stories)
    {
        var data = new TheoryData<string>();
        foreach (var story in stories)
            data.Add(story);
        return data;
    }
}
