namespace Luxel.Gallery.Stories;

public static partial class LearnEcsBasics
{
    [Story]
    public static StoryResult EcsCubesOverviewSample(StoryContext ctx) => EcsCubesStories.EcsCubes(ctx);
}

public static partial class LearnEcsTransforms
{
    [Story]
    public static StoryResult EcsCubesTransformSample(StoryContext ctx) => EcsCubesStories.EcsCubes(ctx);
}
