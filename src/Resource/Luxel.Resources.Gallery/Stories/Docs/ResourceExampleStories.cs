using System.Runtime.InteropServices;
using System.Text.Json;
using Luxel.AssetRuntime;
using Luxel.Assets;
using Luxel.Assets.Gltf;
using Luxel.AssetsGpu;
using Luxel.Resources;
using Luxel.UI;
using static Luxel.Resources.Gallery.Stories.ResourceScenarioSupport;

namespace Luxel.Resources.Gallery.Stories;

/// <summary>標準の実行可能リソースシナリオ。各Storyに、実演するResourceSystem操作を直接記述する。</summary>
public static class ResourceExampleStories
{
    [Story("Examples/Resources/HelloTextAsset", Order = 0, SampleBundle = "resources.scenarios")]
    public static Widget HelloTextAsset(StoryContext ctx) => new ResourceScenarioWidget("テキストアセットの読み込み", async resources =>
    {
        MemoryFileSystem files = Files(("hello.txt", "こんにちは、Resources"));
        resources.AddSource(new FileSource(files));
        resources.AddStep<byte[], TextAsset>(new UpperTextStep());
        using ResourceHandle<TextAsset> value = resources.Load<TextAsset>("hello.txt");
        await value.Ready;
        return $"状態={value.Status}; 値={value.Value.Text}";
    }, ctx.Log);

    [Story("Examples/Resources/CustomPackageSource", Order = 1, SampleBundle = "resources.scenarios")]
    public static Widget CustomPackageSource(StoryContext ctx) => new ResourceScenarioWidget("カスタムパッケージSource", async resources =>
    {
        resources.AddSource(new PackageSource(new Dictionary<string, byte[]> { ["ui/title.txt"] = Bytes("パッケージのタイトル") }));
        resources.AddStep<byte[], TextAsset>(new UpperTextStep());
        using ResourceHandle<TextAsset> value = resources.Load<TextAsset>("package://ui/title.txt");
        await value.Ready;
        return $"スキーム=package; 値={value.Value.Text}";
    }, ctx.Log);

    [Story("Examples/Resources/PlayerStatsPipeline", Order = 2, SampleBundle = "resources.scenarios")]
    public static Widget PlayerStatsPipeline(StoryContext ctx) => new ResourceScenarioWidget("プレイヤー情報パイプライン", async resources =>
    {
        resources.AddSource(new FileSource(Files(("player.stats.json", "{\"name\":\"Mina\",\"level\":7}"))));
        resources.AddStep<byte[], JsonDocument>(new JsonStep());
        resources.AddStep<JsonDocument, PlayerStats>(new PlayerStatsStep());
        using ResourceHandle<PlayerStats> value = resources.Load<PlayerStats>("player.stats.json");
        await value.Ready;
        return $"パイプライン=byte[] → JsonDocument → PlayerStats; 値={value.Value.Name}、レベル={value.Value.Level}";
    }, ctx.Log);

    [Story("Examples/Resources/ExtensionSelection", Order = 3, SampleBundle = "resources.scenarios")]
    public static Widget ExtensionSelection(StoryContext ctx) => new ResourceScenarioWidget("拡張子によるStep選択", async resources =>
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
    public static Widget SharedDependencyGraph(StoryContext ctx) => new ResourceScenarioWidget("共有依存関係グラフ", async resources =>
    {
        var counter = new CountingTextStep();
        resources.AddSource(new FileSource(Files(("shared.txt", "1つの共有ノード"))));
        resources.AddStep<byte[], TextAsset>(counter);
        resources.AddStep<TextAsset, WordCount>(new WordCountStep());
        using ResourceHandle<TextAsset> text = resources.Load<TextAsset>("shared.txt");
        using ResourceHandle<WordCount> count = resources.Load<WordCount>("shared.txt");
        await Task.WhenAll(text.Ready, count.Ready);
        return $"Text Step実行回数={counter.Runs}; 単語数={count.Value.Count}; 共有={counter.Runs == 1}";
    }, ctx.Log);

    [Story("Examples/Resources/ScopedRuntimeValues", Order = 5, SampleBundle = "resources.scenarios")]
    public static Widget ScopedRuntimeValues(StoryContext ctx) => new ResourceScenarioWidget("スコープ内ランタイム値", async resources =>
    {
        resources.AddStep<RuntimeSeed, RuntimeLabel>(new RuntimeLabelStep());
        using ResourceScope scope = resources.CreateScope("scenario/player");
        ResourceHandle<RuntimeLabel> label = scope.Create<RuntimeSeed, RuntimeLabel>("level-label", new RuntimeSeed(12));
        await label.Ready;
        return $"所有者=scenario/player; 値={label.Value.Text}";
    }, ctx.Log);

    [Story("Examples/Resources/HotReloadRecovery", Order = 6, SampleBundle = "resources.scenarios")]
    public static Widget HotReloadRecovery(StoryContext ctx) => new ResourceScenarioWidget("ホットリロードからの復旧", async resources =>
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
        return $"失敗時の直近正常値={lastGood}; 復旧後のレベル={stats.Value.Level}; バージョン={stats.Version}";
    }, ctx.Log);

    [Story("Examples/Resources/BrowserHttpAssets", Order = 7, SampleBundle = "resources.scenarios")]
    public static Widget BrowserHttpAssets(StoryContext ctx) => new ResourceScenarioWidget("ブラウザーでのHTTPアセット", async resources =>
    {
        var http = new HttpClient(new StaticHttpHandler("リモートリソース"));
        resources.AddSource(new HttpSource(http));
        resources.AddStep<byte[], TextAsset>(new UpperTextStep());
        using ResourceHandle<TextAsset> remote = resources.Load<TextAsset>("https://assets.example/motd.txt");
        await remote.Ready;
        return $"転送元=HttpSource; 値={remote.Value.Text}";
    }, ctx.Log);

    [Story("Examples/Resources/Assets/DocumentInspector", Order = 20)]
    public static Widget DocumentInspector(StoryContext ctx) => new ResourceScenarioWidget("ドキュメント検査", async resources =>
    {
        resources.AddStep<AssetDocument, DiagnosticResult>(new DiagnosticStep(document =>
            $"シーン={document.Scenes.Count}、ノード={document.Nodes.Count}、メッシュ={document.Meshes.Count}、マテリアル={document.Materials.Count}"));
        using ResourceScope scope = resources.CreateScope("scenario/asset-inspector");
        ResourceHandle<DiagnosticResult> result = scope.Create<AssetDocument, DiagnosticResult>("document", FixtureDocument());
        await result.Ready;
        return result.Value.Text;
    }, ctx.Log);

    [Story("Examples/Resources/Assets/MeshPrimitiveInspector", Order = 21)]
    public static Widget MeshPrimitiveInspector(StoryContext ctx) => new ResourceScenarioWidget("メッシュとプリミティブの検査", async resources =>
    {
        resources.AddStep<AssetDocument, DiagnosticResult>(new DiagnosticStep(document =>
        {
            AssetPrimitive primitive = document.Meshes[0].Primitives[0];
            int vertices = primitive.Attributes.Positions.Length;
            uint[] indices = primitive.Indices ?? [];
            bool valid = primitive.Attributes.Normals?.Length == vertices && indices.All(index => index < vertices);
            return $"頂点数={vertices}; インデックス数={indices.Length}; 有効={valid}";
        }));
        using ResourceScope scope = resources.CreateScope("scenario/asset-inspector");
        ResourceHandle<DiagnosticResult> result = scope.Create<AssetDocument, DiagnosticResult>("primitive", FixtureDocument());
        await result.Ready;
        return result.Value.Text;
    }, ctx.Log);

    [Story("Examples/Resources/Assets/MaterialTextureInspector", Order = 22)]
    public static Widget MaterialTextureInspector(StoryContext ctx) => new ResourceScenarioWidget("マテリアルとテクスチャの検査", async resources =>
    {
        resources.AddStep<AssetDocument, DiagnosticResult>(new DiagnosticStep(document =>
        {
            AssetMaterial material = document.Materials[0];
            return $"基本色={material.BaseColorFactor}; アルファ={material.AlphaMode}; テクスチャ=2x2; UV={material.BaseColorTexture!.TexCoordSet}";
        }));
        using ResourceScope scope = resources.CreateScope("scenario/asset-inspector");
        ResourceHandle<DiagnosticResult> result = scope.Create<AssetDocument, DiagnosticResult>("material", FixtureDocument());
        await result.Ready;
        return result.Value.Text;
    }, ctx.Log);

    [Story("Examples/Resources/Assets/AnimatedSceneGraph", Order = 23)]
    public static Widget AnimatedSceneGraph(StoryContext ctx) => new ResourceScenarioWidget("アニメーション付きシーングラフ", async resources =>
    {
        resources.AddStep<DiagnosticSeed, DiagnosticResult>(new DiagnosticSeedStep());
        using ResourceScope scope = resources.CreateScope("scenario/diagnostic");
        ResourceHandle<DiagnosticResult> result = scope.Create<DiagnosticSeed, DiagnosticResult>("animated-graph", new DiagnosticSeed("サンプリング → 伝播 → 抽出"));
        await result.Ready;
        return result.Value.Text;
    }, ctx.Log);

    [Story("Examples/Resources/Assets/GpuAssetRegistry", Order = 24)]
    public static Widget GpuAssetRegistry(StoryContext ctx) => new ResourceScenarioWidget("GPUアセット登録", async resources =>
    {
        resources.AddStep<DiagnosticSeed, DiagnosticResult>(new DiagnosticSeedStep());
        using ResourceScope scope = resources.CreateScope("scenario/diagnostic");
        ResourceHandle<DiagnosticResult> result = scope.Create<DiagnosticSeed, DiagnosticResult>("gpu-registry",
            new DiagnosticSeed("スコープ=preview; CPUのAssetMesh → GpuMeshの寿命登録は明示的"));
        await result.Ready;
        return result.Value.Text;
    }, ctx.Log);

    [Story("Examples/Resources/Assets/ShaderBufferInspector", Order = 25)]
    public static Widget ShaderBufferInspector(StoryContext ctx) => new ResourceScenarioWidget("シェーダーバッファの検査", async resources =>
    {
        resources.AddStep<DiagnosticSeed, DiagnosticResult>(new DiagnosticSeedStep());
        using ResourceScope scope = resources.CreateScope("scenario/diagnostic");
        ResourceHandle<DiagnosticResult> result = scope.Create<DiagnosticSeed, DiagnosticResult>("shader-abi",
            new DiagnosticSeed($"MaterialGpuData={Marshal.SizeOf<MaterialGpuData>()}; SceneInstanceData={SceneInstanceData.Stride}; MorphDelta={Marshal.SizeOf<MorphDelta>()}"));
        await result.Ready;
        return result.Value.Text;
    }, ctx.Log);

    [Story("Examples/Resources/Gltf/BoxDocumentLoad", Order = 40)]
    public static Widget BoxDocumentLoad(StoryContext ctx) => new ResourceScenarioWidget("Boxドキュメントの読み込み", async resources =>
    {
        resources.AddSource(new FileSource(Files(("Box.gltf", TriangleGltf))));
        resources.AddStep<byte[], AssetDocument>(new GltfResourceStep());
        using ResourceHandle<AssetDocument> box = resources.Load<AssetDocument>("Box.gltf");
        await box.Ready;
        return $"形式={box.Value.SourceFormat}; メッシュ数={box.Value.Meshes.Count}; ノード数={box.Value.Nodes.Count}";
    }, ctx.Log);

    [Story("Examples/Resources/Gltf/ExternalBufferTrace", Order = 41)]
    public static Widget ExternalBufferTrace(StoryContext ctx) => new ResourceScenarioWidget("外部バッファの追跡", async resources =>
    {
        resources.AddSource(new FileSource(BinaryTriangleFiles()));
        resources.AddStep<byte[], AssetDocument>(new GltfResourceStep());
        using ResourceHandle<AssetDocument> scene = resources.Load<AssetDocument>("models/scene.gltf");
        await scene.Ready;
        ResourceUri resolved = new ResourceUri("models/scene.gltf").Resolve("buffers/geometry.bin");
        return $"解決先={resolved.Url}; メッシュ数={scene.Value.Meshes.Count}; 依存先読み込み済み=True";
    }, ctx.Log);

    [Story("Examples/Resources/Gltf/MalformedAccessorDiagnostics", Order = 42)]
    public static Widget MalformedAccessorDiagnostics(StoryContext ctx) => new ResourceScenarioWidget("不正なアクセサーの診断", async resources =>
    {
        resources.AddSource(new FileSource(Files(("broken.gltf", MalformedGltf))));
        resources.AddStep<byte[], AssetDocument>(new GltfResourceStep());
        using ResourceHandle<AssetDocument> broken = resources.Load<AssetDocument>("broken.gltf");
        try { await broken.Ready; return "予期せずインポートに成功しました"; }
        catch (Exception error) { return $"診断={error.GetBaseException().Message}"; }
    }, ctx.Log);

    [Story("Examples/Resources/Gltf/ExternalDependencyReload", Order = 43)]
    public static Widget ExternalDependencyReload(StoryContext ctx) => new ResourceScenarioWidget("外部依存先の再読み込み", async resources =>
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
        return $"依存先=geometry.bin; ドキュメントバージョン={before}→{scene.Version}; 最終エラー={scene.LastReloadError?.Message ?? "なし"}";
    }, ctx.Log);
}
