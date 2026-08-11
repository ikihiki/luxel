using System.Text;
using Luxel.Resources;

namespace Luxel.Tests;

public sealed class ResourceSystemBuilderTests
{
    private sealed class ThreadNameStep : IResourceStep<byte[], string>
    {
        public IEnumerable<string> Extensions => [".thread"];
        public Task<string> RunAsync(byte[] input, ResourceUri uri, LoadContext context)
            => Task.FromResult($"{Encoding.UTF8.GetString(input)}@{Thread.CurrentThread.Name}");
    }

    [Fact]
    public async Task ArbitraryDedicatedDomainRunsRegisteredStep()
    {
        var files = new MemoryFileSystem();
        files.Set("work.thread", Encoding.UTF8.GetBytes("ready"));
        var builder = new ResourceSystemBuilder();
        ResourceSystemDefaultHandles defaults = ResourceSystemDefaults.AddCore(builder);
        ResourceExecutionDomainHandle worker = builder.Domains.Add("custom.worker")
            .UseDedicatedThread("resource-test-worker")
            .Register();
        builder.Sources.Add(new FileSource(files)).RunOn(defaults.IoDomain).ManagedBy(defaults.IoManager).Register();
        builder.Steps.Add<byte[], string>(new ThreadNameStep()).RunOn(worker).ManagedBy(defaults.CpuManager).Register();
        await using ResourceSystem resources = await builder.BuildAsync();

        using ResourceHandle<string> handle = resources.Load<string>("work.thread");
        await handle.Ready;

        Assert.Equal("ready@resource-test-worker", handle.Value);
        Assert.Contains(resources.CaptureDomainSnapshots(), snapshot => snapshot.Id.Value == "custom.worker");
    }

    [Fact]
    public async Task BuildSealsBuilderAndRejectsFurtherRegistration()
    {
        var builder = ResourceSystemDefaults.CreateBuilder();
        await using ResourceSystem resources = await builder.BuildAsync();

        Assert.Throws<InvalidOperationException>(() => builder.Domains.Add("late"));
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await builder.BuildAsync());
    }

    [Fact]
    public void DuplicateDomainIdsAreReportedAtBuildTime()
    {
        var builder = new ResourceSystemBuilder();
        builder.Domains.Add("duplicate").UseSerial().Register();
        builder.Domains.Add("duplicate").UseSerial().Register();

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => builder.Build());

        Assert.Contains("Duplicate execution domain id 'duplicate'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void PublicResourceSystemHasNoMutableRegistrationOrLegacyConstructor()
    {
        Type type = typeof(ResourceSystem);
        Assert.Empty(type.GetConstructors());
        Assert.Null(type.GetMethod("AddSource"));
        Assert.Null(type.GetMethod("AddStep"));
        Assert.Null(type.GetMethod("SetDeferredDisposeIdleHook"));
        Assert.Null(type.GetMethod("RegisterPumpFlushLease"));
        Assert.Null(typeof(IResourceStep<,>).GetProperty("Executor"));
        Assert.Null(typeof(LoadContext).GetProperty("Io"));
        Assert.Null(typeof(LoadContext).GetProperty("Cpu"));
        Assert.Null(typeof(LoadContext).GetProperty("External"));
    }
}
