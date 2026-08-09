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
        using var context = new PumpSynchronizationContext();
        await using var domain = new BrowserResourceExecutionDomain(new("owner"), context);
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
        using var context = new PumpSynchronizationContext();
        await using var domain = new BrowserResourceExecutionDomain(new("owner"), context);
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
    public async Task EachCompletionPostsTheNextItemForFairness()
    {
        using var context = new PumpSynchronizationContext();
        await using var domain = new BrowserResourceExecutionDomain(new("owner"), context);
        await domain.StartAsync();
        ValueTask<object> first = domain.DispatchAsync(_ => ValueTask.FromResult<object>(1));
        ValueTask<object> second = domain.DispatchAsync(_ => ValueTask.FromResult<object>(2));

        Assert.Equal(1, await first);
        Assert.Equal(2, await second);
        Assert.True(context.PostCount >= 2, "Each FIFO item should be scheduled through a separate owner-context post.");
    }

    [System.Runtime.Versioning.SupportedOSPlatform("browser")]
    [Fact]
    public void BrowserCoreCanBeConfiguredWithoutAnAmbientSynchronizationContext()
    {
        SynchronizationContext? previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(null);
        try
        {
            var builder = new ResourceSystemBuilder();
            ResourceSystemDefaultHandles handles = builder.AddBrowserCore();

            Assert.Equal("resource.io", handles.IoDomain.Id.Value);
            Assert.Equal("resource.cpu", handles.CpuDomain.Id.Value);
            using ResourceSystem resources = builder.Build();
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    [Fact]
    public void BrowserCompositionRootsUseBuilderDomainsWithoutLegacyMutation()
    {
        string root = FindRepositoryRoot();
        string gallery = File.ReadAllText(Path.Combine(root, "src", "Gallery", "Luxel.Gallery.Browser", "BrowserGalleryApplication.cs"));
        string framework = File.ReadAllText(Path.Combine(root, "src", "Framework", "Luxel.Framework.Game.Browser", "BrowserGamePlatform.cs"));
        string sample = File.ReadAllText(Path.Combine(root, "samples", "LuxelPlaygroundBrowser", "Program.cs"));
        string coreProject = File.ReadAllText(Path.Combine(root, "src", "Resource", "Luxel.Resources", "Luxel.Resources.csproj"));

        Assert.Contains("AddBrowserCore", gallery, StringComparison.Ordinal);
        Assert.Contains("UseBrowserOwnerContext", gallery, StringComparison.Ordinal);
        Assert.Contains("UseResourceCore(resourceBuilder => resourceBuilder.AddBrowserCore())", framework, StringComparison.Ordinal);
        Assert.Contains("AddBrowserCore", sample, StringComparison.Ordinal);
        Assert.DoesNotContain("InstallAssetGpu", gallery + sample, StringComparison.Ordinal);
        Assert.DoesNotContain("new ResourceSystem(", gallery + sample, StringComparison.Ordinal);
        Assert.DoesNotContain("Luxel.Resources.Browser", coreProject, StringComparison.Ordinal);
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

    private sealed class PumpSynchronizationContext : SynchronizationContext, IDisposable
    {
        private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> _queue = new();
        private readonly Thread _thread;
        public PumpSynchronizationContext()
        {
            _thread = new Thread(Run) { IsBackground = true, Name = "browser-owner-test" };
            _thread.Start();
        }
        private int _postCount;
        public int PostCount => Volatile.Read(ref _postCount);
        public override void Post(SendOrPostCallback d, object? state)
        {
            Interlocked.Increment(ref _postCount);
            _queue.Add((d, state));
        }
        private void Run()
        {
            SetSynchronizationContext(this);
            foreach ((SendOrPostCallback callback, object? state) in _queue.GetConsumingEnumerable()) callback(state);
        }
        public void Dispose() { _queue.CompleteAdding(); _thread.Join(); _queue.Dispose(); }
    }
}
