namespace Luxel.Gallery.Stories;

public static partial class DocsRenderingLearn
{
    [Story]
    public static StoryResult SceneRenderSample(StoryContext ctx) => TwoDBrowserStories.SceneRender(ctx);

    [Story]
    public static StoryResult ShapesSample(StoryContext ctx) => TwoDBrowserStories.Shapes(ctx);
}
