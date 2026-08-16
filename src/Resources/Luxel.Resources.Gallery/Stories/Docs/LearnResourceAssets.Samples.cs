namespace Luxel.Resources.Gallery.Stories;

public static partial class LearnResourceAssets
{
    [Story]
    public static StoryResult DocumentInspectorSample(StoryContext ctx) => ResourceExampleStories.DocumentInspector(ctx);

    [Story]
    public static StoryResult MeshPrimitiveInspectorSample(StoryContext ctx) => ResourceExampleStories.MeshPrimitiveInspector(ctx);

    [Story]
    public static StoryResult MaterialTextureInspectorSample(StoryContext ctx) => ResourceExampleStories.MaterialTextureInspector(ctx);

    [Story]
    public static StoryResult AnimatedSceneGraphSample(StoryContext ctx) => ResourceExampleStories.AnimatedSceneGraph(ctx);

    [Story]
    public static StoryResult GpuManagerInstallationSample(StoryContext ctx) => ResourceExampleStories.GpuManagerInstallation(ctx);

    [Story]
    public static StoryResult CustomGpuParticleBuffersSample(StoryContext ctx) => ResourceExampleStories.CustomGpuParticleBuffers(ctx);

    [Story]
    public static StoryResult GpuIndexRecyclingSample(StoryContext ctx) => ResourceExampleStories.GpuIndexRecycling(ctx);

    [Story]
    public static StoryResult DeviceLostRecoverySample(StoryContext ctx) => ResourceExampleStories.DeviceLostRecovery(ctx);

    [Story]
    public static StoryResult ShaderBufferInspectorSample(StoryContext ctx) => ResourceExampleStories.ShaderBufferInspector(ctx);
}
