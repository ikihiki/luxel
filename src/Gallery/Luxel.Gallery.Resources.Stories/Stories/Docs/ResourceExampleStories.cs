using Luxel.UI;

namespace Luxel.Gallery.Stories;

/// <summary>Canonical executable resource scenarios. Every route owns and operates an isolated ResourceSystem.</summary>
public static class ResourceExampleStories
{
    [Story("Examples/Resources/HelloTextAsset", Order = 0, SampleBundle = "resources.scenarios")]
    public static Widget HelloTextAsset(StoryContext ctx) => ResourceScenarios.Create(ctx, "Hello text asset", ResourceScenarios.Hello);
    [Story("Examples/Resources/CustomPackageSource", Order = 1, SampleBundle = "resources.scenarios")]
    public static Widget CustomPackageSource(StoryContext ctx) => ResourceScenarios.Create(ctx, "Custom package source", ResourceScenarios.Package);
    [Story("Examples/Resources/PlayerStatsPipeline", Order = 2, SampleBundle = "resources.scenarios")]
    public static Widget PlayerStatsPipeline(StoryContext ctx) => ResourceScenarios.Create(ctx, "Player stats pipeline", ResourceScenarios.PlayerStatsPipeline);
    [Story("Examples/Resources/ExtensionSelection", Order = 3, SampleBundle = "resources.scenarios")]
    public static Widget ExtensionSelection(StoryContext ctx) => ResourceScenarios.Create(ctx, "Extension selection", ResourceScenarios.Extensions);
    [Story("Examples/Resources/SharedDependencyGraph", Order = 4, SampleBundle = "resources.scenarios")]
    public static Widget SharedDependencyGraph(StoryContext ctx) => ResourceScenarios.Create(ctx, "Shared dependency graph", ResourceScenarios.SharedDag);
    [Story("Examples/Resources/ScopedRuntimeValues", Order = 5, SampleBundle = "resources.scenarios")]
    public static Widget ScopedRuntimeValues(StoryContext ctx) => ResourceScenarios.Create(ctx, "Scoped runtime values", ResourceScenarios.Scope);
    [Story("Examples/Resources/HotReloadRecovery", Order = 6, SampleBundle = "resources.scenarios")]
    public static Widget HotReloadRecovery(StoryContext ctx) => ResourceScenarios.Create(ctx, "Hot reload recovery", ResourceScenarios.HotReload);
    [Story("Examples/Resources/BrowserHttpAssets", Order = 7, SampleBundle = "resources.scenarios")]
    public static Widget BrowserHttpAssets(StoryContext ctx) => ResourceScenarios.Create(ctx, "Browser HTTP assets", ResourceScenarios.Http);

    [Story("Examples/Resources/Assets/DocumentInspector", Order = 20)]
    public static Widget DocumentInspector(StoryContext ctx) => ResourceScenarios.Create(ctx, "Document inspector", ResourceScenarios.DocumentInspector);
    [Story("Examples/Resources/Assets/MeshPrimitiveInspector", Order = 21)]
    public static Widget MeshPrimitiveInspector(StoryContext ctx) => ResourceScenarios.Create(ctx, "Mesh and primitive inspector", ResourceScenarios.PrimitiveInspector);
    [Story("Examples/Resources/Assets/MaterialTextureInspector", Order = 22)]
    public static Widget MaterialTextureInspector(StoryContext ctx) => ResourceScenarios.Create(ctx, "Material and texture inspector", ResourceScenarios.MaterialInspector);
    [Story("Examples/Resources/Assets/AnimatedSceneGraph", Order = 23)]
    public static Widget AnimatedSceneGraph(StoryContext ctx) => ResourceScenarios.Create(ctx, "Animated scene graph", ResourceScenarios.AnimatedGraph);
    [Story("Examples/Resources/Assets/GpuAssetRegistry", Order = 24)]
    public static Widget GpuAssetRegistry(StoryContext ctx) => ResourceScenarios.Create(ctx, "GPU asset registry", ResourceScenarios.GpuRegistry);
    [Story("Examples/Resources/Assets/ShaderBufferInspector", Order = 25)]
    public static Widget ShaderBufferInspector(StoryContext ctx) => ResourceScenarios.Create(ctx, "Shader buffer inspector", ResourceScenarios.ShaderBuffers);

    [Story("Examples/Resources/Gltf/BoxDocumentLoad", Order = 40)]
    public static Widget BoxDocumentLoad(StoryContext ctx) => ResourceScenarios.Create(ctx, "Box document load", ResourceScenarios.BoxDocument);
    [Story("Examples/Resources/Gltf/ExternalBufferTrace", Order = 41)]
    public static Widget ExternalBufferTrace(StoryContext ctx) => ResourceScenarios.Create(ctx, "External buffer trace", ResourceScenarios.ExternalTrace);
    [Story("Examples/Resources/Gltf/MalformedAccessorDiagnostics", Order = 42)]
    public static Widget MalformedAccessorDiagnostics(StoryContext ctx) => ResourceScenarios.Create(ctx, "Malformed accessor diagnostics", ResourceScenarios.Malformed);
    [Story("Examples/Resources/Gltf/ExternalDependencyReload", Order = 43)]
    public static Widget ExternalDependencyReload(StoryContext ctx) => ResourceScenarios.Create(ctx, "External dependency reload", ResourceScenarios.ExternalReload);
}
