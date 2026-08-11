using Microsoft.Playwright;

namespace Luxel.Gallery.Browser.E2E.Tests;

public sealed class AudioStoryTests(BrowserFixture fixture) : GalleryBrowserTest(fixture)
{
    public static TheoryData<string> AudioStories => new()
    {
        "Examples/Audio/BackendLifecycle",
        "Examples/Audio/WaveformAndVoice",
        "Examples/Audio/Buses",
        "Examples/Audio/SpatialAttenuation",
        "Examples/Audio/StreamingQueue"
    };

    [Theory]
    [MemberData(nameof(AudioStories))]
    public Task BrowserWasmBootsAudioStory(string story) => RunAsync(story, async page =>
    {
        var failures = CollectFailures(page);
        await page.GotoAsync(StoryPath(story));
        await ExpectRuntimeStoryAsync(page, story);
        Assert.Empty(failures.ConsoleErrors);
        Assert.Empty(failures.PageErrors);
    });

    [Fact]
    public Task WebAudioLifecycleResumesAndSuspendsFromGalleryButtons() => RunAsync(nameof(WebAudioLifecycleResumesAndSuspendsFromGalleryButtons), async page =>
    {
        var failures = CollectFailures(page);
        const string story = "Examples/Audio/BackendLifecycle";
        await page.GotoAsync(StoryPath(story));
        await ExpectRuntimeStoryAsync(page, story);
        await ClickCanvasWidgetAsync(page, "Audioを有効化");
        await ExpectWidgetDetailAsync(page, "ResumeAsync完了: Running");
        await ClickCanvasWidgetAsync(page, "Audioを一時停止");
        await ExpectWidgetDetailAsync(page, "SuspendAsync完了: Suspended");
        Assert.Empty(failures.ConsoleErrors);
        Assert.Empty(failures.PageErrors);
    });

    [Fact]
    public Task WebAudioToneSubmitsPlaysAndClearsQueue() => RunAsync(nameof(WebAudioToneSubmitsPlaysAndClearsQueue), async page =>
    {
        var failures = CollectFailures(page);
        const string story = "Examples/Audio/WaveformAndVoice";
        await page.GotoAsync(StoryPath(story));
        await ExpectRuntimeStoryAsync(page, story);
        await ClickCanvasWidgetAsync(page, "440 Hzを再生");
        await ExpectWidgetDetailAsync(page, "再生中: 440 Hz / queued=1 / playing=True");
        await ClickCanvasWidgetAsync(page, "停止");
        await ExpectWidgetDetailAsync(page, "停止しました。queueは破棄されます。");
        Assert.Empty(failures.ConsoleErrors);
        Assert.Empty(failures.PageErrors);
    });

    [Fact]
    public Task WebAudioControlsUpdateObservableState() => RunAsync(nameof(WebAudioControlsUpdateObservableState), async page =>
    {
        var failures = CollectFailures(page);
        await BootAsync(page, "Examples/Audio/Buses");
        await ClickCanvasWidgetAsync(page, "loopを再生");
        await ExpectWidgetDetailAsync(page, "voice 30%");
        await ClickCanvasWidgetAsync(page, "Music 15%");
        await ExpectWidgetDetailAsync(page, "voice 8%");

        await BootAsync(page, "Examples/Audio/SpatialAttenuation");
        await ClickCanvasWidgetAsync(page, "右・遠い");
        await ExpectWidgetDetailAsync(page, "gain=0.25 / pan=+1.00");

        await BootAsync(page, "Examples/Audio/StreamingQueue");
        await ClickCanvasWidgetAsync(page, "3 chunkを再生");
        await ExpectWidgetDetailAsync(page, "330 → 440 → 660 Hz / queued=3 / playing=True");
        await ClickCanvasWidgetAsync(page, "停止");
        await ExpectWidgetDetailAsync(page, "停止してqueueを破棄しました。");
        Assert.Empty(failures.ConsoleErrors);
        Assert.Empty(failures.PageErrors);
    });

    private static async Task BootAsync(IPage page, string story)
    {
        await page.GotoAsync(StoryPath(story));
        await ExpectRuntimeStoryAsync(page, story);
    }
}
