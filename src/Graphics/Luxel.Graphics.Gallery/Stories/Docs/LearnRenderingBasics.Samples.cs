namespace Luxel.Gallery.Stories;

public static partial class DocsRenderingLearn
{
    [Story]
    public static StoryResult ClearColorSample(StoryContext ctx) => GpuViewStories.ClearColor(ctx);

    [Story]
    public static StoryResult TriangleSample(StoryContext ctx) => GpuViewStories.Triangle(ctx);

    [Story]
    public static StoryResult BuffersAndBindingsSample(StoryContext ctx) => GpuViewStories.BuffersAndBindings(ctx);

    [Story]
    public static StoryResult TexturesSample(StoryContext ctx) => GpuViewStories.Textures(ctx);

    [Story]
    public static StoryResult TopologySample(StoryContext ctx) => PipelineStateStories.Topology(ctx);

    [Story]
    public static StoryResult RasterizerSample(StoryContext ctx) => PipelineStateStories.Rasterizer(ctx);

    [Story]
    public static StoryResult DepthStatesSample(StoryContext ctx) => PipelineStateStories.DepthStates(ctx);

    [Story]
    public static StoryResult BlendStateSample(StoryContext ctx) => PipelineStateStories.BlendState(ctx);

    [Story]
    public static StoryResult StencilSample(StoryContext ctx) => PipelineStateStories.Stencil(ctx);

    [Story]
    public static StoryResult ViewportScissorSample(StoryContext ctx) => PipelineStateStories.ViewportScissor(ctx);

    [Story]
    public static StoryResult SeparationSample(StoryContext ctx) => PipelineStateStories.Separation(ctx);
}
