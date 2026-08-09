using System.Runtime.InteropServices;
using System.Text.Json;
using Luxel.AssetRuntime;
using Luxel.Assets;
using Luxel.Assets.Gltf;
using Luxel.AssetsGpu;
using Luxel.Resources;
using Luxel.UI;
using static Luxel.Gallery.Stories.ResourceScenarioSupport;

namespace Luxel.Gallery.Stories;

/// <summary>Canonical executable resource scenarios. Every story contains the ResourceSystem operations it demonstrates.</summary>
public static class ResourceExampleStories
{
    [Story("Examples/Resources/HelloTextAsset", Order = 0, SampleBundle = "resources.scenarios")]
    public static Widget HelloTextAsset(StoryContext ctx) => new ResourceScenarioWidget("Hello text asset", async resources =>
    {
        MemoryFileSystem files = Files(("hello.txt", "hello resources"));
        resources.AddSource(new FileSource(files));
        resources.AddStep<byte[], TextAsset>(new UpperTextStep());
        using ResourceHandle<TextAsset> value = resources.Load<TextAsset>("hello.txt");
        await value.Ready;
        return $"status={value.Status}; value={value.Value.Text}";
    }, ctx.Log);

    [Story("Examples/Resources/CustomPackageSource", Order = 1, SampleBundle = "resources.scenarios")]
    public static Widget CustomPackageSource(StoryContext ctx) => new ResourceScenarioWidget("Custom package source", async resources =>
    {
        resources.AddSource(new PackageSource(new Dictionary<string, byte[]> { ["ui/title.txt"] = Bytes("package title") }));
        resources.AddStep<byte[], TextAsset>(new UpperTextStep());
        using ResourceHandle<TextAsset> value = resources.Load<TextAsset>("package://ui/title.txt");
        await value.Ready;
        return $"scheme=package; value={value.Value.Text}";
    }, ctx.Log);

    [Story("Examples/Resources/PlayerStatsPipeline", Order = 2, SampleBundle = "resources.scenarios")]
    public static Widget PlayerStatsPipeline(StoryContext ctx) => new ResourceScenarioWidget("Player stats pipeline", async resources =>
    {
        resources.AddSource(new FileSource(Files(("player.stats.json", "{\"name\":\"Mina\",\"level\":7}"))));
        resources.AddStep<byte[], JsonDocument>(new JsonStep());
        resources.AddStep<JsonDocument, PlayerStats>(new PlayerStatsStep());
        using ResourceHandle<PlayerStats> value = resources.Load<PlayerStats>("player.stats.json");
        await value.Ready;
        return $"pipeline=byte[] -> JsonDocument -> PlayerStats; value={value.Value.Name} level {value.Value.Level}";
    }, ctx.Log);

    [Story("Examples/Resources/ExtensionSelection", Order = 3, SampleBundle = "resources.scenarios")]
    public static Widget ExtensionSelection(StoryContext ctx) => new ResourceScenarioWidget("Extension selection", async resources =>
    {
        resources.AddSource(new FileSource(Files(("motd.txt", "hello"), ("motd.caption", "hello"))));
        resources.AddStep<byte[], MessageAsset>(new PlainMessageStep());
        resources.AddStep<byte[], MessageAsset>(new CaptionMessageStep());
        using ResourceHandle<MessageAsset> plain = resources.Load<MessageAsset>("motd.txt");
        using ResourceHandle<MessageAsset> caption = resources.Load<MessageAsset>("motd.caption");
        await Task.WhenAll(plain.Ready, caption.Ready);
        return $".txt={plain.Value.Text}; .caption={caption.Value.Text}";
    }, ctx.Log);

    [Story("Examples/Resources/SharedDependencyGraph", Order = 4, SampleBundle = "resources.scenarios")]
    public static Widget SharedDependencyGraph(StoryContext ctx) => new ResourceScenarioWidget("Shared dependency graph", async resources =>
    {
        var counter = new CountingTextStep();
        resources.AddSource(new FileSource(Files(("shared.txt", "one shared node"))));
        resources.AddStep<byte[], TextAsset>(counter);
        resources.AddStep<TextAsset, WordCount>(new WordCountStep());
        using ResourceHandle<TextAsset> text = resources.Load<TextAsset>("shared.txt");
        using ResourceHandle<WordCount> count = resources.Load<WordCount>("shared.txt");
        await Task.WhenAll(text.Ready, count.Ready);
        return $"text-step-runs={counter.Runs}; words={count.Value.Count}; shared={counter.Runs == 1}";
    }, ctx.Log);

    [Story("Examples/Resources/ScopedRuntimeValues", Order = 5, SampleBundle = "resources.scenarios")]
    public static Widget ScopedRuntimeValues(StoryContext ctx) => new ResourceScenarioWidget("Scoped runtime values", async resources =>
    {
        resources.AddStep<RuntimeSeed, RuntimeLabel>(new RuntimeLabelStep());
        using ResourceScope scope = resources.CreateScope("scenario/player");
        ResourceHandle<RuntimeLabel> label = scope.Create<RuntimeSeed, RuntimeLabel>("level-label", new RuntimeSeed(12));
        await label.Ready;
        return $"owner=scenario/player; value={label.Value.Text}";
    }, ctx.Log);

    [Story("Examples/Resources/HotReloadRecovery", Order = 6, SampleBundle = "resources.scenarios")]
    public static Widget HotReloadRecovery(StoryContext ctx) => new ResourceScenarioWidget("Hot reload recovery", async resources =>
    {
        MemoryFileSystem files = Files(("live.stats.json", "{\"name\":\"Mina\",\"level\":1}"));
        resources.AddSource(new FileSource(files));
        resources.AddStep<byte[], JsonDocument>(new JsonStep());
        resources.AddStep<JsonDocument, PlayerStats>(new PlayerStatsStep());
        resources.Watch();
        using ResourceHandle<JsonDocument> json = resources.Load<JsonDocument>("live.stats.json");
        using ResourceHandle<PlayerStats> stats = resources.Load<PlayerStats>("live.stats.json");
        await Task.WhenAll(json.Ready, stats.Ready);
        files.Set("live.stats.json", Bytes("not json"));
        await PumpUntil(resources, () => json.LastReloadError is not null);
        int lastGood = stats.Value.Level;
        files.Set("live.stats.json", Bytes("{\"name\":\"Mina\",\"level\":2}"));
        await PumpUntil(resources, () => json.LastReloadError is null && stats.Value.Level == 2);
        return $"failed-reload-last-good={lastGood}; recovered-level={stats.Value.Level}; version={stats.Version}";
    }, ctx.Log);

    [Story("Examples/Resources/BrowserHttpAssets", Order = 7, SampleBundle = "resources.scenarios")]
    public static Widget BrowserHttpAssets(StoryContext ctx) => new ResourceScenarioWidget("Browser HTTP assets", async resources =>
    {
        var http = new HttpClient(new StaticHttpHandler("remote resource"));
        resources.AddSource(new HttpSource(http));
        resources.AddStep<byte[], TextAsset>(new UpperTextStep());
        using ResourceHandle<TextAsset> remote = resources.Load<TextAsset>("https://assets.example/motd.txt");
        await remote.Ready;
        return $"transport=HttpSource; value={remote.Value.Text}";
    }, ctx.Log);

    [Story("Examples/Resources/Assets/DocumentInspector", Order = 20)]
    public static Widget DocumentInspector(StoryContext ctx) => new ResourceScenarioWidget("Document inspector", async resources =>
    {
        resources.AddStep<AssetDocument, DiagnosticResult>(new DiagnosticStep(document =>
            $"scenes={document.Scenes.Count}, nodes={document.Nodes.Count}, meshes={document.Meshes.Count}, materials={document.Materials.Count}"));
        using ResourceScope scope = resources.CreateScope("scenario/asset-inspector");
        ResourceHandle<DiagnosticResult> result = scope.Create<AssetDocument, DiagnosticResult>("document", FixtureDocument());
        await result.Ready;
        return result.Value.Text;
    }, ctx.Log);

    [Story("Examples/Resources/Assets/MeshPrimitiveInspector", Order = 21)]
    public static Widget MeshPrimitiveInspector(StoryContext ctx) => new ResourceScenarioWidget("Mesh and primitive inspector", async resources =>
    {
        resources.AddStep<AssetDocument, DiagnosticResult>(new DiagnosticStep(document =>
        {
            AssetPrimitive primitive = document.Meshes[0].Primitives[0];
            int vertices = primitive.Attributes.Positions.Length;
            uint[] indices = primitive.Indices ?? [];
            bool valid = primitive.Attributes.Normals?.Length == vertices && indices.All(index => index < vertices);
            return $"vertices={vertices}; indices={indices.Length}; valid={valid}";
        }));
        using ResourceScope scope = resources.CreateScope("scenario/asset-inspector");
        ResourceHandle<DiagnosticResult> result = scope.Create<AssetDocument, DiagnosticResult>("primitive", FixtureDocument());
        await result.Ready;
        return result.Value.Text;
    }, ctx.Log);

    [Story("Examples/Resources/Assets/MaterialTextureInspector", Order = 22)]
    public static Widget MaterialTextureInspector(StoryContext ctx) => new ResourceScenarioWidget("Material and texture inspector", async resources =>
    {
        resources.AddStep<AssetDocument, DiagnosticResult>(new DiagnosticStep(document =>
        {
            AssetMaterial material = document.Materials[0];
            return $"base={material.BaseColorFactor}; alpha={material.AlphaMode}; texture=2x2; uv={material.BaseColorTexture!.TexCoordSet}";
        }));
        using ResourceScope scope = resources.CreateScope("scenario/asset-inspector");
        ResourceHandle<DiagnosticResult> result = scope.Create<AssetDocument, DiagnosticResult>("material", FixtureDocument());
        await result.Ready;
        return result.Value.Text;
    }, ctx.Log);

    [Story("Examples/Resources/Assets/AnimatedSceneGraph", Order = 23)]
    public static Widget AnimatedSceneGraph(StoryContext ctx) => new ResourceScenarioWidget("Animated scene graph", async resources =>
    {
        resources.AddStep<DiagnosticSeed, DiagnosticResult>(new DiagnosticSeedStep());
        using ResourceScope scope = resources.CreateScope("scenario/diagnostic");
        ResourceHandle<DiagnosticResult> result = scope.Create<DiagnosticSeed, DiagnosticResult>("animated-graph", new DiagnosticSeed("sample -> propagate -> extract"));
        await result.Ready;
        return result.Value.Text;
    }, ctx.Log);

    [Story("Examples/Resources/Assets/GpuAssetRegistry", Order = 24)]
    public static Widget GpuAssetRegistry(StoryContext ctx) => new ResourceScenarioWidget("GPU asset registry", async resources =>
    {
        resources.AddStep<DiagnosticSeed, DiagnosticResult>(new DiagnosticSeedStep());
        using ResourceScope scope = resources.CreateScope("scenario/diagnostic");
        ResourceHandle<DiagnosticResult> result = scope.Create<DiagnosticSeed, DiagnosticResult>("gpu-registry",
            new DiagnosticSeed("scope=preview; CPU AssetMesh -> GpuMesh lifecycle registration is explicit"));
        await result.Ready;
        return result.Value.Text;
    }, ctx.Log);

    [Story("Examples/Resources/Assets/ShaderBufferInspector", Order = 25)]
    public static Widget ShaderBufferInspector(StoryContext ctx) => new ResourceScenarioWidget("Shader buffer inspector", async resources =>
    {
        resources.AddStep<DiagnosticSeed, DiagnosticResult>(new DiagnosticSeedStep());
        using ResourceScope scope = resources.CreateScope("scenario/diagnostic");
        ResourceHandle<DiagnosticResult> result = scope.Create<DiagnosticSeed, DiagnosticResult>("shader-abi",
            new DiagnosticSeed($"MaterialGpuData={Marshal.SizeOf<MaterialGpuData>()}; SceneInstanceData={SceneInstanceData.Stride}; MorphDelta={Marshal.SizeOf<MorphDelta>()}"));
        await result.Ready;
        return result.Value.Text;
    }, ctx.Log);

    [Story("Examples/Resources/Gltf/BoxDocumentLoad", Order = 40)]
    public static Widget BoxDocumentLoad(StoryContext ctx) => new ResourceScenarioWidget("Box document load", async resources =>
    {
        resources.AddSource(new FileSource(Files(("Box.gltf", TriangleGltf))));
        resources.AddStep<byte[], AssetDocument>(new GltfResourceStep());
        using ResourceHandle<AssetDocument> box = resources.Load<AssetDocument>("Box.gltf");
        await box.Ready;
        return $"format={box.Value.SourceFormat}; meshes={box.Value.Meshes.Count}; nodes={box.Value.Nodes.Count}";
    }, ctx.Log);

    [Story("Examples/Resources/Gltf/ExternalBufferTrace", Order = 41)]
    public static Widget ExternalBufferTrace(StoryContext ctx) => new ResourceScenarioWidget("External buffer trace", async resources =>
    {
        resources.AddSource(new FileSource(BinaryTriangleFiles()));
        resources.AddStep<byte[], AssetDocument>(new GltfResourceStep());
        using ResourceHandle<AssetDocument> scene = resources.Load<AssetDocument>("models/scene.gltf");
        await scene.Ready;
        ResourceUri resolved = new ResourceUri("models/scene.gltf").Resolve("buffers/geometry.bin");
        return $"resolved={resolved.Url}; meshes={scene.Value.Meshes.Count}; dependency-loaded=True";
    }, ctx.Log);

    [Story("Examples/Resources/Gltf/MalformedAccessorDiagnostics", Order = 42)]
    public static Widget MalformedAccessorDiagnostics(StoryContext ctx) => new ResourceScenarioWidget("Malformed accessor diagnostics", async resources =>
    {
        resources.AddSource(new FileSource(Files(("broken.gltf", MalformedGltf))));
        resources.AddStep<byte[], AssetDocument>(new GltfResourceStep());
        using ResourceHandle<AssetDocument> broken = resources.Load<AssetDocument>("broken.gltf");
        try { await broken.Ready; return "unexpectedly imported"; }
        catch (Exception error) { return $"diagnostic={error.GetBaseException().Message}"; }
    }, ctx.Log);

    [Story("Examples/Resources/Gltf/ExternalDependencyReload", Order = 43)]
    public static Widget ExternalDependencyReload(StoryContext ctx) => new ResourceScenarioWidget("External dependency reload", async resources =>
    {
        MemoryFileSystem files = BinaryTriangleFiles();
        resources.AddSource(new FileSource(files));
        resources.AddStep<byte[], AssetDocument>(new GltfResourceStep());
        resources.Watch();
        using ResourceHandle<byte[]> buffer = resources.Load<byte[]>("models/buffers/geometry.bin");
        using ResourceHandle<AssetDocument> scene = resources.Load<AssetDocument>("models/scene.gltf");
        await Task.WhenAll(buffer.Ready, scene.Ready);
        int before = scene.Version;
        files.Set("models/buffers/geometry.bin", TriangleBinary(0.75f));
        await PumpUntil(resources, () => scene.Version > before);
        return $"dependency=geometry.bin; document-version={before}->{scene.Version}; last-error={scene.LastReloadError?.Message ?? "none"}";
    }, ctx.Log);
}
