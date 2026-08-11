using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Luxel.Gallery.Browser.E2E.Tests;

public sealed class ResourceStoryTests(BrowserFixture fixture) : GalleryBrowserTest(fixture)
{
    public static TheoryData<string> CpuResourceStories => Data(
        "Examples/Resources/ReadyBuilder", "Examples/Resources/CustomExecutionDomain", "Examples/Resources/SerializedCompilerDomain",
        "Examples/Resources/TypedManagerBinding", "Examples/Resources/SharedRequestIdentity", "Examples/Resources/CustomSourceAndStep",
        "Examples/Resources/DependencyPublication", "Examples/Resources/ScopedRetirement", "Examples/Resources/ReloadKeepsLastGood",
        "Examples/Resources/DomainAndManagerMetrics", "Examples/Resources/WasmCooperativeScheduling",
        "Examples/Resources/Assets/GpuManagerInstallation", "Examples/Resources/Assets/CustomGpuParticleBuffers",
        "Examples/Resources/Assets/CustomGpuStructRetirement", "Examples/Resources/Assets/GpuIndexRecycling",
        "Examples/Resources/Assets/GpuCompaction", "Examples/Resources/Assets/DeviceLostRecovery",
        "Examples/Resources/Assets/DocumentInspector", "Examples/Resources/Assets/MeshPrimitiveInspector",
        "Examples/Resources/Assets/MaterialTextureInspector", "Examples/Resources/Assets/AnimatedSceneGraph",
        "Examples/Resources/Assets/ShaderBufferInspector", "Examples/Resources/Gltf/BoxDocumentLoad",
        "Examples/Resources/Gltf/ExternalBufferTrace", "Examples/Resources/Gltf/MalformedAccessorDiagnostics",
        "Examples/Resources/Gltf/ExternalDependencyReload");

    public static TheoryData<string> GpuResourceStories => Data(
        "Examples/Resources/Gltf/BoxScene", "Examples/Resources/Gltf/AnimatedBox",
        "Examples/Resources/Gltf/RiggedSimpleSkinning", "Examples/Resources/Gltf/MorphWeights");

    [Fact]
    public Task ResourceLearnRendersNavigationLiveExampleAndBack() => RunAsync(nameof(ResourceLearnRendersNavigationLiveExampleAndBack), async page =>
    {
        var failures = CollectFailures(page, responses: true);
        const string story = "Learn/Resources/IdentityAndHandles";
        await page.GotoAsync($"/?story={Uri.EscapeDataString(story)}");
        await Expect(page.Locator(".markdown-document h1")).ToHaveTextAsync("Identityとhandle");
        await Expect(page.Locator(".markdown-document a[href*=\"Learn%2FResources%2FResourceManagers\"]")).ToContainTextAsync("Resource manager");
        await Expect(page.Locator(".markdown-document a[href*=\"Learn%2FResources%2FSourcesAndSteps\"]")).ToContainTextAsync("SourceとStep");
        var embeds = page.Locator(".markdown-story-embed");
        await Expect(embeds).ToHaveCountAsync(1);
        await Expect(embeds.Locator("header")).ToContainTextAsync("Examples/Resources/SharedRequestIdentity");
        var embedded = page.Locator(".markdown-story-embed iframe").First.ContentFrame;
        await Expect(embedded.GetByRole(AriaRole.Tab, new() { Name = "引数" })).ToBeVisibleAsync();
        await embedded.GetByRole(AriaRole.Tab, new() { Name = "ソース" }).ClickAsync();
        await Expect(embedded.Locator(".story-source")).ToContainTextAsync("resources.Load<TextAsset>");
        await Expect(embedded.Locator(".story-source")).Not.ToContainTextAsync("ResourceScenarios.Create");
        await Expect(embedded.Locator("#status")).ToHaveAttributeAsync("data-status", "pass", new() { Timeout = 90_000 });
        await embedded.GetByRole(AriaRole.Tab, new() { Name = "出力" }).ClickAsync();
        await Expect(embedded.Locator(".output-list")).ToContainTextAsync("共有request identity: 準備完了");
        await Expect(embedded.Locator(".output-list")).ToContainTextAsync("中間Step実行=1; 単語数=1");
        await embeds.Nth(0).GetByRole(AriaRole.Link, new() { Name = "Storyを開く" }).ClickAsync();
        await Expect(page).ToHaveURLAsync(new Regex("story=Examples%2FResources%2FSharedRequestIdentity"));
        await Expect(page.Locator(".story-toolbar h1")).ToHaveTextAsync("SharedRequestIdentity");
        await page.GoBackAsync();
        await Expect(page).ToHaveURLAsync(new Regex("story=Learn%2FResources%2FIdentityAndHandles"));
        await Expect(page.Locator(".markdown-document h1")).ToHaveTextAsync("Identityとhandle");
        failures.AssertEmpty();
    });

    [Fact]
    public Task AssetsLearnEmbedsOneCpuExample() => RunAsync(nameof(AssetsLearnEmbedsOneCpuExample), async page =>
    {
        var failures = CollectFailures(page, responses: true);
        const string story = "Learn/Resources/Assets/Overview";
        await page.GotoAsync($"/?story={Uri.EscapeDataString(story)}");
        await Expect(page.Locator(".markdown-document h1")).ToHaveTextAsync("アセットの概要");
        var embeds = page.Locator(".markdown-story-embed");
        await Expect(embeds).ToHaveCountAsync(1);
        await Expect(embeds.Locator("header")).ToContainTextAsync("Examples/Resources/Assets/DocumentInspector");
        await embeds.GetByRole(AriaRole.Link, new() { Name = "Storyを開く" }).ClickAsync();
        await Expect(page).ToHaveURLAsync(new Regex("story=Examples%2FResources%2FAssets%2FDocumentInspector"));
        await Expect(page.FrameLocator(".story-runtime-frame").Locator("#status")).ToHaveAttributeAsync("data-status", "pass", new() { Timeout = 90_000 });
        await page.GoBackAsync();
        await Expect(page.Locator(".markdown-document h1")).ToHaveTextAsync("アセットの概要");
        failures.AssertEmpty();
    });

    [Fact]
    public Task GltfLearnExposesExamplesWithoutFixtureFailures() => RunAsync(nameof(GltfLearnExposesExamplesWithoutFixtureFailures), async page =>
    {
        var failures = CollectFailures(page, responses: true);
        await page.GotoAsync($"/?story={Uri.EscapeDataString("Learn/Resources/Gltf/RegistrationAndLoading")}");
        await Expect(page.Locator(".markdown-document h1")).ToHaveTextAsync("glTFの登録と読み込み");
        await Expect(page.Locator(".markdown-story-embed")).ToHaveCountAsync(1);
        await Expect(page.Locator(".markdown-story-embed header")).ToContainTextAsync("Examples/Resources/Gltf/BoxDocumentLoad");
        await page.GotoAsync($"/?story={Uri.EscapeDataString("Learn/Resources/Gltf/ExternalBuffersImagesAndUris")}");
        await Expect(page.Locator(".markdown-document h1")).ToHaveTextAsync("外部バッファ、画像、URI");
        await Expect(page.Locator(".markdown-story-embed")).ToHaveCountAsync(1);
        await Expect(page.Locator(".markdown-story-embed header")).ToContainTextAsync("Examples/Resources/Gltf/ExternalBufferTrace");
        await page.GotoAsync($"/?story={Uri.EscapeDataString("Learn/Resources/Gltf/ValidationAndDiagnostics")}");
        await Expect(page.Locator(".markdown-document h1")).ToHaveTextAsync("検証と診断");
        await Expect(page.Locator(".markdown-story-embed header").First).ToContainTextAsync("Examples/Resources/Gltf/MalformedAccessorDiagnostics");
        failures.AssertEmpty();
    });

    [Theory]
    [MemberData(nameof(CpuResourceStories))]
    public Task ExecutesCpuResourceStory(string story) => RunAsync(story, async page =>
    {
        var failures = CollectFailures(page, responses: true);
        await page.GotoAsync(StoryPath(story));
        await ExpectRuntimeStoryAsync(page, story);
        await EventuallyAsync(() => page.EvaluateAsync<bool>(
            "() => globalThis.luxelBrowserState?.widgets?.some(widget => widget.detail === '準備完了') || false"));
        failures.AssertEmpty();
    });

    [Fact]
    public Task ResourceWidgetPublishesOutputAndSource() => RunAsync(nameof(ResourceWidgetPublishesOutputAndSource), async page =>
    {
        var failures = CollectFailures(page, responses: true);
        const string story = "Examples/Resources/ReadyBuilder";
        await page.GotoAsync($"/?story={Uri.EscapeDataString(story)}");
        await Expect(page.FrameLocator(".story-runtime-frame").Locator("#status")).ToHaveAttributeAsync("data-status", "pass", new() { Timeout = 90_000 });
        await page.GetByRole(AriaRole.Tab, new() { Name = "出力" }).ClickAsync();
        await Expect(page.Locator(".output-list")).ToContainTextAsync("readyなbuilder: 準備完了");
        await Expect(page.Locator(".output-list")).ToContainTextAsync("状態=Ready; 値=HELLO RESOURCES");
        await page.GetByRole(AriaRole.Tab, new() { Name = "ソース" }).ClickAsync();
        await Expect(page.Locator(".story-source")).ToContainTextAsync("public static Widget ReadyBuilder");
        await Expect(page.Locator(".story-source")).ToContainTextAsync("builder.Steps.Add<byte[], TextAsset>");
        await Expect(page.Locator(".story-source")).Not.ToContainTextAsync("ResourceScenarios.Create");
        failures.AssertEmpty();
    });

    [Theory]
    [MemberData(nameof(GpuResourceStories))]
    public Task RendersGpuResourceStory(string story) => RunAsync(story, async page =>
    {
        var failures = CollectFailures(page, responses: true);
        await page.GotoAsync(StoryPath(story));
        await ExpectRuntimeStoryAsync(page, story, webGpu: true, gpuView: true);
        if (story == "Examples/Resources/Gltf/AnimatedBox")
        {
            var revision = await page.EvaluateAsync<int>("() => globalThis.luxelBrowserState.renderRevision");
            await EventuallyAsync(async () => await page.EvaluateAsync<int>("() => globalThis.luxelBrowserState.renderRevision") > revision + 9);
        }
        failures.AssertEmpty();
    });

    private static TheoryData<string> Data(params string[] stories)
    {
        var data = new TheoryData<string>();
        foreach (var story in stories)
            data.Add(story);
        return data;
    }
}
