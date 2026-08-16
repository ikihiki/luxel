namespace Luxel.Gallery.Stories;

public static partial class LearnRenderingTwoD
{
    [Story]
    public static StoryResult InputPathsSample(StoryContext ctx) => TwoDBrowserStories.InputPaths(ctx);

    [Story]
    public static StoryResult CompositeSample(StoryContext ctx) => TwoDBrowserStories.Composite(ctx);

    [Story]
    public static StoryResult SpritesSample(StoryContext ctx) => TwoDBrowserStories.Sprites(ctx);

    [Story]
    public static StoryResult CameraTransformSample(StoryContext ctx) => TwoDBrowserStories.CameraTransform(ctx);

    [Story]
    public static StoryResult BackendsSample(StoryContext ctx) => TwoDBackendStories.Backends(ctx);

    [Story]
    public static StoryResult RetainedUpdatesSample(StoryContext ctx) => TwoDBrowserStories.RetainedUpdates(ctx);
}
