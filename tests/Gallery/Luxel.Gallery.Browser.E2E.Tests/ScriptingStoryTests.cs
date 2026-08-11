using Microsoft.Playwright;

namespace Luxel.Gallery.Browser.E2E.Tests;

public sealed class ScriptingStoryTests(BrowserFixture fixture) : GalleryBrowserTest(fixture)
{
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
    public Task NotebookCodeCellsExecuteThroughRoslynWeb() => RunAsync(nameof(NotebookCodeCellsExecuteThroughRoslynWeb), async page =>
    {
        await BootAsync(page, "Examples/Scripting/Notebook");
        await ClickCanvasWidgetAsync(page, index: 0);
        await ExpectWidgetDetailAsync(page, "sum = 385");
    });

    private Task VerifyInteractionAsync(string story, string button, string expected) => RunAsync(story, async page =>
    {
        await BootAsync(page, story);
        await ClickCanvasWidgetAsync(page, button);
        await ExpectWidgetDetailAsync(page, expected);
    });

    private static async Task BootAsync(IPage page, string story)
    {
        await page.GotoAsync(StoryPath(story));
        await ExpectRuntimeStoryAsync(page, story, noCapabilityFallback: true);
    }
}
