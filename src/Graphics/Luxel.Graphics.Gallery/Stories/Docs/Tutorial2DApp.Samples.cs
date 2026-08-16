namespace Luxel.Gallery.Stories;

public static partial class Tutorial2DApp
{
    [Story]
    public static StoryResult ShapesSample(StoryContext ctx) => TwoDBrowserStories.Shapes(ctx);

    [Story]
    public static StoryResult CameraSample(StoryContext ctx) => TwoDBrowserStories.CameraTransform(ctx);

    [Story]
    public static StoryResult SpritesSample(StoryContext ctx) => TwoDBrowserStories.Sprites(ctx);

    [Story]
    public static StoryResult RetainedUpdatesSample(StoryContext ctx) => TwoDBrowserStories.RetainedUpdates(ctx);
}
