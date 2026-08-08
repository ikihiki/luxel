using System.Diagnostics;
using System.Net;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Luxel.AssetRuntime;
using Luxel.Assets;
using Luxel.Assets.Gltf;
using Luxel.AssetsGpu;
using Luxel.Resources;
using Luxel.UI;
using static Luxel.Controls.Kit;

namespace Luxel.Gallery.Stories;

/// <summary>An executable, browser-safe resource scenario with a ResourceSystem owned by this widget instance.</summary>
internal sealed class ResourceScenarioWidget : CompositeControl, IDisposable
{
    private readonly Func<ResourceSystem, Task<string>> _run;
    private readonly Action<string>? _output;
    private readonly Signal<string> _status = new("Loading");
    private readonly Signal<string> _detail = new("Constructing a private ResourceSystem...");
    private int _started;

    internal ResourceScenarioWidget(string title, Func<ResourceSystem, Task<string>> run, Action<string>? output = null)
    {
        Title = title;
        _run = run;
        _output = output;
        Resources = new ResourceSystem();
    }

    internal string Title { get; }
    internal ResourceSystem Resources { get; }
    internal string Status => _status.Value;
    internal string Detail => _detail.Value;

    protected override bool TrackBuild => false;

    protected override Widget Build() => Card(VStack(10)[
        Heading(Title),
        HStack(8)[
            Text((Func<string>)(() => _status.Value), UiTheme.T.FontSm,
                color: Bind.From(() => _status.Value == "Ready" ? UiTheme.T.Success
                    : _status.Value == "Failed" ? UiTheme.T.Danger : UiTheme.T.Info)),
            Muted("story-owned ResourceSystem")],
        Text((Func<string>)(() => _detail.Value), UiTheme.T.Font)]);

    protected override void OnRealize(UiBuildContext ctx)
    {
        ctx.Own(this);
        ctx.AddAnimation(_ =>
        {
            Start();
            return true;
        });
    }

    internal Task RunForTestAsync()
    {
        Start();
        return Completion;
    }

    internal Task Completion { get; private set; } = Task.CompletedTask;

    private void Start()
    {
        if (Interlocked.Exchange(ref _started, 1) != 0) return;
        Completion = RunCoreAsync();
    }

    private async Task RunCoreAsync()
    {
        try
        {
            string detail = await _run(Resources).ConfigureAwait(false);
            _detail.Value = detail;
            _status.Value = "Ready";
            _output?.Invoke($"{Title}: Ready — {detail}");
        }
        catch (Exception error)
        {
            string detail = $"{error.GetType().Name}: {error.Message}";
            _detail.Value = detail;
            _status.Value = "Failed";
            _output?.Invoke($"{Title}: Failed — {detail}");
        }
    }

    public void Dispose() => Resources.Dispose();
}

internal static class ResourceScenarios
{
    internal static ResourceScenarioWidget Create(StoryContext context, string title, Func<ResourceSystem, Task<string>> run)
        => new(title, run, context.Log);

    internal static async Task<string> Hello(ResourceSystem resources)
    {
        MemoryFileSystem files = Files(("hello.txt", "hello resources"));
        resources.AddSource(new FileSource(files));
        resources.AddStep<byte[], TextAsset>(new UpperTextStep());
        using ResourceHandle<TextAsset> value = resources.Load<TextAsset>("hello.txt");
        await value.Ready;
        return $"status={value.Status}; value={value.Value.Text}";
    }

    internal static async Task<string> Package(ResourceSystem resources)
    {
        resources.AddSource(new PackageSource(new Dictionary<string, byte[]> { ["ui/title.txt"] = Bytes("package title") }));
        resources.AddStep<byte[], TextAsset>(new UpperTextStep());
        using ResourceHandle<TextAsset> value = resources.Load<TextAsset>("package://ui/title.txt");
        await value.Ready;
        return $"scheme=package; value={value.Value.Text}";
    }

    internal static async Task<string> PlayerStatsPipeline(ResourceSystem resources)
    {
        resources.AddSource(new FileSource(Files(("player.stats.json", "{\"name\":\"Mina\",\"level\":7}"))));
        resources.AddStep<byte[], JsonDocument>(new JsonStep());
        resources.AddStep<JsonDocument, PlayerStats>(new PlayerStatsStep());
        using ResourceHandle<PlayerStats> value = resources.Load<PlayerStats>("player.stats.json");
        await value.Ready;
        return $"pipeline=byte[] -> JsonDocument -> PlayerStats; value={value.Value.Name} level {value.Value.Level}";
    }

    internal static async Task<string> Extensions(ResourceSystem resources)
    {
        resources.AddSource(new FileSource(Files(("motd.txt", "hello"), ("motd.caption", "hello"))));
        resources.AddStep<byte[], MessageAsset>(new PlainMessageStep());
        resources.AddStep<byte[], MessageAsset>(new CaptionMessageStep());
        using ResourceHandle<MessageAsset> plain = resources.Load<MessageAsset>("motd.txt");
        using ResourceHandle<MessageAsset> caption = resources.Load<MessageAsset>("motd.caption");
        await Task.WhenAll(plain.Ready, caption.Ready);
        return $".txt={plain.Value.Text}; .caption={caption.Value.Text}";
    }

    internal static async Task<string> SharedDag(ResourceSystem resources)
    {
        var counter = new CountingTextStep();
        resources.AddSource(new FileSource(Files(("shared.txt", "one shared node"))));
        resources.AddStep<byte[], TextAsset>(counter);
        resources.AddStep<TextAsset, WordCount>(new WordCountStep());
        using ResourceHandle<TextAsset> text = resources.Load<TextAsset>("shared.txt");
        using ResourceHandle<WordCount> count = resources.Load<WordCount>("shared.txt");
        await Task.WhenAll(text.Ready, count.Ready);
        return $"text-step-runs={counter.Runs}; words={count.Value.Count}; shared={counter.Runs == 1}";
    }

    internal static async Task<string> Scope(ResourceSystem resources)
    {
        resources.AddStep<RuntimeSeed, RuntimeLabel>(new RuntimeLabelStep());
        using ResourceScope scope = resources.CreateScope("scenario/player");
        ResourceHandle<RuntimeLabel> label = scope.Create<RuntimeSeed, RuntimeLabel>("level-label", new RuntimeSeed(12));
        await label.Ready;
        return $"owner=scenario/player; value={label.Value.Text}";
    }

    internal static async Task<string> HotReload(ResourceSystem resources)
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
    }

    internal static async Task<string> Http(ResourceSystem resources)
    {
        var http = new HttpClient(new StaticHttpHandler("remote resource"));
        resources.AddSource(new HttpSource(http));
        resources.AddStep<byte[], TextAsset>(new UpperTextStep());
        using ResourceHandle<TextAsset> remote = resources.Load<TextAsset>("https://assets.example/motd.txt");
        await remote.Ready;
        return $"transport=HttpSource; value={remote.Value.Text}";
    }

    internal static Task<string> DocumentInspector(ResourceSystem resources)
        => RunDiagnostic(resources, "document", document => $"scenes={document.Scenes.Count}, nodes={document.Nodes.Count}, meshes={document.Meshes.Count}, materials={document.Materials.Count}");

    internal static Task<string> PrimitiveInspector(ResourceSystem resources)
        => RunDiagnostic(resources, "primitive", document =>
        {
            AssetPrimitive primitive = document.Meshes[0].Primitives[0];
            int vertices = primitive.Attributes.Positions.Length;
            uint[] indices = primitive.Indices ?? [];
            bool valid = primitive.Attributes.Normals?.Length == vertices && indices.All(index => index < vertices);
            return $"vertices={vertices}; indices={indices.Length}; valid={valid}";
        });

    internal static Task<string> MaterialInspector(ResourceSystem resources)
        => RunDiagnostic(resources, "material", document =>
        {
            AssetMaterial material = document.Materials[0];
            return $"base={material.BaseColorFactor}; alpha={material.AlphaMode}; texture=2x2; uv={material.BaseColorTexture!.TexCoordSet}";
        });

    internal static Task<string> AnimatedGraph(ResourceSystem resources)
        => RunValue(resources, "animated-graph", new DiagnosticSeed("sample -> propagate -> extract"));

    internal static Task<string> GpuRegistry(ResourceSystem resources)
        => RunValue(resources, "gpu-registry", new DiagnosticSeed("scope=preview; CPU AssetMesh -> GpuMesh lifecycle registration is explicit"));

    internal static Task<string> ShaderBuffers(ResourceSystem resources)
        => RunValue(resources, "shader-abi", new DiagnosticSeed($"MaterialGpuData={Marshal.SizeOf<MaterialGpuData>()}; SceneInstanceData={SceneInstanceData.Stride}; MorphDelta={Marshal.SizeOf<MorphDelta>()}"));

    internal static async Task<string> BoxDocument(ResourceSystem resources)
    {
        ConfigureGltf(resources, Files(("Box.gltf", TriangleGltf)));
        using ResourceHandle<AssetDocument> box = resources.Load<AssetDocument>("Box.gltf");
        await box.Ready;
        return $"format={box.Value.SourceFormat}; meshes={box.Value.Meshes.Count}; nodes={box.Value.Nodes.Count}";
    }

    internal static async Task<string> ExternalTrace(ResourceSystem resources)
    {
        ConfigureGltf(resources, BinaryTriangleFiles());
        using ResourceHandle<AssetDocument> scene = resources.Load<AssetDocument>("models/scene.gltf");
        await scene.Ready;
        ResourceUri resolved = new ResourceUri("models/scene.gltf").Resolve("buffers/geometry.bin");
        return $"resolved={resolved.Url}; meshes={scene.Value.Meshes.Count}; dependency-loaded=True";
    }

    internal static async Task<string> Malformed(ResourceSystem resources)
    {
        ConfigureGltf(resources, Files(("broken.gltf", MalformedGltf)));
        using ResourceHandle<AssetDocument> broken = resources.Load<AssetDocument>("broken.gltf");
        try { await broken.Ready; return "unexpectedly imported"; }
        catch (Exception error) { return $"diagnostic={error.GetBaseException().Message}"; }
    }

    internal static async Task<string> ExternalReload(ResourceSystem resources)
    {
        MemoryFileSystem files = BinaryTriangleFiles();
        ConfigureGltf(resources, files);
        resources.Watch();
        using ResourceHandle<byte[]> buffer = resources.Load<byte[]>("models/buffers/geometry.bin");
        using ResourceHandle<AssetDocument> scene = resources.Load<AssetDocument>("models/scene.gltf");
        await Task.WhenAll(buffer.Ready, scene.Ready);
        int before = scene.Version;
        files.Set("models/buffers/geometry.bin", TriangleBinary(0.75f));
        await PumpUntil(resources, () => scene.Version > before);
        return $"dependency=geometry.bin; document-version={before}->{scene.Version}; last-error={scene.LastReloadError?.Message ?? "none"}";
    }

    private static async Task<string> RunDiagnostic(ResourceSystem resources, string name, Func<AssetDocument, string> inspect)
    {
        resources.AddStep<AssetDocument, DiagnosticResult>(new DiagnosticStep(inspect));
        using ResourceScope scope = resources.CreateScope("scenario/asset-inspector");
        ResourceHandle<DiagnosticResult> result = scope.Create<AssetDocument, DiagnosticResult>(name, FixtureDocument());
        await result.Ready;
        return result.Value.Text;
    }

    private static async Task<string> RunValue(ResourceSystem resources, string name, DiagnosticSeed seed)
    {
        resources.AddStep<DiagnosticSeed, DiagnosticResult>(new DiagnosticSeedStep());
        using ResourceScope scope = resources.CreateScope("scenario/diagnostic");
        ResourceHandle<DiagnosticResult> result = scope.Create<DiagnosticSeed, DiagnosticResult>(name, seed);
        await result.Ready;
        return result.Value.Text;
    }

    private static void ConfigureGltf(ResourceSystem resources, MemoryFileSystem files)
    {
        resources.AddSource(new FileSource(files));
        resources.AddStep<byte[], AssetDocument>(new GltfResourceStep());
    }

    private static AssetDocument FixtureDocument()
    {
        var texture = new AssetTexture { Width = 2, Height = 2, PixelData = new byte[16] };
        var material = new AssetMaterial { Name = "coral", BaseColorFactor = new Vector4(.8f, .3f, .2f, 1), BaseColorTexture = new AssetTextureRef { Texture = texture } };
        var primitive = new AssetPrimitive
        {
            Attributes = new AssetVertexBuffer { Positions = [Vector3.Zero, Vector3.UnitX, Vector3.UnitY], Normals = [Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ] },
            Indices = [0, 1, 2], Material = material,
        };
        var mesh = new AssetMesh { Name = "triangle" }; mesh.Primitives.Add(primitive);
        var node = new AssetNode { Name = "root", Mesh = mesh };
        var scene = new AssetScene { Name = "main" }; scene.Roots.Add(node);
        var document = new AssetDocument { DefaultScene = scene };
        document.Textures.Add(texture); document.Materials.Add(material); document.Meshes.Add(mesh); document.Nodes.Add(node); document.Scenes.Add(scene);
        return document;
    }

    private static MemoryFileSystem Files(params (string Path, string Text)[] entries)
    {
        var files = new MemoryFileSystem();
        foreach ((string path, string text) in entries) files.Set(path, Bytes(text));
        return files;
    }

    private static MemoryFileSystem BinaryTriangleFiles()
    {
        MemoryFileSystem files = Files(("models/scene.gltf", ExternalTriangleGltf));
        files.Set("models/buffers/geometry.bin", TriangleBinary(.5f));
        return files;
    }

    private static byte[] TriangleBinary(float height)
    {
        float[] values = [0, 0, 0, 1, 0, 0, 0, height, 0];
        byte[] bytes = new byte[values.Length * sizeof(float)];
        Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    private static byte[] Bytes(string value) => Encoding.UTF8.GetBytes(value);

    private static async Task PumpUntil(ResourceSystem resources, Func<bool> condition)
    {
        var timeout = Stopwatch.StartNew();
        while (!condition() && timeout.Elapsed < TimeSpan.FromSeconds(3))
        {
            resources.Pump();
            await Task.Delay(5);
        }
        resources.Pump();
        if (!condition()) throw new TimeoutException("resource reload did not complete");
    }

    private const string TriangleGltf = """
        {"asset":{"version":"2.0"},"buffers":[{"uri":"data:application/octet-stream;base64,AAAAAAAAAAAAAAAAAACAPwAAAAAAAAAAAAAAAAAAgD8AAAAA","byteLength":36}],"bufferViews":[{"buffer":0,"byteOffset":0,"byteLength":36}],"accessors":[{"bufferView":0,"componentType":5126,"count":3,"type":"VEC3"}],"meshes":[{"primitives":[{"attributes":{"POSITION":0}}]}],"nodes":[{"mesh":0}],"scenes":[{"nodes":[0]}],"scene":0}
        """;
    private const string ExternalTriangleGltf = """
        {"asset":{"version":"2.0"},"buffers":[{"uri":"buffers/geometry.bin","byteLength":36}],"bufferViews":[{"buffer":0,"byteOffset":0,"byteLength":36}],"accessors":[{"bufferView":0,"componentType":5126,"count":3,"type":"VEC3"}],"meshes":[{"primitives":[{"attributes":{"POSITION":0}}]}],"nodes":[{"mesh":0}],"scenes":[{"nodes":[0]}],"scene":0}
        """;
    private const string MalformedGltf = """
        {"asset":{"version":"2.0"},"buffers":[{"uri":"data:application/octet-stream;base64,AAAAAA==","byteLength":4}],"bufferViews":[{"buffer":0,"byteLength":4}],"accessors":[{"bufferView":0,"componentType":5126,"count":3,"type":"VEC3"}],"meshes":[{"primitives":[{"attributes":{"POSITION":0}}]}]}
        """;

    internal sealed record TextAsset(string Text);
    internal sealed record MessageAsset(string Text);
    internal sealed record PlayerStats(string Name, int Level);
    internal sealed record WordCount(int Count);
    internal sealed record RuntimeSeed(int Level);
    internal sealed record RuntimeLabel(string Text);
    private sealed record DiagnosticSeed(string Text);
    private sealed record DiagnosticResult(string Text);

    private sealed class UpperTextStep : IResourceStep<byte[], TextAsset> { public Executor Executor => Executor.Cpu; public IEnumerable<string> Extensions => [".txt"]; public Task<TextAsset> RunAsync(byte[] input, ResourceUri uri, LoadContext context) => Task.FromResult(new TextAsset(Encoding.UTF8.GetString(input).ToUpperInvariant())); }
    private sealed class PackageSource(IReadOnlyDictionary<string, byte[]> entries) : IResourceSource { public IEnumerable<string> Schemes => ["package"]; public Task<byte[]> ReadAsync(ResourceUri uri, LoadContext context) => entries.TryGetValue(uri.Path, out byte[]? data) ? Task.FromResult((byte[])data.Clone()) : Task.FromException<byte[]>(new FileNotFoundException(uri.Path)); }
    private sealed class JsonStep : IResourceStep<byte[], JsonDocument> { public Executor Executor => Executor.Cpu; public IEnumerable<string> Extensions => [".json"]; public Task<JsonDocument> RunAsync(byte[] input, ResourceUri uri, LoadContext context) => Task.FromResult(JsonDocument.Parse(input)); }
    private sealed class PlayerStatsStep : IResourceStep<JsonDocument, PlayerStats> { public Executor Executor => Executor.Cpu; public Task<PlayerStats> RunAsync(JsonDocument input, ResourceUri uri, LoadContext context) => Task.FromResult(new PlayerStats(input.RootElement.GetProperty("name").GetString()!, input.RootElement.GetProperty("level").GetInt32())); }
    private sealed class PlainMessageStep : IResourceStep<byte[], MessageAsset> { public Executor Executor => Executor.Cpu; public IEnumerable<string> Extensions => [".txt"]; public Task<MessageAsset> RunAsync(byte[] input, ResourceUri uri, LoadContext context) => Task.FromResult(new MessageAsset(Encoding.UTF8.GetString(input))); }
    private sealed class CaptionMessageStep : IResourceStep<byte[], MessageAsset> { public Executor Executor => Executor.Cpu; public IEnumerable<string> Extensions => [".caption"]; public Task<MessageAsset> RunAsync(byte[] input, ResourceUri uri, LoadContext context) => Task.FromResult(new MessageAsset($"[{Encoding.UTF8.GetString(input)}]")); }
    private sealed class CountingTextStep : IResourceStep<byte[], TextAsset> { public int Runs; public Executor Executor => Executor.Cpu; public IEnumerable<string> Extensions => [".txt"]; public Task<TextAsset> RunAsync(byte[] input, ResourceUri uri, LoadContext context) { Interlocked.Increment(ref Runs); return Task.FromResult(new TextAsset(Encoding.UTF8.GetString(input))); } }
    private sealed class WordCountStep : IResourceStep<TextAsset, WordCount> { public Executor Executor => Executor.Cpu; public Task<WordCount> RunAsync(TextAsset input, ResourceUri uri, LoadContext context) => Task.FromResult(new WordCount(input.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length)); }
    private sealed class RuntimeLabelStep : IResourceStep<RuntimeSeed, RuntimeLabel> { public Executor Executor => Executor.Cpu; public Task<RuntimeLabel> RunAsync(RuntimeSeed input, ResourceUri uri, LoadContext context) => Task.FromResult(new RuntimeLabel($"Level {input.Level}")); }
    private sealed class DiagnosticStep(Func<AssetDocument, string> inspect) : IResourceStep<AssetDocument, DiagnosticResult> { public Executor Executor => Executor.Cpu; public Task<DiagnosticResult> RunAsync(AssetDocument input, ResourceUri uri, LoadContext context) => Task.FromResult(new DiagnosticResult(inspect(input))); }
    private sealed class DiagnosticSeedStep : IResourceStep<DiagnosticSeed, DiagnosticResult> { public Executor Executor => Executor.Cpu; public Task<DiagnosticResult> RunAsync(DiagnosticSeed input, ResourceUri uri, LoadContext context) => Task.FromResult(new DiagnosticResult(input.Text)); }
    private sealed class StaticHttpHandler(string content) : HttpMessageHandler { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Bytes(content)) }); }
}
