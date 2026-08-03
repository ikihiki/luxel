using System.Diagnostics;
using System.Text;
using Luxel.Controls;
using Luxel.Resources;

namespace Luxel.Tests;

/// <summary>
/// Luxel.Resources の GPU 不要テスト。MemoryFileSystem + fake step クラス + fake IServiceProvider で
/// 任意長オートコンポーズ・中間停止・(型,uri)共有・DI・publish(Pump)・リロード/伝播・device-lost・バンドルを検証。
/// </summary>
public class ResourceSystemTests
{
    // ---- fake 中間型 ----
    public sealed class Doc(string text) { public string Text => text; }
    public sealed class Upper(string text) { public string Text => text; }
    public sealed class Final(string text) { public string Text => text; }
    public sealed class Bundle(Final a, Final b) { public Final A => a; public Final B => b; }

    public interface ITag { string Tag { get; } }
    private sealed class EmptyTag : ITag { public string Tag => ""; }

    // ---- 実行回数カウンタ (共有/再ロード検証) ----
    private static int _docRuns, _upperRuns, _finalRuns;
    private static void Reset() { _docRuns = _upperRuns = _finalRuns = 0; }

    // ---- fake ステップ (byte[]→Doc→Upper→Final、Bundle は byte[]→Bundle) ----
    private sealed class DocStep(ITag tag) : IResourceStep<byte[], Doc>   // DI: ITag 注入
    {
        public Executor Executor => Executor.Cpu;
        public IEnumerable<string> Extensions => [".doc", ".bundle"];
        public Task<Doc> RunAsync(byte[] b, ResourceUri u, LoadContext c)
        { Interlocked.Increment(ref _docRuns); return Task.FromResult(new Doc(Encoding.UTF8.GetString(b) + tag.Tag)); }
    }
    private sealed class UpperStep : IResourceStep<Doc, Upper>
    {
        public Executor Executor => Executor.Cpu;
        public Task<Upper> RunAsync(Doc d, ResourceUri u, LoadContext c)
        { Interlocked.Increment(ref _upperRuns); return Task.FromResult(new Upper(d.Text.ToUpperInvariant())); }
    }
    private sealed class FinalStep : IResourceStep<Upper, Final>
    {
        public Executor Executor => Executor.Gpu;   // Gpu レーン (テストではデバイス不要)
        public Task<Final> RunAsync(Upper up, ResourceUri u, LoadContext c)
        { Interlocked.Increment(ref _finalRuns); return Task.FromResult(new Final(up.Text + "!")); }
    }
    private sealed class BundleStep : IResourceStep<byte[], Bundle>
    {
        public Executor Executor => Executor.Cpu;
        public IEnumerable<string> Extensions => [".bundle"];
        public async Task<Bundle> RunAsync(byte[] b, ResourceUri u, LoadContext c)
        {
            string[] parts = Encoding.UTF8.GetString(b).Split(';');
            var a = c.Load<Final>(parts[0]);
            var second = c.Load<Final>(parts[1]);
            await Task.WhenAll(a.Ready, second.Ready);
            return new Bundle(a.Value, second.Value);
        }
    }

    private static ResourceSystem NewSystem(out MemoryFileSystem vfs, ITag? tag = null)
    {
        Reset();
        vfs = new MemoryFileSystem();
        // Source/Step 全てをコンストラクタで注入 (DI コンテナは無し)
        return new ResourceSystem(
            sources: new IResourceSource[] { new FileSource(vfs) },
            steps: new IResourceStep[]
            {
                new DocStep(tag ?? new EmptyTag()),
                new UpperStep(),
                new FinalStep(),
                new BundleStep(),
            });
    }

    private static async Task PumpUntil(ResourceSystem sys, Func<bool> cond, int ms = 3000)
    {
        var sw = Stopwatch.StartNew();
        while (!cond() && sw.ElapsedMilliseconds < ms) { sys.Pump(); await Task.Delay(5); }
        sys.Pump();
    }

    [Fact]
    public async Task AutoCompose_BuildsArbitraryLengthChain()
    {
        var sys = NewSystem(out var vfs);
        vfs.Set("a.doc", Encoding.UTF8.GetBytes("hello"));
        var h = sys.Load<Final>("a.doc");
        await h.Ready;
        Assert.Equal("HELLO!", h.Value.Text);          // byte[]→Doc→Upper→Final の3段が自動合成
        Assert.Equal(1, _docRuns); Assert.Equal(1, _upperRuns); Assert.Equal(1, _finalRuns);
    }

    [Fact]
    public async Task IntermediateType_StopsChain()
    {
        var sys = NewSystem(out var vfs);
        vfs.Set("a.doc", Encoding.UTF8.GetBytes("hello"));
        var h = sys.Load<Doc>("a.doc");                 // 途中型を要求 → Upper/Final は走らない
        await h.Ready;
        Assert.Equal("hello", h.Value.Text);
        Assert.Equal(1, _docRuns); Assert.Equal(0, _upperRuns); Assert.Equal(0, _finalRuns);
    }

    [Fact]
    public async Task IntermediateNode_IsShared()
    {
        var sys = NewSystem(out var vfs);
        vfs.Set("a.doc", Encoding.UTF8.GetBytes("hi"));
        var f = sys.Load<Final>("a.doc"); await f.Ready;
        var d = sys.Load<Doc>("a.doc"); await d.Ready;  // 同一 (Doc,uri) ノードを共有
        Assert.Equal("hi", d.Value.Text);
        Assert.Equal(1, _docRuns);                       // デコードは 1 回だけ
    }

    [Fact]
    public async Task DI_CtorInjection_Resolves()
    {
        // ユーザーが ITag 実装を Step ctor に渡して注入 (DI コンテナ非経由)
        Reset();
        var vfs = new MemoryFileSystem();
        var sys = new ResourceSystem(
            sources: new IResourceSource[] { new FileSource(vfs) },
            steps: new IResourceStep[] { new DocStep(new TagImpl("#")) });
        vfs.Set("a.doc", Encoding.UTF8.GetBytes("x"));
        var h = sys.Load<Doc>("a.doc"); await h.Ready;
        Assert.Equal("x#", h.Value.Text);
    }
    private sealed class TagImpl(string s) : ITag { public string Tag => s; }

    [Fact]
    public async Task Reload_OnFileChange_PropagatesAndPublishesOnPump()
    {
        var sys = NewSystem(out var vfs);
        sys.Watch();
        vfs.Set("a.doc", Encoding.UTF8.GetBytes("one"));
        var h = sys.Load<Final>("a.doc");
        await h.Ready;
        Assert.Equal("ONE!", h.Value.Text);
        int reloadFired = 0; h.Reloaded += () => reloadFired++;

        vfs.Set("a.doc", Encoding.UTF8.GetBytes("two"));   // ファイル変更 → 自動リロード
        Assert.Equal(0, reloadFired);                       // Pump 前は通知されない

        await PumpUntil(sys, () => h.Value.Text == "TWO!" && h.Version >= 1);
        Assert.Equal("TWO!", h.Value.Text);                 // 伝播して再構成
        Assert.True(h.Version >= 1);
        Assert.True(reloadFired >= 1);                      // Reloaded は Pump で発火
    }

    [Fact]
    public async Task DeviceLost_ReloadsAll()
    {
        var sys = NewSystem(out var vfs);
        vfs.Set("a.doc", Encoding.UTF8.GetBytes("k"));
        var h = sys.Load<Final>("a.doc"); await h.Ready;
        int beforeFinal = _finalRuns;
        sys.NotifyDeviceLost();
        await PumpUntil(sys, () => _finalRuns > beforeFinal);
        Assert.True(_finalRuns > beforeFinal);              // GPU 段が再実行された
        Assert.Equal("K!", h.Value.Text);
    }

    [Fact]
    public async Task SupersededReload_CannotPublishAfterNewerGeneration()
    {
        var sys = new ResourceSystem();
        var loads = new List<TaskCompletionSource<Final>>();
        var tokens = new List<CancellationToken>();
        Loader<Final> loader = ctx =>
        {
            tokens.Add(ctx.Token);
            var completion = new TaskCompletionSource<Final>(TaskCreationOptions.RunContinuationsAsynchronously);
            loads.Add(completion);
            return completion.Task;
        };

        var h = sys.Load("controlled://shader", loader);
        loads[0].SetResult(new Final("initial"));
        await h.Ready;
        Assert.Equal("initial", h.Value.Text);

        sys.NotifyDeviceLost();
        sys.NotifyDeviceLost(); // duplicate queued reloads are coalesced
        sys.Pump();
        Assert.Equal(2, loads.Count);
        Task olderReload = h.Ready;

        sys.NotifyDeviceLost();
        sys.Pump();
        Assert.Equal(3, loads.Count);
        Assert.True(tokens[1].IsCancellationRequested);
        Task newerReload = h.Ready;

        loads[2].SetResult(new Final("newer"));
        await newerReload;
        sys.Pump();
        Assert.Equal("newer", h.Value.Text);
        Assert.Equal(1, h.Version);

        loads[1].SetResult(new Final("stale"));
        await olderReload;
        sys.Pump();
        Assert.Equal("newer", h.Value.Text);
        Assert.Equal(1, h.Version);
    }

    [Fact]
    public async Task FailedReload_RetainsLastGoodValue_AndLaterSuccessClearsError()
    {
        var sys = new ResourceSystem();
        var loads = new List<TaskCompletionSource<Final>>();
        Loader<Final> loader = _ =>
        {
            var completion = new TaskCompletionSource<Final>(TaskCreationOptions.RunContinuationsAsynchronously);
            loads.Add(completion);
            return completion.Task;
        };

        var h = sys.Load("controlled://shader", loader);
        loads[0].SetResult(new Final("good"));
        await h.Ready;
        Assert.True(h.HasValue);

        sys.NotifyDeviceLost();
        sys.Pump();
        Task failedReload = h.Ready;
        var failure = new InvalidOperationException("compile failed");
        loads[1].SetException(failure);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await failedReload);

        Assert.True(h.HasValue);
        Assert.True(h.IsReady);
        Assert.Same(failure, h.LastReloadError);
        Assert.Equal("good", h.Value.Text);

        sys.NotifyDeviceLost();
        sys.Pump();
        Task successfulReload = h.Ready;
        loads[2].SetResult(new Final("fixed"));
        await successfulReload;
        sys.Pump();

        Assert.Equal("fixed", h.Value.Text);
        Assert.True(h.IsReady);
        Assert.Null(h.LastReloadError);
    }

    [Fact]
    public async Task Refcount_EvictsAndReloads()
    {
        var sys = NewSystem(out var vfs);
        vfs.Set("a.doc", Encoding.UTF8.GetBytes("z"));
        var h1 = sys.Load<Final>("a.doc"); await h1.Ready;
        Assert.Equal(1, _finalRuns);
        h1.Dispose();                                       // 参照0 → evict (依存も連鎖)
        var h2 = sys.Load<Final>("a.doc"); await h2.Ready;
        Assert.Equal(2, _finalRuns);                        // 再生成された
    }

    private sealed class DisposableResource : IDisposable
    {
        public int DisposeCount { get; private set; }
        public void Dispose() => DisposeCount++;
    }

    [Fact]
    public async Task Scope_CreateQualifiesKeysAndDisposesEachLeaseOnce()
    {
        using var sys = new ResourceSystem();
        var firstScope = sys.CreateScope("panel/a");
        var first = new DisposableResource();
        ResourceHandle<DisposableResource> firstHandle = firstScope.Create(
            "buffer 1", _ => Task.FromResult(first));
        await firstHandle.Ready;

        Assert.Equal("scope://panel%2Fa/buffer%201", firstHandle.Uri.ToString());

        firstHandle.Dispose();
        firstScope.Dispose(); // tracked handle was already disposed
        sys.Pump();
        Assert.Equal(1, first.DisposeCount);

        using ResourceScope secondScope = sys.CreateScope("panel/a");
        var second = new DisposableResource();
        ResourceHandle<DisposableResource> secondHandle = secondScope.Create(
            "buffer 1", _ => Task.FromResult(second));
        await secondHandle.Ready;
        Assert.NotSame(first, secondHandle.Value); // prior node was evicted despite scope's second Dispose
    }

    [Fact]
    public async Task Scope_LocalKeysAreIsolatedByOwner_AndLoadTracksSharedLeases()
    {
        using var sys = NewSystem(out var vfs);
        vfs.Set("shared.doc", Encoding.UTF8.GetBytes("shared"));
        using ResourceHandle<Final> outside = sys.Load<Final>("shared.doc");
        await outside.Ready;

        var left = sys.CreateScope("left");
        var right = sys.CreateScope("right");
        ResourceHandle<Final> scopedLoad = left.Load<Final>("shared.doc");
        var leftValue = new DisposableResource();
        var rightValue = new DisposableResource();
        ResourceHandle<DisposableResource> leftHandle = left.Create("same", _ => Task.FromResult(leftValue));
        ResourceHandle<DisposableResource> rightHandle = right.Create("same", _ => Task.FromResult(rightValue));
        await Task.WhenAll(scopedLoad.Ready, leftHandle.Ready, rightHandle.Ready);

        Assert.Equal(outside.Value.Text, scopedLoad.Value.Text);
        Assert.NotEqual(leftHandle.Uri, rightHandle.Uri);

        left.Dispose();
        sys.Pump();
        Assert.Equal(1, leftValue.DisposeCount);
        Assert.Equal(0, rightValue.DisposeCount);
        Assert.True(outside.HasValue);

        right.Dispose();
        sys.Pump();
        Assert.Equal(1, rightValue.DisposeCount);
    }

    [Fact]
    public async Task LateOwnedLoaderCompletionAfterEvictionIsDisposed()
    {
        using var sys = new ResourceSystem();
        var completion = new TaskCompletionSource<DisposableResource>(TaskCreationOptions.RunContinuationsAsynchronously);
        ResourceHandle<DisposableResource> handle = sys.Load(
            "controlled://late", _ => completion.Task, ResourceOwnership.Owned);
        Task ready = handle.Ready;
        handle.Dispose();

        var value = new DisposableResource();
        completion.SetResult(value);
        await ready;

        Assert.Equal(1, value.DisposeCount);
    }

    [Fact]
    public async Task LateOwnedLoaderCompletionAfterSystemDisposeIsDisposed()
    {
        var sys = new ResourceSystem();
        var completion = new TaskCompletionSource<DisposableResource>(TaskCreationOptions.RunContinuationsAsynchronously);
        ResourceHandle<DisposableResource> handle = sys.Load(
            "controlled://late-dispose", _ => completion.Task, ResourceOwnership.Owned);
        Task ready = handle.Ready;
        sys.Dispose();

        var value = new DisposableResource();
        completion.SetResult(value);
        await ready;
        handle.Dispose();

        Assert.Equal(1, value.DisposeCount);
    }

    [Fact]
    public void BorrowedValuesAreNotDisposedOnEviction()
    {
        using var sys = new ResourceSystem();
        var value = new DisposableResource();
        ResourceHandle<DisposableResource> handle = sys.Publish(
            "published://borrowed", value, ResourceOwnership.Borrowed);

        handle.Dispose();
        sys.Pump();

        Assert.Equal(0, value.DisposeCount);
    }

    [Fact]
    public void PumpFlushRegistrationStopsAtNodeEviction()
    {
        using var sys = new ResourceSystem();
        ResourceHandle<object> handle = sys.Publish("published://flush", new object());
        int calls = 0;
        using IDisposable registration = sys.RegisterPumpFlushLease(handle, () => { calls++; return false; });

        handle.Dispose();
        sys.Pump();

        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task InvalidateAllReloadsExplicitLoaders()
    {
        using var sys = new ResourceSystem();
        int loads = 0;
        using ResourceHandle<Final> handle = sys.Load(
            "controlled://generic-invalidation",
            _ => Task.FromResult(new Final((++loads).ToString())));
        await handle.Ready;

        sys.InvalidateAll();
        sys.Pump();
        await handle.Ready;
        sys.Pump();

        Assert.Equal(2, loads);
        Assert.Equal("2", handle.Value.Text);
    }

    [Fact]
    public async Task Bundle_LoadsCrossUriChildren()
    {
        var sys = NewSystem(out var vfs);
        vfs.Set("a.doc", Encoding.UTF8.GetBytes("x"));
        vfs.Set("b.doc", Encoding.UTF8.GetBytes("y"));
        vfs.Set("pack.bundle", Encoding.UTF8.GetBytes("a.doc;b.doc"));
        var h = sys.Load<Bundle>("pack.bundle");
        await h.Ready;
        Assert.Equal("X!", h.Value.A.Text);
        Assert.Equal("Y!", h.Value.B.Text);
    }
}
