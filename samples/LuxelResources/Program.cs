using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using Luxel.Resources;

await HelloTextAsset();
await CustomPackageSource();
await PlayerStatsPipeline();
await ExtensionSelection();
await SharedDependencyGraph();
await ScopedRuntimeValues();
await HotReloadRecovery();
await BrowserHttpAssets();
Console.WriteLine("resources: status=Ready, value=HELLO RESOURCES, scenarios=8");

// docs:begin hello-text-asset
static async Task HelloTextAsset()
{
    var files = new MemoryFileSystem();
    files.Set("hello.txt", Encoding.UTF8.GetBytes("hello resources"));
    using var resources = new ResourceSystem(
        sources: [new FileSource(files)],
        steps: [new Utf8TextStep()]);
    using ResourceHandle<TextAsset> handle = resources.Load<TextAsset>("hello.txt");
    await handle.Ready;
    Ensure(handle.Value.Text == "HELLO RESOURCES", "typed text load");
}
// docs:end hello-text-asset

// docs:begin custom-package-source
static async Task CustomPackageSource()
{
    using var resources = new ResourceSystem(
        sources: [new PackageSource(new Dictionary<string, byte[]> { ["ui/title.txt"] = Encoding.UTF8.GetBytes("package title") })],
        steps: [new Utf8TextStep()]);
    using ResourceHandle<TextAsset> title = resources.Load<TextAsset>("package://ui/title.txt");
    await title.Ready;
    Ensure(title.Value.Text == "PACKAGE TITLE", "custom package source");
}
// docs:end custom-package-source

// docs:begin player-stats-pipeline
static async Task PlayerStatsPipeline()
{
    var files = new MemoryFileSystem();
    files.Set("player.stats.json", Encoding.UTF8.GetBytes("{\"name\":\"Mina\",\"level\":7}"));
    using var resources = new ResourceSystem(
        sources: [new FileSource(files)],
        steps: [new JsonDocumentStep(), new PlayerStatsStep()]);
    using ResourceHandle<PlayerStats> stats = resources.Load<PlayerStats>("player.stats.json");
    await stats.Ready;
    Ensure(stats.Value is { Name: "Mina", Level: 7 }, "multi-step player stats pipeline");
}
// docs:end player-stats-pipeline

// docs:begin extension-selection
static async Task ExtensionSelection()
{
    var files = new MemoryFileSystem();
    files.Set("motd.txt", Encoding.UTF8.GetBytes("hello"));
    files.Set("motd.caption", Encoding.UTF8.GetBytes("hello"));
    using var resources = new ResourceSystem(
        sources: [new FileSource(files)],
        steps: [new PlainMessageStep(), new CaptionMessageStep()]);
    using ResourceHandle<MessageAsset> plain = resources.Load<MessageAsset>("motd.txt");
    using ResourceHandle<MessageAsset> caption = resources.Load<MessageAsset>("motd.caption");
    await Task.WhenAll(plain.Ready, caption.Ready);
    Ensure(plain.Value.Text == "hello" && caption.Value.Text == "[hello]", "extension-selected steps");
}
// docs:end extension-selection

// docs:begin shared-dependency-graph
static async Task SharedDependencyGraph()
{
    CountingTextStep.Runs = 0;
    var files = new MemoryFileSystem();
    files.Set("shared.txt", Encoding.UTF8.GetBytes("shared"));
    using var resources = new ResourceSystem(
        sources: [new FileSource(files)],
        steps: [new CountingTextStep(), new WordCountStep()]);
    using ResourceHandle<TextAsset> text = resources.Load<TextAsset>("shared.txt");
    using ResourceHandle<WordCount> count = resources.Load<WordCount>("shared.txt");
    await Task.WhenAll(text.Ready, count.Ready);
    Ensure(CountingTextStep.Runs == 1 && count.Value.Count == 1, "shared intermediate cache node");
}
// docs:end shared-dependency-graph

// docs:begin scoped-runtime-values
static async Task ScopedRuntimeValues()
{
    using var resources = new ResourceSystem(steps: [new RuntimeLabelStep()]);
    using ResourceScope scene = resources.CreateScope("scene/player");
    ResourceHandle<RuntimeLabel> label = scene.Create<RuntimeSeed, RuntimeLabel>("level-label", new RuntimeSeed(12));
    await label.Ready;
    Ensure(label.Value.Text == "Level 12", "scope-local runtime value");
}
// docs:end scoped-runtime-values

// docs:begin hot-reload-recovery
static async Task HotReloadRecovery()
{
    var files = new MemoryFileSystem();
    files.Set("live.stats.json", Encoding.UTF8.GetBytes("{\"name\":\"Mina\",\"level\":1}"));
    using var resources = new ResourceSystem(
        sources: [new FileSource(files)],
        steps: [new JsonDocumentStep(), new PlayerStatsStep()]);
    resources.Watch();
    using ResourceHandle<JsonDocument> json = resources.Load<JsonDocument>("live.stats.json");
    using ResourceHandle<PlayerStats> stats = resources.Load<PlayerStats>("live.stats.json");
    await Task.WhenAll(json.Ready, stats.Ready);

    files.Set("live.stats.json", Encoding.UTF8.GetBytes("not json"));
    await PumpUntil(resources, () => json.LastReloadError is not null);
    Ensure(json.HasValue && stats.HasValue && stats.Value.Level == 1, "last-good value after failed reload");

    files.Set("live.stats.json", Encoding.UTF8.GetBytes("{\"name\":\"Mina\",\"level\":2}"));
    await PumpUntil(resources, () => json.LastReloadError is null && stats.Value.Level == 2);
    Ensure(stats.Version >= 1, "successful hot reload recovery");
}
// docs:end hot-reload-recovery

// docs:begin browser-http-assets
static async Task BrowserHttpAssets()
{
    using var http = new HttpClient(new StaticHttpHandler("remote resource"));
    using var resources = new ResourceSystem(
        sources: [new HttpSource(http)],
        steps: [new Utf8TextStep()]);
    using ResourceHandle<TextAsset> remote = resources.Load<TextAsset>("https://assets.example/motd.txt");
    await remote.Ready;
    Ensure(remote.Value.Text == "REMOTE RESOURCE", "HTTP source composition used by browser hosts");
}
// docs:end browser-http-assets

static async Task PumpUntil(ResourceSystem resources, Func<bool> condition)
{
    var timeout = Stopwatch.StartNew();
    while (!condition() && timeout.Elapsed < TimeSpan.FromSeconds(3))
    {
        resources.Pump();
        await Task.Delay(5);
    }
    resources.Pump();
    Ensure(condition(), "reload completed before timeout");
}

static void Ensure(bool condition, string scenario)
{
    if (!condition) throw new InvalidOperationException($"Resource sample failed: {scenario}");
}

sealed record TextAsset(string Text);
sealed record MessageAsset(string Text);
sealed record PlayerStats(string Name, int Level);
sealed record WordCount(int Count);
sealed record RuntimeSeed(int Level);
sealed record RuntimeLabel(string Text);

sealed class Utf8TextStep : IResourceStep<byte[], TextAsset>
{
    public Executor Executor => Executor.Cpu;
    public IEnumerable<string> Extensions => [".txt"];
    public Task<TextAsset> RunAsync(byte[] input, ResourceUri uri, LoadContext context)
        => Task.FromResult(new TextAsset(Encoding.UTF8.GetString(input).ToUpperInvariant()));
}

sealed class PackageSource(IReadOnlyDictionary<string, byte[]> entries) : IResourceSource
{
    public IEnumerable<string> Schemes => ["package"];
    public Task<byte[]> ReadAsync(ResourceUri uri, LoadContext context)
        => entries.TryGetValue(uri.Path, out byte[]? data)
            ? Task.FromResult((byte[])data.Clone())
            : Task.FromException<byte[]>(new FileNotFoundException(uri.Path));
}

sealed class JsonDocumentStep : IResourceStep<byte[], JsonDocument>
{
    public Executor Executor => Executor.Cpu;
    public IEnumerable<string> Extensions => [".json"];
    public Task<JsonDocument> RunAsync(byte[] input, ResourceUri uri, LoadContext context)
        => Task.FromResult(JsonDocument.Parse(input));
}

sealed class PlayerStatsStep : IResourceStep<JsonDocument, PlayerStats>
{
    public Executor Executor => Executor.Cpu;
    public Task<PlayerStats> RunAsync(JsonDocument input, ResourceUri uri, LoadContext context)
    {
        JsonElement root = input.RootElement;
        return Task.FromResult(new PlayerStats(root.GetProperty("name").GetString()!, root.GetProperty("level").GetInt32()));
    }
}

sealed class PlainMessageStep : IResourceStep<byte[], MessageAsset>
{
    public Executor Executor => Executor.Cpu;
    public IEnumerable<string> Extensions => [".txt"];
    public Task<MessageAsset> RunAsync(byte[] input, ResourceUri uri, LoadContext context)
        => Task.FromResult(new MessageAsset(Encoding.UTF8.GetString(input)));
}

sealed class CaptionMessageStep : IResourceStep<byte[], MessageAsset>
{
    public Executor Executor => Executor.Cpu;
    public IEnumerable<string> Extensions => [".caption"];
    public Task<MessageAsset> RunAsync(byte[] input, ResourceUri uri, LoadContext context)
        => Task.FromResult(new MessageAsset($"[{Encoding.UTF8.GetString(input)}]"));
}

sealed class CountingTextStep : IResourceStep<byte[], TextAsset>
{
    public static int Runs;
    public Executor Executor => Executor.Cpu;
    public IEnumerable<string> Extensions => [".txt"];
    public Task<TextAsset> RunAsync(byte[] input, ResourceUri uri, LoadContext context)
    {
        Interlocked.Increment(ref Runs);
        return Task.FromResult(new TextAsset(Encoding.UTF8.GetString(input)));
    }
}

sealed class WordCountStep : IResourceStep<TextAsset, WordCount>
{
    public Executor Executor => Executor.Cpu;
    public Task<WordCount> RunAsync(TextAsset input, ResourceUri uri, LoadContext context)
        => Task.FromResult(new WordCount(input.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length));
}

sealed class RuntimeLabelStep : IResourceStep<RuntimeSeed, RuntimeLabel>
{
    public Executor Executor => Executor.Cpu;
    public Task<RuntimeLabel> RunAsync(RuntimeSeed input, ResourceUri uri, LoadContext context)
        => Task.FromResult(new RuntimeLabel($"Level {input.Level}"));
}

sealed class StaticHttpHandler(string content) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(Encoding.UTF8.GetBytes(content)),
        });
}
