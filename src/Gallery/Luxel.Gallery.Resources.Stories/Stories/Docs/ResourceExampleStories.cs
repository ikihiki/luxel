using Luxel.UI;
using static Luxel.Gallery.Stories.ResourceDocsKit;

namespace Luxel.Gallery.Stories;

/// <summary>Canonical scenario examples. Each documentation-only scenario points at focused executable sample code or a concrete API inspection.</summary>
public static class ResourceExampleStories
{
    [Story("Examples/Resources/HelloTextAsset", Order = 0, SampleBundle = "resources.scenarios")]
    public static StoryResult HelloTextAsset(StoryContext ctx) => Sample("Hello text asset", "A memory-backed file source and byte-to-text step complete a typed load and validate the value.", "hello-text-asset");

    [Story("Examples/Resources/CustomPackageSource", Order = 1, SampleBundle = "resources.scenarios")]
    public static StoryResult CustomPackageSource(StoryContext ctx) => Sample("Custom package source", "A package:// source implements scheme dispatch without performing decode work.", "custom-package-source");

    [Story("Examples/Resources/PlayerStatsPipeline", Order = 2, SampleBundle = "resources.scenarios")]
    public static StoryResult PlayerStatsPipeline(StoryContext ctx) => Sample("Player stats pipeline", "The registered chain byte[] → JsonDocument → PlayerStats demonstrates adding a new resource type end to end.", "player-stats-pipeline");

    [Story("Examples/Resources/ExtensionSelection", Order = 3, SampleBundle = "resources.scenarios")]
    public static StoryResult ExtensionSelection(StoryContext ctx) => Sample("Extension selection", "Two steps produce the same type; .txt and .caption select distinct implementations and observable values.", "extension-selection");

    [Story("Examples/Resources/SharedDependencyGraph", Order = 4, SampleBundle = "resources.scenarios")]
    public static StoryResult SharedDependencyGraph(StoryContext ctx) => Sample("Shared dependency graph", "TextAsset and WordCount loads share one counted intermediate TextAsset node.", "shared-dependency-graph");

    [Story("Examples/Resources/ScopedRuntimeValues", Order = 5, SampleBundle = "resources.scenarios")]
    public static StoryResult ScopedRuntimeValues(StoryContext ctx) => Sample("Scoped runtime values", "A scope-local integer is converted to RuntimeLabel and released with its owner scope.", "scoped-runtime-values");

    [Story("Examples/Resources/HotReloadRecovery", Order = 6, SampleBundle = "resources.scenarios")]
    public static StoryResult HotReloadRecovery(StoryContext ctx) => Sample("Hot reload recovery", "A malformed JSON reload preserves level 1, records an error, then a valid edit publishes level 2 through Pump.", "hot-reload-recovery");

    [Story("Examples/Resources/BrowserHttpAssets", Order = 7, SampleBundle = "resources.scenarios")]
    public static StoryResult BrowserHttpAssets(StoryContext ctx) => Sample("Browser HTTP assets", "HttpSource loads the same typed TextAsset pipeline used by browser hosts; a deterministic handler keeps the sample headless.", "browser-http-assets");

    [Story("Examples/Resources/Assets/DocumentInspector", Order = 20)]
    public static StoryResult DocumentInspector(StoryContext ctx) => $$"""
        # Document inspector

        Inspect every class-family count without creating GPU state.

        ```csharp
        Console.WriteLine($"scenes={document.Scenes.Count}, nodes={document.Nodes.Count}, " +
            $"meshes={document.Meshes.Count}, materials={document.Materials.Count}, " +
            $"animations={document.Animations.Count}, skins={document.Skins.Count}");
        foreach (AssetScene scene in document.Scenes)
            foreach (AssetNode root in scene.Roots)
                Visit(root, depth: 0);
        ```
        """;

    [Story("Examples/Resources/Assets/MeshPrimitiveInspector", Order = 21)]
    public static StoryResult MeshPrimitiveInspector(StoryContext ctx) => $$"""
        # Mesh and primitive inspector

        This inspection catches mismatched optional attributes and out-of-range indices before upload.

        ```csharp
        foreach (AssetPrimitive primitive in mesh.Primitives)
        {
            int vertices = primitive.Attributes.Positions.Length;
            if (primitive.Attributes.Normals is { } normals && normals.Length != vertices)
                throw new InvalidDataException("NORMAL count differs from POSITION count.");
            if (primitive.Indices is { } indices && indices.Any(index => index >= vertices))
                throw new InvalidDataException("Primitive index exceeds vertex count.");
        }
        ```
        """;

    [Story("Examples/Resources/Assets/MaterialTextureInspector", Order = 22)]
    public static StoryResult MaterialTextureInspector(StoryContext ctx) => $$"""
        # Material and texture inspector

        ```csharp
        foreach (AssetMaterial material in document.Materials)
        {
            AssetTextureRef? color = material.BaseColorTexture;
            Console.WriteLine($"base={material.BaseColorFactor}, alpha={material.AlphaMode}, " +
                $"texture={color?.Texture.Width}x{color?.Texture.Height}, uv={color?.TexCoordSet}");
        }
        ```

        The inspector reports CPU intent; it does not claim every field is evaluated by the standard shader.
        """;

    [Story("Examples/Resources/Assets/AnimatedSceneGraph", Order = 23)]
    public static StoryResult AnimatedSceneGraph(StoryContext ctx) => $$"""
        # Animated scene graph

        The runtime update order is explicit and executable in the canonical AnimatedBox story.

        ```csharp
        player.Sample(time % duration);
        TransformPropagateSystem.Run(world);
        extractor.Extract(new ExtractContext(device, frameIndex++));
        ```

        [Open AnimatedBox](story:Examples/Resources/Gltf/AnimatedBox)
        """;

    [Story("Examples/Resources/Assets/GpuAssetRegistry", Order = 24)]
    public static StoryResult GpuAssetRegistry(StoryContext ctx) => $$"""
        # GPU asset registry

        ```csharp
        using AssetGpuInstallation installation = resources.InstallAssetGpuLifecycle(device);
        using ResourceScope scope = resources.CreateScope("preview");
        ResourceHandle<GpuMesh> gpuMesh =
            scope.Create<AssetMesh, GpuMesh>("mesh", cpuMesh);
        await gpuMesh.Ready;
        ```

        The installation owns the registry and must be disposed before the device.
        """;

    [Story("Examples/Resources/Assets/ShaderBufferInspector", Order = 25)]
    public static StoryResult ShaderBufferInspector(StoryContext ctx) => $$"""
        # Shader buffer inspector

        ```csharp
        Debug.Assert(Marshal.SizeOf<MaterialGpuData>() == 32);
        Debug.Assert(SceneInstanceData.Stride == 80);
        Debug.Assert(Marshal.SizeOf<MorphDelta>() == 24);
        Console.WriteLine($"vertexStride={primitive.VertexStride}, skinned={primitive.HasSkinning}");
        ```

        Run these checks beside shader offset tests whenever the ABI changes.
        """;

    [Story("Examples/Resources/Gltf/BoxDocumentLoad", Order = 40)]
    public static StoryResult BoxDocumentLoad(StoryContext ctx) => $$"""
        # Box document load

        ```csharp
        resources.AddStep<byte[], AssetDocument>(new GltfResourceStep());
        using ResourceHandle<AssetDocument> box = resources.Load<AssetDocument>("Box.glb");
        await box.Ready;
        Console.WriteLine($"meshes={box.Value.Meshes.Count}, nodes={box.Value.Nodes.Count}");
        ```

        This route demonstrates the CPU document boundary; rendering is [BoxScene](story:Examples/Resources/Gltf/BoxScene).
        """;

    [Story("Examples/Resources/Gltf/ExternalBufferTrace", Order = 41)]
    public static StoryResult ExternalBufferTrace(StoryContext ctx) => $$"""
        # External buffer trace

        ```csharp
        ResourceUri document = new("https://cdn.example/models/scene.gltf");
        ResourceUri buffer = document.Resolve("buffers/geometry.bin");
        Debug.Assert(buffer.Url == "https://cdn.example/models/buffers/geometry.bin");
        // GltfResourceStep obtains this through ctx.Load<byte[]>(buffer), creating a DAG edge.
        ```
        """;

    [Story("Examples/Resources/Gltf/MalformedAccessorDiagnostics", Order = 42)]
    public static StoryResult MalformedAccessorDiagnostics(StoryContext ctx) => $$"""
        # Malformed accessor diagnostics

        ```csharp
        try
        {
            AssetDocument document = await LoadMalformedFixture();
            throw new InvalidOperationException("Fixture unexpectedly imported.");
        }
        catch (InvalidDataException error)
        {
            Console.WriteLine(error.Message); // accessor/buffer-view context belongs in this diagnostic
        }
        ```

        Importer tests use malformed fixtures to verify the concrete failure path before any GPU upload.
        """;

    [Story("Examples/Resources/Gltf/ExternalDependencyReload", Order = 43)]
    public static StoryResult ExternalDependencyReload(StoryContext ctx) => $$"""
        # External dependency reload

        ```csharp
        resources.Watch();
        using ResourceHandle<AssetDocument> scene = resources.Load<AssetDocument>("scene.gltf");
        await scene.Ready;
        // Editing geometry.bin triggers its byte[] node; the dependency edge reloads scene.gltf.
        while (running)
        {
            resources.Pump();
            DrawLastGood(scene.Value);
            if (scene.LastReloadError is { } error) ShowImportError(error);
        }
        ```
        """;

    private static StoryResult Sample(string title, string description, string region) => $$"""
        # {{title}}

        {{description}}

        {{SampleSource("samples/LuxelResources/Program.cs", region)}}

        {{SampleBundle("resources.scenarios")}}
        """;
}
