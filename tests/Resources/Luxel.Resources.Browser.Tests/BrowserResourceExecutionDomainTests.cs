using Xunit;
using System.Collections.Concurrent;
using Luxel.Resources;
using Luxel.Resources.Browser;

namespace Luxel.Resources.Browser.Tests;

public sealed class BrowserResourceExecutionDomainTests
{
    [Fact]
    public async Task DispatchIsFifoSerializedAndReportsMetrics()
    {
        await using var domain = new BrowserResourceExecutionDomain(new("owner"));
        await domain.StartAsync();
        var order = new ConcurrentQueue<int>();
        int active = 0, maximum = 0;
        ValueTask<object>[] work = Enumerable.Range(0, 4).Select(index => domain.DispatchAsync(async _ =>
        {
            int current = Interlocked.Increment(ref active);
            maximum = Math.Max(maximum, current);
            order.Enqueue(index);
            await Task.Yield();
            Interlocked.Decrement(ref active);
            return index;
        })).ToArray();

        await Task.WhenAll(work.Select(item => item.AsTask()));

        Assert.Equal([0, 1, 2, 3], order);
        Assert.Equal(1, maximum);
        ResourceExecutionDomainSnapshot snapshot = domain.CaptureSnapshot();
        Assert.Equal(4, snapshot.CompletedCount);
        Assert.Equal(0, snapshot.QueueDepth);
        Assert.Equal(0, snapshot.ActiveCount);
        Assert.Equal(1, domain.Capabilities.MaxConcurrency);
        Assert.Equal(ResourceProgressModel.Cooperative, domain.Capabilities.ProgressModel);
    }

    [Fact]
    public async Task QueuedCancellationDoesNotRunWorkOrBlockFollowingItem()
    {
        await using var domain = new BrowserResourceExecutionDomain(new("owner"));
        await domain.StartAsync();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ValueTask<object> first = domain.DispatchAsync(async _ => { await release.Task; return 1; });
        using var cancellation = new CancellationTokenSource();
        ValueTask<object> canceled = domain.DispatchAsync(_ => ValueTask.FromResult<object>(2), cancellation.Token);
        ValueTask<object> last = domain.DispatchAsync(_ => ValueTask.FromResult<object>(3));
        cancellation.Cancel();
        release.SetResult();

        Assert.Equal(1, await first);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceled.AsTask());
        Assert.Equal(3, await last);
        Assert.Equal(3, domain.CaptureSnapshot().CompletedCount);
    }

    [Fact]
    public async Task DispatchUsesAnIndependentAsynchronousTurn()
    {
        await using var domain = new BrowserResourceExecutionDomain(new("owner"));
        await domain.StartAsync();
        bool ran = false;

        ValueTask<object> dispatched = domain.DispatchAsync(_ =>
        {
            ran = true;
            return ValueTask.FromResult<object>(1);
        });

        Assert.False(ran, "Browser work must never run inline with DispatchAsync.");
        Assert.Equal(1, await dispatched);
        Assert.True(ran);
    }

    [System.Runtime.Versioning.SupportedOSPlatform("browser")]
    [Fact]
    public void BrowserCoreBuildsWithItsIndependentScheduler()
    {
        var builder = new ResourceSystemBuilder();
        ResourceSystemDefaultHandles handles = builder.AddBrowserCore();

        Assert.Equal("resource.io", handles.IoDomain.Id.Value);
        Assert.Equal("resource.cpu", handles.CpuDomain.Id.Value);
        using ResourceSystem resources = builder.Build();
    }

    [Fact]
    public void BrowserCompositionRootsUseBuilderDomainsWithoutLegacyMutation()
    {
        string root = FindRepositoryRoot();
        string gallery = File.ReadAllText(Path.Combine(root, "src", "Gallery", "Luxel.Gallery.Browser", "BrowserGalleryApplication.cs"));
        string framework = File.ReadAllText(Path.Combine(root, "src", "Framework", "Luxel.Framework.Game.Browser", "BrowserGamePlatform.cs"));
        string sample = File.ReadAllText(Path.Combine(root, "samples", "LuxelPlaygroundBrowser", "Program.cs"));
        string coreProject = File.ReadAllText(Path.Combine(root, "src", "Resources", "Luxel.Resources", "Luxel.Resources.csproj"));

        Assert.Contains("AddBrowserCore", gallery, StringComparison.Ordinal);
        Assert.Contains("UseBrowserCooperative", gallery, StringComparison.Ordinal);
        Assert.Contains("UseResourceCore(resourceBuilder => resourceBuilder.AddBrowserCore())", framework, StringComparison.Ordinal);
        Assert.Contains("AddBrowserCore", sample, StringComparison.Ordinal);
        Assert.DoesNotContain("InstallAssetGpu", gallery + sample, StringComparison.Ordinal);
        Assert.DoesNotContain("new ResourceSystem(", gallery + sample, StringComparison.Ordinal);
        Assert.DoesNotContain("Luxel.Resources.Browser", coreProject, StringComparison.Ordinal);
        string browserResourceSources = string.Join('\n', Directory.GetFiles(
            Path.Combine(root, "src", "Resources", "Luxel.Resources.Browser"), "*.cs")
            .Select(File.ReadAllText));
        Assert.DoesNotContain("SynchronizationContext", browserResourceSources, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Luxel.slnx"))) return current.FullName;
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate Luxel.slnx.");
    }
}
