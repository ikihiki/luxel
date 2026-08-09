using Luxel.UI;

namespace Luxel.Gallery.Stories;

internal static class ResourceExampleSources
{
    internal const string Hello = """
        using var resources = new ResourceSystem();
        resources.AddSource(new FileSource(files));
        resources.AddStep<byte[], TextAsset>(new UpperTextStep());

        using ResourceHandle<TextAsset> text = resources.Load<TextAsset>("hello.txt");
        await text.Ready;
        Console.WriteLine(text.Value.Text);
        """;
    internal const string Package = """
        using var resources = new ResourceSystem();
        resources.AddSource(new PackageSource(package));
        resources.AddStep<byte[], TextAsset>(new UpperTextStep());

        using ResourceHandle<TextAsset> title = resources.Load<TextAsset>("package://ui/title.txt");
        await title.Ready;
        """;
    internal const string Pipeline = """
        using var resources = new ResourceSystem();
        resources.AddSource(new FileSource(files));
        resources.AddStep<byte[], JsonDocument>(new JsonStep());
        resources.AddStep<JsonDocument, PlayerStats>(new PlayerStatsStep());

        using ResourceHandle<PlayerStats> stats = resources.Load<PlayerStats>("player.stats.json");
        await stats.Ready;
        """;
    internal const string Extensions = """
        resources.AddStep<byte[], MessageAsset>(new PlainMessageStep());      // .txt
        resources.AddStep<byte[], MessageAsset>(new CaptionMessageStep());    // .caption

        using ResourceHandle<MessageAsset> plain = resources.Load<MessageAsset>("motd.txt");
        using ResourceHandle<MessageAsset> caption = resources.Load<MessageAsset>("motd.caption");
        await Task.WhenAll(plain.Ready, caption.Ready);
        """;
    internal const string Dag = """
        resources.AddStep<byte[], TextAsset>(textStep);
        resources.AddStep<TextAsset, WordCount>(new WordCountStep());

        using ResourceHandle<TextAsset> text = resources.Load<TextAsset>("shared.txt");
        using ResourceHandle<WordCount> count = resources.Load<WordCount>("shared.txt");
        await Task.WhenAll(text.Ready, count.Ready); // both share the TextAsset node
        """;
    internal const string Scope = """
        using ResourceScope scope = resources.CreateScope("scenario/player");
        ResourceHandle<RuntimeLabel> label =
            scope.Create<RuntimeSeed, RuntimeLabel>("level-label", new RuntimeSeed(12));
        await label.Ready;
        // Disposing scope releases every handle it owns.
        """;
    internal const string Reload = """
        resources.Watch();
        using ResourceHandle<PlayerStats> stats = resources.Load<PlayerStats>("live.stats.json");
        await stats.Ready;

        files.Set("live.stats.json", malformedJson);
        await resources.PumpAsync(); // stats.Value remains the last good value
        files.Set("live.stats.json", correctedJson);
        await resources.PumpAsync(); // publishes the recovered value
        """;
    internal const string Http = """
        using var resources = new ResourceSystem();
        resources.AddSource(new HttpSource(httpClient));
        resources.AddStep<byte[], TextAsset>(new UpperTextStep());

        using ResourceHandle<TextAsset> remote =
            resources.Load<TextAsset>("https://assets.example/motd.txt");
        await remote.Ready;
        """;
    internal const string Document = """
        using ResourceHandle<AssetDocument> document = resources.Load<AssetDocument>(uri);
        await document.Ready;

        Console.WriteLine($"scenes={document.Value.Scenes.Count}");
        Console.WriteLine($"nodes={document.Value.Nodes.Count}");
        Console.WriteLine($"meshes={document.Value.Meshes.Count}");
        """;
    internal const string Primitive = """
        AssetPrimitive primitive = document.Meshes[0].Primitives[0];
        int vertexCount = primitive.Attributes.Positions.Length;
        bool valid = primitive.Attributes.Normals?.Length == vertexCount
            && primitive.Indices!.All(index => index < vertexCount);
        """;
    internal const string Material = """
        AssetMaterial material = document.Materials[0];
        Vector4 baseColor = material.BaseColorFactor;
        AssetTextureRef? texture = material.BaseColorTexture;
        int uvSet = texture?.TexCoordSet ?? 0;
        """;
    internal const string Animation = """
        animationPlayer.Sample(time);
        transformPropagate.Run(world);
        skinning.Run(world, sceneAssets);
        sceneAssets.FlushDynamicBuffers();
        extractor.Extract(world, sceneAssets);
        """;
    internal const string Gpu = """
        using AssetGpuInstallation gpu = resources.InstallAssetGpuLifecycle(device);
        using ResourceScope scope = resources.CreateScope("preview");
        ResourceHandle<GpuMesh> mesh = scope.Create<AssetMesh, GpuMesh>("mesh", cpuMesh);
        await mesh.Ready;
        """;
    internal const string Shader = """
        Debug.Assert(Marshal.SizeOf<MaterialGpuData>() == 32);
        Debug.Assert(SceneInstanceData.Stride == 80);
        Debug.Assert(Marshal.SizeOf<MorphDelta>() == 24);
        // C# encoders and shader decoders must keep these offsets in lockstep.
        """;
    internal const string BoxScene = """
        using ResourceHandle<AssetDocument> document = resources.Load<AssetDocument>("Box.gltf");
        await document.Ready;

        AssetPrimitive primitive = document.Value.Meshes[0].Primitives[0];
        using GpuPrimitive gpu = GpuAssetFactory.Upload(primitive, device);
        command.SetVertexBuffer(gpu.VertexBuffer).Draw((uint)gpu.VertexCount, 1);
        """;
    internal const string AnimatedScene = """
        SceneAssets scene = SceneBuilder.Build(world, document, device);
        var player = new SceneAnimationPlayer(world, scene, document.Animations[0]);

        player.Sample(time % document.Animations[0].Duration);
        TransformPropagateSystem.Run(world);
        extractor.Extract(new ExtractContext(device, frameIndex));
        """;
    internal const string SkinnedScene = """
        SceneAssets scene = SceneBuilder.Build(world, document, device);
        player.Sample(document.Animations[0].Duration * 0.30f);
        TransformPropagateSystem.Run(world);
        SkinningSystem.Run(world, scene);
        // Upload JointMatrices in skin-joint order before the skinned draw.
        """;
    internal const string MorphScene = """
        AssetPrimitive primitive = CreatePrimitiveWithMorphTargets();
        SceneAssets scene = SceneBuilder.Build(world, document, device);
        world.GetEntity(node).Add(new MorphWeights([0.85f]));
        // The morph shader adds weighted position/normal deltas before world transform.
        """;
    internal const string GltfLoad = """
        resources.AddStep<byte[], AssetDocument>(new GltfResourceStep());
        using ResourceHandle<AssetDocument> box = resources.Load<AssetDocument>("Box.gltf");
        await box.Ready;
        Console.WriteLine($"meshes={box.Value.Meshes.Count}");
        """;
    internal const string GltfExternal = """
        resources.AddStep<byte[], AssetDocument>(new GltfResourceStep());
        using ResourceHandle<AssetDocument> scene = resources.Load<AssetDocument>("models/scene.gltf");
        await scene.Ready;
        // GltfResourceStep resolves and loads models/buffers/geometry.bin as a dependency node.
        """;
    internal const string GltfMalformed = """
        using ResourceHandle<AssetDocument> broken = resources.Load<AssetDocument>("broken.gltf");
        try { await broken.Ready; }
        catch (Exception error)
        {
            Console.WriteLine(error.GetBaseException().Message); // accessor/buffer diagnostic
        }
        """;
    internal const string GltfReload = """
        resources.Watch();
        using ResourceHandle<AssetDocument> scene = resources.Load<AssetDocument>("models/scene.gltf");
        await scene.Ready;

        buffers.Set("models/buffers/geometry.bin", updatedGeometry);
        await resources.PumpAsync(); // dependency change reloads the root document
        """;
}

/// <summary>Canonical executable resource scenarios. Every route owns and operates an isolated ResourceSystem.</summary>
public static class ResourceExampleStories
{
    [Story("Examples/Resources/HelloTextAsset", Order = 0, SampleBundle = "resources.scenarios", Source = ResourceExampleSources.Hello)]
    public static Widget HelloTextAsset(StoryContext ctx) => ResourceScenarios.Create(ctx, "Hello text asset", ResourceScenarios.Hello);
    [Story("Examples/Resources/CustomPackageSource", Order = 1, SampleBundle = "resources.scenarios", Source = ResourceExampleSources.Package)]
    public static Widget CustomPackageSource(StoryContext ctx) => ResourceScenarios.Create(ctx, "Custom package source", ResourceScenarios.Package);
    [Story("Examples/Resources/PlayerStatsPipeline", Order = 2, SampleBundle = "resources.scenarios", Source = ResourceExampleSources.Pipeline)]
    public static Widget PlayerStatsPipeline(StoryContext ctx) => ResourceScenarios.Create(ctx, "Player stats pipeline", ResourceScenarios.PlayerStatsPipeline);
    [Story("Examples/Resources/ExtensionSelection", Order = 3, SampleBundle = "resources.scenarios", Source = ResourceExampleSources.Extensions)]
    public static Widget ExtensionSelection(StoryContext ctx) => ResourceScenarios.Create(ctx, "Extension selection", ResourceScenarios.Extensions);
    [Story("Examples/Resources/SharedDependencyGraph", Order = 4, SampleBundle = "resources.scenarios", Source = ResourceExampleSources.Dag)]
    public static Widget SharedDependencyGraph(StoryContext ctx) => ResourceScenarios.Create(ctx, "Shared dependency graph", ResourceScenarios.SharedDag);
    [Story("Examples/Resources/ScopedRuntimeValues", Order = 5, SampleBundle = "resources.scenarios", Source = ResourceExampleSources.Scope)]
    public static Widget ScopedRuntimeValues(StoryContext ctx) => ResourceScenarios.Create(ctx, "Scoped runtime values", ResourceScenarios.Scope);
    [Story("Examples/Resources/HotReloadRecovery", Order = 6, SampleBundle = "resources.scenarios", Source = ResourceExampleSources.Reload)]
    public static Widget HotReloadRecovery(StoryContext ctx) => ResourceScenarios.Create(ctx, "Hot reload recovery", ResourceScenarios.HotReload);
    [Story("Examples/Resources/BrowserHttpAssets", Order = 7, SampleBundle = "resources.scenarios", Source = ResourceExampleSources.Http)]
    public static Widget BrowserHttpAssets(StoryContext ctx) => ResourceScenarios.Create(ctx, "Browser HTTP assets", ResourceScenarios.Http);

    [Story("Examples/Resources/Assets/DocumentInspector", Order = 20, Source = ResourceExampleSources.Document)]
    public static Widget DocumentInspector(StoryContext ctx) => ResourceScenarios.Create(ctx, "Document inspector", ResourceScenarios.DocumentInspector);
    [Story("Examples/Resources/Assets/MeshPrimitiveInspector", Order = 21, Source = ResourceExampleSources.Primitive)]
    public static Widget MeshPrimitiveInspector(StoryContext ctx) => ResourceScenarios.Create(ctx, "Mesh and primitive inspector", ResourceScenarios.PrimitiveInspector);
    [Story("Examples/Resources/Assets/MaterialTextureInspector", Order = 22, Source = ResourceExampleSources.Material)]
    public static Widget MaterialTextureInspector(StoryContext ctx) => ResourceScenarios.Create(ctx, "Material and texture inspector", ResourceScenarios.MaterialInspector);
    [Story("Examples/Resources/Assets/AnimatedSceneGraph", Order = 23, Source = ResourceExampleSources.Animation)]
    public static Widget AnimatedSceneGraph(StoryContext ctx) => ResourceScenarios.Create(ctx, "Animated scene graph", ResourceScenarios.AnimatedGraph);
    [Story("Examples/Resources/Assets/GpuAssetRegistry", Order = 24, Source = ResourceExampleSources.Gpu)]
    public static Widget GpuAssetRegistry(StoryContext ctx) => ResourceScenarios.Create(ctx, "GPU asset registry", ResourceScenarios.GpuRegistry);
    [Story("Examples/Resources/Assets/ShaderBufferInspector", Order = 25, Source = ResourceExampleSources.Shader)]
    public static Widget ShaderBufferInspector(StoryContext ctx) => ResourceScenarios.Create(ctx, "Shader buffer inspector", ResourceScenarios.ShaderBuffers);

    [Story("Examples/Resources/Gltf/BoxDocumentLoad", Order = 40, Source = ResourceExampleSources.GltfLoad)]
    public static Widget BoxDocumentLoad(StoryContext ctx) => ResourceScenarios.Create(ctx, "Box document load", ResourceScenarios.BoxDocument);
    [Story("Examples/Resources/Gltf/ExternalBufferTrace", Order = 41, Source = ResourceExampleSources.GltfExternal)]
    public static Widget ExternalBufferTrace(StoryContext ctx) => ResourceScenarios.Create(ctx, "External buffer trace", ResourceScenarios.ExternalTrace);
    [Story("Examples/Resources/Gltf/MalformedAccessorDiagnostics", Order = 42, Source = ResourceExampleSources.GltfMalformed)]
    public static Widget MalformedAccessorDiagnostics(StoryContext ctx) => ResourceScenarios.Create(ctx, "Malformed accessor diagnostics", ResourceScenarios.Malformed);
    [Story("Examples/Resources/Gltf/ExternalDependencyReload", Order = 43, Source = ResourceExampleSources.GltfReload)]
    public static Widget ExternalDependencyReload(StoryContext ctx) => ResourceScenarios.Create(ctx, "External dependency reload", ResourceScenarios.ExternalReload);
}
