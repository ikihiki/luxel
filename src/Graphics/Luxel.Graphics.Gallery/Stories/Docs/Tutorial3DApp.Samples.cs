namespace Luxel.Gallery.Stories;

public static partial class Tutorial3DApp
{
    [Story]
    public static StoryResult TriangleSample(StoryContext ctx) => GpuViewStories.Triangle(ctx);

    [Story]
    public static StoryResult DepthSample(StoryContext ctx) => PipelineStateStories.DepthStates(ctx);

    [Story]
    public static StoryResult WorldSpaceUiSample(StoryContext ctx) => Ecs3DStories.WorldSpaceUi();

    [Story]
    public static StoryResult BloomSample(StoryContext ctx) => Ecs3DStories.Bloom3D(ctx);
}
