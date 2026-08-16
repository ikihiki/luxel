namespace Luxel.Resources.Gallery.Stories;

public static partial class LearnResourceGltf
{
    [Story]
    public static StoryResult BoxDocumentLoadSample(StoryContext ctx) => ResourceExampleStories.BoxDocumentLoad(ctx);

    [Story]
    public static StoryResult ExternalBufferTraceSample(StoryContext ctx) => ResourceExampleStories.ExternalBufferTrace(ctx);

    [Story]
    public static StoryResult MalformedAccessorDiagnosticsSample(StoryContext ctx) => ResourceExampleStories.MalformedAccessorDiagnostics(ctx);

    [Story]
    public static StoryResult GltfBoxSample(StoryContext ctx) => AssetStories.GltfBox(ctx);

    [Story]
    public static StoryResult GltfAnimatedSample(StoryContext ctx) => AssetStories.GltfAnimated(ctx);

    [Story]
    public static StoryResult ExternalDependencyReloadSample(StoryContext ctx) => ResourceExampleStories.ExternalDependencyReload(ctx);
}
