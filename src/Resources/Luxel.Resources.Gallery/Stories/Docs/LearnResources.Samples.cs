namespace Luxel.Resources.Gallery.Stories;

public static partial class LearnResources
{
    [Story]
    public static StoryResult ReadyBuilderSample(StoryContext ctx) => ResourceExampleStories.ReadyBuilder(ctx);

    [Story]
    public static StoryResult CustomExecutionDomainSample(StoryContext ctx) => ResourceExampleStories.CustomExecutionDomain(ctx);

    [Story]
    public static StoryResult TypedManagerBindingSample(StoryContext ctx) => ResourceExampleStories.TypedManagerBinding(ctx);

    [Story]
    public static StoryResult SharedRequestIdentitySample(StoryContext ctx) => ResourceExampleStories.SharedRequestIdentity(ctx);

    [Story]
    public static StoryResult CustomSourceAndStepSample(StoryContext ctx) => ResourceExampleStories.CustomSourceAndStep(ctx);

    [Story]
    public static StoryResult DependencyPublicationSample(StoryContext ctx) => ResourceExampleStories.DependencyPublication(ctx);

    [Story]
    public static StoryResult ScopedRetirementSample(StoryContext ctx) => ResourceExampleStories.ScopedRetirement(ctx);

    [Story]
    public static StoryResult ReloadKeepsLastGoodSample(StoryContext ctx) => ResourceExampleStories.ReloadKeepsLastGood(ctx);

    [Story]
    public static StoryResult DomainAndManagerMetricsSample(StoryContext ctx) => ResourceExampleStories.DomainAndManagerMetrics(ctx);

    [Story]
    public static StoryResult WasmCooperativeSchedulingSample(StoryContext ctx) => ResourceExampleStories.WasmCooperativeScheduling(ctx);
}
