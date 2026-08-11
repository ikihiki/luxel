using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Luxel.Gallery.Browser.E2E.Tests;

public sealed class ResourceStoryTests : Microsoft.Playwright.Xunit.PageTest
{
    public override BrowserNewContextOptions ContextOptions() => GalleryTestHost.ContextOptions();

    public override async Task InitializeAsync()
    {
        await GalleryTestHost.EnsureStartedAsync();
        await base.InitializeAsync();
    }

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
    public async Task ResourceLearnRendersNavigationLiveExampleAndBack()
    {
        var failures = Page.CollectFailures(responses: true);
        const string story = "Learn/Resources/IdentityAndHandles";
        await Page.GotoAsync($"/?story={Uri.EscapeDataString(story)}");
        await Expect(Page.Locator(".markdown-document h1")).ToHaveTextAsync("Identityとhandle");
        await Expect(Page.Locator(".markdown-document a[href*=\"Learn%2FResources%2FResourceManagers\"]")).ToContainTextAsync("Resource manager");
        await Expect(Page.Locator(".markdown-document a[href*=\"Learn%2FResources%2FSourcesAndSteps\"]")).ToContainTextAsync("SourceとStep");
        var embeds = Page.Locator(".markdown-story-embed");
        await Expect(embeds).ToHaveCountAsync(1);
        await Expect(embeds.Locator("header")).ToContainTextAsync("Examples/Resources/SharedRequestIdentity");
        var embedded = Page.Locator(".markdown-story-embed iframe").First.ContentFrame;
        await Expect(embedded.GetByRole(AriaRole.Tab, new() { Name = "引数" })).ToBeVisibleAsync();
        await embedded.GetByRole(AriaRole.Tab, new() { Name = "ソース" }).ClickAsync();
        await Expect(embedded.Locator(".story-source")).ToContainTextAsync("resources.Load<TextAsset>");
        await Expect(embedded.Locator(".story-source")).Not.ToContainTextAsync("ResourceScenarios.Create");
        await Expect(embedded.Locator("#status")).ToHaveAttributeAsync("data-status", "pass", new() { Timeout = 90_000 });
        await embedded.GetByRole(AriaRole.Tab, new() { Name = "出力" }).ClickAsync();
        await Expect(embedded.Locator(".output-list")).ToContainTextAsync("共有request identity: 準備完了");
        await Expect(embedded.Locator(".output-list")).ToContainTextAsync("中間Step実行=1; 単語数=1");
        await embeds.Nth(0).GetByRole(AriaRole.Link, new() { Name = "Storyを開く" }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex("story=Examples%2FResources%2FSharedRequestIdentity"));
        await Expect(Page.Locator(".story-toolbar h1")).ToHaveTextAsync("SharedRequestIdentity");
        await Page.GoBackAsync();
        await Expect(Page).ToHaveURLAsync(new Regex("story=Learn%2FResources%2FIdentityAndHandles"));
        await Expect(Page.Locator(".markdown-document h1")).ToHaveTextAsync("Identityとhandle");
        failures.AssertEmpty();
    }

    [Fact]
    public async Task AssetsLearnEmbedsOneCpuExample()
    {
        var failures = Page.CollectFailures(responses: true);
        const string story = "Learn/Resources/Assets/Overview";
        await Page.GotoAsync($"/?story={Uri.EscapeDataString(story)}");
        await Expect(Page.Locator(".markdown-document h1")).ToHaveTextAsync("アセットの概要");
        var embeds = Page.Locator(".markdown-story-embed");
        await Expect(embeds).ToHaveCountAsync(1);
        await Expect(embeds.Locator("header")).ToContainTextAsync("Examples/Resources/Assets/DocumentInspector");
        await embeds.GetByRole(AriaRole.Link, new() { Name = "Storyを開く" }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex("story=Examples%2FResources%2FAssets%2FDocumentInspector"));
        await Expect(Page.FrameLocator(".story-runtime-frame").Locator("#status")).ToHaveAttributeAsync("data-status", "pass", new() { Timeout = 90_000 });
        await Page.GoBackAsync();
        await Expect(Page.Locator(".markdown-document h1")).ToHaveTextAsync("アセットの概要");
        failures.AssertEmpty();
    }

    [Fact]
    public async Task GltfLearnExposesExamplesWithoutFixtureFailures()
    {
        var failures = Page.CollectFailures(responses: true);
        await Page.GotoAsync($"/?story={Uri.EscapeDataString("Learn/Resources/Gltf/RegistrationAndLoading")}");
        await Expect(Page.Locator(".markdown-document h1")).ToHaveTextAsync("glTFの登録と読み込み");
        await Expect(Page.Locator(".markdown-story-embed")).ToHaveCountAsync(1);
        await Expect(Page.Locator(".markdown-story-embed header")).ToContainTextAsync("Examples/Resources/Gltf/BoxDocumentLoad");
        await Page.GotoAsync($"/?story={Uri.EscapeDataString("Learn/Resources/Gltf/ExternalBuffersImagesAndUris")}");
        await Expect(Page.Locator(".markdown-document h1")).ToHaveTextAsync("外部バッファ、画像、URI");
        await Expect(Page.Locator(".markdown-story-embed")).ToHaveCountAsync(1);
        await Expect(Page.Locator(".markdown-story-embed header")).ToContainTextAsync("Examples/Resources/Gltf/ExternalBufferTrace");
        await Page.GotoAsync($"/?story={Uri.EscapeDataString("Learn/Resources/Gltf/ValidationAndDiagnostics")}");
        await Expect(Page.Locator(".markdown-document h1")).ToHaveTextAsync("検証と診断");
        await Expect(Page.Locator(".markdown-story-embed header").First).ToContainTextAsync("Examples/Resources/Gltf/MalformedAccessorDiagnostics");
        failures.AssertEmpty();
    }

    [Theory]
    [MemberData(nameof(CpuResourceStories))]
    public async Task ExecutesCpuResourceStory(string story)
    {
        var failures = Page.CollectFailures(responses: true);
        await Page.GotoAsync(story.StoryPath());
        await Page.ExpectRuntimeStoryAsync(story);
        await GalleryPolling.EventuallyAsync(() => Page.EvaluateAsync<bool>(
            "() => globalThis.luxelBrowserState?.widgets?.some(widget => widget.detail === '準備完了') || false"));
        failures.AssertEmpty();
    }

    [Fact]
    public async Task ResourceWidgetPublishesOutputAndSource()
    {
        var failures = Page.CollectFailures(responses: true);
        const string story = "Examples/Resources/ReadyBuilder";
        await Page.GotoAsync($"/?story={Uri.EscapeDataString(story)}");
        await Expect(Page.FrameLocator(".story-runtime-frame").Locator("#status")).ToHaveAttributeAsync("data-status", "pass", new() { Timeout = 90_000 });
        await Page.GetByRole(AriaRole.Tab, new() { Name = "出力" }).ClickAsync();
        await Expect(Page.Locator(".output-list")).ToContainTextAsync("readyなbuilder: 準備完了");
        await Expect(Page.Locator(".output-list")).ToContainTextAsync("状態=Ready; 値=HELLO RESOURCES");
        await Page.GetByRole(AriaRole.Tab, new() { Name = "ソース" }).ClickAsync();
        await Expect(Page.Locator(".story-source")).ToContainTextAsync("public static Widget ReadyBuilder");
        await Expect(Page.Locator(".story-source")).ToContainTextAsync("builder.Steps.Add<byte[], TextAsset>");
        await Expect(Page.Locator(".story-source")).Not.ToContainTextAsync("ResourceScenarios.Create");
        failures.AssertEmpty();
    }

    [Theory]
    [MemberData(nameof(GpuResourceStories))]
    public async Task RendersGpuResourceStory(string story)
    {
        var failures = Page.CollectFailures(responses: true);
        await Page.GotoAsync(story.StoryPath());
        await Page.ExpectRuntimeStoryAsync(story, webGpu: true, gpuView: true);
        if (story == "Examples/Resources/Gltf/AnimatedBox")
        {
            var revision = await Page.EvaluateAsync<int>("() => globalThis.luxelBrowserState.renderRevision");
            await GalleryPolling.EventuallyAsync(async () => await Page.EvaluateAsync<int>("() => globalThis.luxelBrowserState.renderRevision") > revision + 9);
        }
        failures.AssertEmpty();
    }

    private static TheoryData<string> Data(params string[] stories)
    {
        var data = new TheoryData<string>();
        foreach (var story in stories)
            data.Add(story);
        return data;
    }
}
