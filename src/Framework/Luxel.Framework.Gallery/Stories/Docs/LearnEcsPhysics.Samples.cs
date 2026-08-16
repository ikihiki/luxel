namespace Luxel.Gallery.Stories;

public static partial class LearnEcsPhysics
{
    [Story]
    public static StoryResult PhysicsFallingSample(StoryContext ctx) => PhysicsBrowserStories.PhysicsFalling(ctx);

    public static IReadOnlyList<StoryArgDefinition> PhysicsPlaygroundSampleArgs()
        => PhysicsBrowserStories.PhysicsPlaygroundArgs();

    [Story(Args = nameof(PhysicsPlaygroundSampleArgs))]
    public static StoryResult PhysicsPlaygroundSample(StoryContext ctx) => PhysicsBrowserStories.PhysicsPlayground(ctx);

    [Story]
    public static StoryResult PhysicsGizmosSample(StoryContext ctx) => PhysicsBrowserStories.PhysicsGizmosDemo(ctx);

    [Story]
    public static StoryResult PhysicsTriggerSample(StoryContext ctx) => PhysicsBrowserStories.PhysicsTriggerDemo(ctx);

    [Story]
    public static StoryResult PhysicsMeshSample(StoryContext ctx) => PhysicsBrowserStories.PhysicsMeshDemo(ctx);
}
