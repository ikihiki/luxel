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

internal static class ResourceScenarioSupport
{
    internal static AssetDocument FixtureDocument()
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

    internal static MemoryFileSystem Files(params (string Path, string Text)[] entries)
    {
        var files = new MemoryFileSystem();
        foreach ((string path, string text) in entries) files.Set(path, Bytes(text));
        return files;
    }

    internal static MemoryFileSystem BinaryTriangleFiles()
    {
        MemoryFileSystem files = Files(("models/scene.gltf", ExternalTriangleGltf));
        files.Set("models/buffers/geometry.bin", TriangleBinary(.5f));
        return files;
    }

    internal static byte[] TriangleBinary(float height)
    {
        float[] values = [0, 0, 0, 1, 0, 0, 0, height, 0];
        byte[] bytes = new byte[values.Length * sizeof(float)];
        Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    internal static byte[] Bytes(string value) => Encoding.UTF8.GetBytes(value);

    internal static async Task PumpUntil(ResourceSystem resources, Func<bool> condition)
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

    internal const string TriangleGltf = """
        {"asset":{"version":"2.0"},"buffers":[{"uri":"data:application/octet-stream;base64,AAAAAAAAAAAAAAAAAACAPwAAAAAAAAAAAAAAAAAAgD8AAAAA","byteLength":36}],"bufferViews":[{"buffer":0,"byteOffset":0,"byteLength":36}],"accessors":[{"bufferView":0,"componentType":5126,"count":3,"type":"VEC3"}],"meshes":[{"primitives":[{"attributes":{"POSITION":0}}]}],"nodes":[{"mesh":0}],"scenes":[{"nodes":[0]}],"scene":0}
        """;
    internal const string ExternalTriangleGltf = """
        {"asset":{"version":"2.0"},"buffers":[{"uri":"buffers/geometry.bin","byteLength":36}],"bufferViews":[{"buffer":0,"byteOffset":0,"byteLength":36}],"accessors":[{"bufferView":0,"componentType":5126,"count":3,"type":"VEC3"}],"meshes":[{"primitives":[{"attributes":{"POSITION":0}}]}],"nodes":[{"mesh":0}],"scenes":[{"nodes":[0]}],"scene":0}
        """;
    internal const string MalformedGltf = """
        {"asset":{"version":"2.0"},"buffers":[{"uri":"data:application/octet-stream;base64,AAAAAA==","byteLength":4}],"bufferViews":[{"buffer":0,"byteLength":4}],"accessors":[{"bufferView":0,"componentType":5126,"count":3,"type":"VEC3"}],"meshes":[{"primitives":[{"attributes":{"POSITION":0}}]}]}
        """;

    internal sealed record TextAsset(string Text);
    internal sealed record MessageAsset(string Text);
    internal sealed record PlayerStats(string Name, int Level);
    internal sealed record WordCount(int Count);
    internal sealed record RuntimeSeed(int Level);
    internal sealed record RuntimeLabel(string Text);
    internal sealed record DiagnosticSeed(string Text);
    internal sealed record DiagnosticResult(string Text);

    internal sealed class UpperTextStep : IResourceStep<byte[], TextAsset> { public Executor Executor => Executor.Cpu; public IEnumerable<string> Extensions => [".txt"]; public Task<TextAsset> RunAsync(byte[] input, ResourceUri uri, LoadContext context) => Task.FromResult(new TextAsset(Encoding.UTF8.GetString(input).ToUpperInvariant())); }
    internal sealed class PackageSource(IReadOnlyDictionary<string, byte[]> entries) : IResourceSource { public IEnumerable<string> Schemes => ["package"]; public Task<byte[]> ReadAsync(ResourceUri uri, LoadContext context) => entries.TryGetValue(uri.Path, out byte[]? data) ? Task.FromResult((byte[])data.Clone()) : Task.FromException<byte[]>(new FileNotFoundException(uri.Path)); }
    internal sealed class JsonStep : IResourceStep<byte[], JsonDocument> { public Executor Executor => Executor.Cpu; public IEnumerable<string> Extensions => [".json"]; public Task<JsonDocument> RunAsync(byte[] input, ResourceUri uri, LoadContext context) => Task.FromResult(JsonDocument.Parse(input)); }
    internal sealed class PlayerStatsStep : IResourceStep<JsonDocument, PlayerStats> { public Executor Executor => Executor.Cpu; public Task<PlayerStats> RunAsync(JsonDocument input, ResourceUri uri, LoadContext context) => Task.FromResult(new PlayerStats(input.RootElement.GetProperty("name").GetString()!, input.RootElement.GetProperty("level").GetInt32())); }
    internal sealed class PlainMessageStep : IResourceStep<byte[], MessageAsset> { public Executor Executor => Executor.Cpu; public IEnumerable<string> Extensions => [".txt"]; public Task<MessageAsset> RunAsync(byte[] input, ResourceUri uri, LoadContext context) => Task.FromResult(new MessageAsset(Encoding.UTF8.GetString(input))); }
    internal sealed class CaptionMessageStep : IResourceStep<byte[], MessageAsset> { public Executor Executor => Executor.Cpu; public IEnumerable<string> Extensions => [".caption"]; public Task<MessageAsset> RunAsync(byte[] input, ResourceUri uri, LoadContext context) => Task.FromResult(new MessageAsset($"[{Encoding.UTF8.GetString(input)}]")); }
    internal sealed class CountingTextStep : IResourceStep<byte[], TextAsset> { public int Runs; public Executor Executor => Executor.Cpu; public IEnumerable<string> Extensions => [".txt"]; public Task<TextAsset> RunAsync(byte[] input, ResourceUri uri, LoadContext context) { Interlocked.Increment(ref Runs); return Task.FromResult(new TextAsset(Encoding.UTF8.GetString(input))); } }
    internal sealed class WordCountStep : IResourceStep<TextAsset, WordCount> { public Executor Executor => Executor.Cpu; public Task<WordCount> RunAsync(TextAsset input, ResourceUri uri, LoadContext context) => Task.FromResult(new WordCount(input.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length)); }
    internal sealed class RuntimeLabelStep : IResourceStep<RuntimeSeed, RuntimeLabel> { public Executor Executor => Executor.Cpu; public Task<RuntimeLabel> RunAsync(RuntimeSeed input, ResourceUri uri, LoadContext context) => Task.FromResult(new RuntimeLabel($"Level {input.Level}")); }
    internal sealed class DiagnosticStep(Func<AssetDocument, string> inspect) : IResourceStep<AssetDocument, DiagnosticResult> { public Executor Executor => Executor.Cpu; public Task<DiagnosticResult> RunAsync(AssetDocument input, ResourceUri uri, LoadContext context) => Task.FromResult(new DiagnosticResult(inspect(input))); }
    internal sealed class DiagnosticSeedStep : IResourceStep<DiagnosticSeed, DiagnosticResult> { public Executor Executor => Executor.Cpu; public Task<DiagnosticResult> RunAsync(DiagnosticSeed input, ResourceUri uri, LoadContext context) => Task.FromResult(new DiagnosticResult(input.Text)); }
    internal sealed class StaticHttpHandler(string content) : HttpMessageHandler { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Bytes(content)) }); }
}
