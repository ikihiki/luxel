using Microsoft.Playwright;

namespace Luxel.Gallery.Browser.E2E.Tests;

public sealed class AudioStoryTests : Microsoft.Playwright.Xunit.PageTest
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
    public async Task BrowserWasmBootsAudioStory(string story)
    {
        var failures = Page.CollectFailures();
        await Page.GotoAsync(story.StoryPath());
        await Page.ExpectRuntimeStoryAsync(story);
        Assert.Empty(failures.ConsoleErrors);
        Assert.Empty(failures.PageErrors);
    }

    [Fact]
    public async Task WebAudioLifecycleResumesAndSuspendsFromGalleryButtons()
    {
        var failures = Page.CollectFailures();
        const string story = "Examples/Audio/BackendLifecycle";
        await Page.GotoAsync(story.StoryPath());
        await Page.ExpectRuntimeStoryAsync(story);
        await Page.ClickCanvasWidgetAsync("Audioを有効化");
        await Page.ExpectWidgetDetailAsync("ResumeAsync完了: Running");
        await Page.ClickCanvasWidgetAsync("Audioを一時停止");
        await Page.ExpectWidgetDetailAsync("SuspendAsync完了: Suspended");
        Assert.Empty(failures.ConsoleErrors);
        Assert.Empty(failures.PageErrors);
    }

    [Fact]
    public async Task WebAudioToneSubmitsPlaysAndClearsQueue()
    {
        var failures = Page.CollectFailures();
        const string story = "Examples/Audio/WaveformAndVoice";
        await Page.GotoAsync(story.StoryPath());
        await Page.ExpectRuntimeStoryAsync(story);
        await Page.ClickCanvasWidgetAsync("440 Hzを再生");
        await Page.ExpectWidgetDetailAsync("再生中: 440 Hz / queued=1 / playing=True");
        await Page.ClickCanvasWidgetAsync("停止");
        await Page.ExpectWidgetDetailAsync("停止しました。queueは破棄されます。");
        Assert.Empty(failures.ConsoleErrors);
        Assert.Empty(failures.PageErrors);
    }

    [Fact]
    public async Task WebAudioControlsUpdateObservableState()
    {
        var failures = Page.CollectFailures();
        await BootAsync(Page, "Examples/Audio/Buses");
        await Page.ClickCanvasWidgetAsync("loopを再生");
        await Page.ExpectWidgetDetailAsync("voice 30%");
        await Page.ClickCanvasWidgetAsync("Music 15%");
        await Page.ExpectWidgetDetailAsync("voice 8%");

        await BootAsync(Page, "Examples/Audio/SpatialAttenuation");
        await Page.ClickCanvasWidgetAsync("右・遠い");
        await Page.ExpectWidgetDetailAsync("gain=0.25 / pan=+1.00");

        await BootAsync(Page, "Examples/Audio/StreamingQueue");
        await Page.ClickCanvasWidgetAsync("3 chunkを再生");
        await Page.ExpectWidgetDetailAsync("330 → 440 → 660 Hz / queued=3 / playing=True");
        await Page.ClickCanvasWidgetAsync("停止");
        await Page.ExpectWidgetDetailAsync("停止してqueueを破棄しました。");
        Assert.Empty(failures.ConsoleErrors);
        Assert.Empty(failures.PageErrors);
    }

    private static async Task BootAsync(IPage page, string story)
    {
        await page.GotoAsync(story.StoryPath());
        await page.ExpectRuntimeStoryAsync(story);
    }
}
