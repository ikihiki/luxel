namespace Luxel.Gallery.Stories;

public static partial class LearnRenderGraph
{
    [Story]
    public static StoryResult BlurSample(StoryContext ctx) => BrowserRenderGraphStories.Blur(ctx);
}
