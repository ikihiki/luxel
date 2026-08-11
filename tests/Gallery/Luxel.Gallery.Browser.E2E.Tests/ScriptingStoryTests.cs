using Microsoft.Playwright;

namespace Luxel.Gallery.Browser.E2E.Tests;

public sealed class ScriptingStoryTests : Microsoft.Playwright.Xunit.PageTest
{
    public override BrowserNewContextOptions ContextOptions() => GalleryTestHost.ContextOptions();

    public override async Task InitializeAsync()
    {
        await GalleryTestHost.EnsureStartedAsync();
        await base.InitializeAsync();
    }

    [Fact]
    public Task LiveCsxCompilesAndRendersWithRoslynWeb() => VerifyInteractionAsync(
        "Examples/Scripting/LiveCsx", "Run", "こんにちは Luxel + Roslyn + csx");

    [Fact]
    public Task BrowserHotReloadPublishesSuccessfulPreview() => VerifyInteractionAsync(
        "Examples/Scripting/HotReload", "Apply", "version 1");

    [Fact]
    public Task MultiFilePlaygroundExecutesThroughBrowserRunner() => VerifyInteractionAsync(
        "Examples/Scripting/Playground", "Run", "Workspace ready");

    [Fact]
    public async Task NotebookCodeCellsExecuteThroughRoslynWeb()
    {
        await BootAsync(Page, "Examples/Scripting/Notebook");
        await Page.ClickCanvasWidgetAsync(index: 0);
        await Page.ExpectWidgetDetailAsync("sum = 385");
    }

    private async Task VerifyInteractionAsync(string story, string button, string expected)
    {
        await BootAsync(Page, story);
        await Page.ClickCanvasWidgetAsync(button);
        await Page.ExpectWidgetDetailAsync(expected);
    }

    private static async Task BootAsync(IPage page, string story)
    {
        await page.GotoAsync(story.StoryPath());
        await page.ExpectRuntimeStoryAsync(story, noCapabilityFallback: true);
    }
}
