using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Luxel.Resources;

await ReadyBuilder();
await CustomExecutionDomain();
await SerializedDomain();
await TypedManagerBinding();
await SharedRequestIdentity();
await CustomSourceAndStep();
await DependencyPublication();
await ScopedRetirement();
await ReloadKeepsLastGood();
await DomainAndManagerMetrics();
Console.WriteLine("resources: status=Ready, architecture=builder-domain-manager, scenarios=10");

// docs:begin ready-builder
static async Task ReadyBuilder()
{
    MemoryFileSystem files = Files(("hello.txt", "hello resources"));
    using ResourceSystem resources = Build((builder, h) =>
    {
        builder.Sources.Add(new FileSource(files)).RunOn(h.IoDomain).ManagedBy(h.IoManager).Register();
        builder.Steps.Add<byte[], TextAsset>(new TextStep()).RunOn(h.CpuDomain).ManagedBy(h.CpuManager).Register();
    });
    using ResourceHandle<TextAsset> value = resources.Load<TextAsset>("hello.txt");
    await value.Ready;
    Ensure(value.Value.Text == "HELLO RESOURCES", "ready builder");
}
// docs:end ready-builder

// docs:begin custom-execution-domain
static async Task CustomExecutionDomain()
{
    ResourceExecutionDomainHandle decode = default;
    using ResourceSystem resources = Build((builder, h) =>
    {
        decode = builder.Domains.Add("sample.decode").UseThreadPool(2).Register();
        builder.Steps.Add<Seed, Label>(new LabelStep()).RunOn(decode).ManagedBy(h.CpuManager).Register();
    });
    using ResourceScope scope = resources.CreateScope("sample/domain");
    ResourceHandle<Label> value = scope.Create<Seed, Label>("label", new(2));
    await value.Ready;
    Ensure(resources.CaptureDomainSnapshots().Any(x => x.Id == decode.Id), "custom domain");
}
// docs:end custom-execution-domain

// docs:begin serialized-domain
static async Task SerializedDomain()
{
    var step = new SerialProbeStep();
    using ResourceSystem resources = Build((builder, h) =>
    {
        ResourceExecutionDomainHandle serial = builder.Domains.Add("sample.compiler").UseSerial().Register();
        builder.Steps.Add<Seed, Label>(step).RunOn(serial).ManagedBy(h.CpuManager).Register();
    });
    using ResourceScope scope = resources.CreateScope("sample/compiler");
    ResourceHandle<Label>[] values = Enumerable.Range(1, 3).Select(i => scope.Create<Seed, Label>($"job-{i}", new(i))).ToArray();
    await Task.WhenAll(values.Select(x => x.Ready));
    Ensure(step.MaxActive == 1 && step.Order.SequenceEqual([1, 2, 3]), "serialized domain");
}
// docs:end serialized-domain

// docs:begin typed-manager-binding
static async Task TypedManagerBinding()
{
    TrackingManager? manager = null;
    using ResourceSystem resources = Build((builder, h) =>
    {
        ResourceManagerHandle labels = builder.Managers.Add("sample.labels").RunOn(h.CpuDomain)
            .Use(ctx => manager = new TrackingManager(ctx.Id)).Register();
        builder.Managers.Manage<Label>().With(labels).Register();
        builder.Steps.Add<Seed, Label>(new LabelStep()).RunOn(h.CpuDomain).Register();
    });
    using ResourceScope scope = resources.CreateScope("sample/manager");
    ResourceHandle<Label> value = scope.Create<Seed, Label>("managed", new(3));
    await value.Ready;
    Ensure(manager!.Adopted == 1, "typed manager binding");
}
// docs:end typed-manager-binding

// docs:begin shared-request-identity
static async Task SharedRequestIdentity()
{
    var step = new CountingTextStep();
    using ResourceSystem resources = Build((builder, h) =>
    {
        builder.Sources.Add(new FileSource(Files(("shared.txt", "one two")))).RunOn(h.IoDomain).ManagedBy(h.IoManager).Register();
        builder.Steps.Add<byte[], TextAsset>(step).RunOn(h.CpuDomain).ManagedBy(h.CpuManager).Register();
        builder.Steps.Add<TextAsset, WordCount>(new WordCountStep()).RunOn(h.CpuDomain).ManagedBy(h.CpuManager).Register();
    });
    using ResourceHandle<TextAsset> text = resources.Load<TextAsset>("shared.txt");
    using ResourceHandle<WordCount> count = resources.Load<WordCount>("shared.txt");
    await Task.WhenAll(text.Ready, count.Ready);
    Ensure(step.Runs == 1 && count.Value.Count == 2, "shared request identity");
}
// docs:end shared-request-identity

// docs:begin custom-source-and-step
static async Task CustomSourceAndStep()
{
    using ResourceSystem resources = Build((builder, h) =>
    {
        builder.Sources.Add(new PackageSource(new Dictionary<string, byte[]> { ["ui/title.txt"] = Encoding.UTF8.GetBytes("package") }))
            .RunOn(h.IoDomain).ManagedBy(h.IoManager).Register();
        builder.Steps.Add<byte[], TextAsset>(new TextStep()).RunOn(h.CpuDomain).ManagedBy(h.CpuManager).ForExtensions(".txt").Register();
    });
    using ResourceHandle<TextAsset> value = resources.Load<TextAsset>("package://ui/title.txt");
    await value.Ready;
    Ensure(value.Value.Text == "PACKAGE", "custom source and step");
}
// docs:end custom-source-and-step

static async Task DependencyPublication()
{
    using ResourceSystem resources = Build((builder, h) =>
    {
        builder.Sources.Add(new FileSource(Files(("words.txt", "one two three")))).RunOn(h.IoDomain).ManagedBy(h.IoManager).Register();
        builder.Steps.Add<byte[], TextAsset>(new TextStep()).RunOn(h.CpuDomain).ManagedBy(h.CpuManager).Register();
        builder.Steps.Add<TextAsset, WordCount>(new WordCountStep()).RunOn(h.CpuDomain).ManagedBy(h.CpuManager).Register();
    });
    using ResourceHandle<WordCount> count = resources.Load<WordCount>("words.txt");
    await count.Ready;
    resources.Pump();
    Ensure(count.Value.Count == 3, "dependency publication");
}

static async Task ScopedRetirement()
{
    TrackingManager? manager = null;
    using ResourceSystem resources = Build((builder, h) =>
    {
        ResourceManagerHandle tracked = builder.Managers.Add("sample.retirement").RunOn(h.CpuDomain)
            .Use(ctx => manager = new TrackingManager(ctx.Id)).Register();
        builder.Managers.Manage<Label>().With(tracked).Register();
        builder.Steps.Add<Seed, Label>(new LabelStep()).RunOn(h.CpuDomain).Register();
    });
    using (ResourceScope scope = resources.CreateScope("sample/retirement"))
    {
        ResourceHandle<Label> value = scope.Create<Seed, Label>("owned", new(4));
        await value.Ready;
    }
    resources.Pump();
    await WaitUntil(() => manager!.Retired > 0);
    Ensure(manager!.Retired == 1, "scoped retirement");
}

static async Task ReloadKeepsLastGood()
{
    MemoryFileSystem files = Files(("live.json", "{\"level\":1}"));
    using ResourceSystem resources = Build((builder, h) =>
    {
        builder.Sources.Add(new FileSource(files)).RunOn(h.IoDomain).ManagedBy(h.IoManager).Register();
        builder.Steps.Add<byte[], JsonDocument>(new JsonStep()).RunOn(h.CpuDomain).ManagedBy(h.CpuManager).Register();
        builder.Steps.Add<JsonDocument, Stats>(new StatsStep()).RunOn(h.CpuDomain).ManagedBy(h.CpuManager).Register();
    });
    resources.Watch();
    using ResourceHandle<JsonDocument> json = resources.Load<JsonDocument>("live.json");
    using ResourceHandle<Stats> stats = resources.Load<Stats>("live.json");
    await Task.WhenAll(json.Ready, stats.Ready);
    files.Set("live.json", Encoding.UTF8.GetBytes("bad"));
    await PumpUntil(resources, () => json.LastReloadError is not null);
    Ensure(stats.Value.Level == 1, "last-good value");
    files.Set("live.json", Encoding.UTF8.GetBytes("{\"level\":2}"));
    await PumpUntil(resources, () => json.LastReloadError is null && stats.Value.Level == 2);
}

static async Task DomainAndManagerMetrics()
{
    using ResourceSystem resources = Build((builder, h) =>
    {
        builder.Sources.Add(new FileSource(Files(("metrics.txt", "metrics")))).RunOn(h.IoDomain).ManagedBy(h.IoManager).Register();
        builder.Steps.Add<byte[], TextAsset>(new TextStep()).RunOn(h.CpuDomain).ManagedBy(h.CpuManager).Register();
    });
    using ResourceHandle<TextAsset> value = resources.Load<TextAsset>("metrics.txt");
    await value.Ready;
    Ensure(resources.CaptureDomainSnapshots().Any(x => x.CompletedCount > 0), "domain metrics");
    Ensure(resources.CaptureManagerSnapshots().Any(x => x.AdoptedCount > 0), "manager metrics");
}

static ResourceSystem Build(Action<ResourceSystemBuilder, ResourceSystemDefaultHandles> configure)
{
    var builder = new ResourceSystemBuilder();
    ResourceSystemDefaultHandles handles = ResourceSystemDefaults.AddCore(builder);
    configure(builder, handles);
    return builder.Build();
}

static MemoryFileSystem Files(params (string Path, string Text)[] entries)
{
    var files = new MemoryFileSystem();
    foreach ((string path, string text) in entries) files.Set(path, Encoding.UTF8.GetBytes(text));
    return files;
}

static async Task PumpUntil(ResourceSystem resources, Func<bool> condition)
{
    var timeout = Stopwatch.StartNew();
    while (!condition() && timeout.Elapsed < TimeSpan.FromSeconds(3)) { resources.Pump(); await Task.Delay(5); }
    resources.Pump();
    Ensure(condition(), "pump timeout");
}

static async Task WaitUntil(Func<bool> condition)
{
    for (int i = 0; i < 200 && !condition(); i++) await Task.Delay(5);
    Ensure(condition(), "retirement timeout");
}

static void Ensure(bool condition, string scenario)
{
    if (!condition) throw new InvalidOperationException($"Resource sample failed: {scenario}");
}

sealed record TextAsset(string Text);
sealed record WordCount(int Count);
sealed record Seed(int Level);
sealed record Label(string Text);
sealed record Stats(int Level);

sealed class TextStep : IResourceStep<byte[], TextAsset>
{
    public IEnumerable<string> Extensions => [".txt"];
    public Task<TextAsset> RunAsync(byte[] input, ResourceUri uri, LoadContext ctx) => Task.FromResult(new TextAsset(Encoding.UTF8.GetString(input).ToUpperInvariant()));
}

sealed class CountingTextStep : IResourceStep<byte[], TextAsset>
{
    public int Runs;
    public IEnumerable<string> Extensions => [".txt"];
    public Task<TextAsset> RunAsync(byte[] input, ResourceUri uri, LoadContext ctx) { Interlocked.Increment(ref Runs); return Task.FromResult(new TextAsset(Encoding.UTF8.GetString(input))); }
}

sealed class WordCountStep : IResourceStep<TextAsset, WordCount>
{
    public Task<WordCount> RunAsync(TextAsset input, ResourceUri uri, LoadContext ctx) => Task.FromResult(new WordCount(input.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length));
}

sealed class LabelStep : IResourceStep<Seed, Label>
{
    public Task<Label> RunAsync(Seed input, ResourceUri uri, LoadContext ctx) => Task.FromResult(new Label($"Level {input.Level}"));
}

sealed class SerialProbeStep : IResourceStep<Seed, Label>
{
    private int _active;
    public List<int> Order { get; } = [];
    public int MaxActive { get; private set; }
    public async Task<Label> RunAsync(Seed input, ResourceUri uri, LoadContext ctx)
    {
        int active = Interlocked.Increment(ref _active);
        MaxActive = Math.Max(MaxActive, active);
        Order.Add(input.Level);
        await Task.Delay(5, ctx.Token);
        Interlocked.Decrement(ref _active);
        return new($"Level {input.Level}");
    }
}

sealed class PackageSource(IReadOnlyDictionary<string, byte[]> entries) : IResourceSource
{
    public IEnumerable<string> Schemes => ["package"];
    public Task<byte[]> ReadAsync(ResourceUri uri, LoadContext ctx) => entries.TryGetValue(uri.Path, out byte[]? data)
        ? Task.FromResult((byte[])data.Clone()) : Task.FromException<byte[]>(new FileNotFoundException(uri.Path));
}

sealed class JsonStep : IResourceStep<byte[], JsonDocument>
{
    public IEnumerable<string> Extensions => [".json"];
    public Task<JsonDocument> RunAsync(byte[] input, ResourceUri uri, LoadContext ctx) => Task.FromResult(JsonDocument.Parse(input));
}

sealed class StatsStep : IResourceStep<JsonDocument, Stats>
{
    public Task<Stats> RunAsync(JsonDocument input, ResourceUri uri, LoadContext ctx) => Task.FromResult(new Stats(input.RootElement.GetProperty("level").GetInt32()));
}

sealed class TrackingManager(ResourceManagerId id) : CpuResourceManager(id)
{
    public long Adopted => CaptureSnapshot().AdoptedCount;
    public long Retired => CaptureSnapshot().RetiredCount;
}
