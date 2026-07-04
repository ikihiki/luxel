using System.Collections.Concurrent;
using Luxel.Diagnostics;

namespace Luxel.Resources;

/// <summary>キャッシュ上の 1 ノード ((出力型, 正規化uri) で一意)。</summary>
internal sealed class ResourceNode
{
    public required Type Type;
    public required ResourceUri Uri;
    public required string Key;
    public Func<LoadContext, Task<object>> Loader = null!;
    public volatile object? Value;
    public int Version;
    public ResourceStatus Status = ResourceStatus.Loading;
    public Task<object> Computed = null!;
    public Exception? Error;
    public int RefCount;
    public readonly HashSet<ResourceNode> Dependents = new();
    public readonly HashSet<ResourceNode> Dependencies = new();
    public IReloadToken? WatchToken;
    public CancellationTokenSource Cts = new();
    public string StepName = "?";
    public Executor StepExecutor;
    public event Action? Reloaded;
    public void FireReloaded() => Reloaded?.Invoke();
    public bool IsBorrowed;
}

/// <summary>リソースへの安定参照ハンドル。再ロードで <see cref="Value"/> が差し替わる。</summary>
public sealed class ResourceHandle<T> : IDisposable
{
    private readonly ResourceSystem _sys;
    internal readonly ResourceNode Node;
    private bool _disposed;

    internal ResourceHandle(ResourceSystem sys, ResourceNode node) { _sys = sys; Node = node; }

    public T Value => Node.Value is T t ? t : default!;
    public bool IsReady => Node.Status == ResourceStatus.Ready;
    public ResourceStatus Status => Node.Status;
    public ResourceUri Uri => Node.Uri;
    public Task Ready => Node.Computed;
    public int Version => Node.Version;
    public Exception? Error => Node.Error;

    public event Action Reloaded { add => Node.Reloaded += value; remove => Node.Reloaded -= value; }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _sys.Release(Node);
    }
}

/// <summary>ローダ/ステップへ渡す文脈。ステージ hop + 依存ロード。DI コンテナは持たない (Step は ctor で依存を受け取る)。</summary>
public sealed class LoadContext
{
    private readonly ResourceSystem _sys;
    internal readonly ResourceNode Owner;
    public ResourceUri Uri { get; }

    internal LoadContext(ResourceSystem sys, ResourceNode owner, ResourceUri uri) { _sys = sys; Owner = owner; Uri = uri; }

    public CancellationToken Token => Owner.Cts.Token;
    public StageAwaitable Io => _sys.IoStage;
    public StageAwaitable Cpu => _sys.CpuStage;
    public StageAwaitable Gpu => _sys.GpuStage;

    /// <summary>fragment 有りノードは既定で borrowed。新規リソース作成 step は本メソッドで所有権を Resources に譲渡。</summary>
    public void MarkOwned() => Owner.IsBorrowed = false;

    /// <summary>依存リソースを (型,uri) で自動合成しロード (キャッシュ共有・リロード伝播)。</summary>
    public ResourceHandle<U> Load<U>(string uri) => _sys.LoadDependency<U>(uri, Owner);
    public ResourceHandle<U> Load<U>(string uri, Loader<U> loader) => _sys.LoadDependency(uri, loader, Owner);
}

/// <summary>
/// リソース管理システム。(型,uri) ノードキャッシュ + 出力型逆引きの再帰オートコンポーズ + 自動リロード。
///
/// <para><b>Source/Step は構築済みインスタンスをコンストラクタ配列 or <see cref="AddSource"/> / <see cref="AddStep"/>
/// で登録する</b>。GPU device 等の依存は Step の ctor 引数として呼び出し側 (アプリ) が組み立てて渡す ─ 本システムに
/// DI コンテナは含まれない。</para>
/// </summary>
public sealed class ResourceSystem : IDisposable
{
    private readonly Pipeline _pipeline;
    private readonly ResourceLane _io, _cpu, _gpu;
    private readonly Dictionary<string, ResourceNode> _cache = new();
    private readonly object _lock = new();
    private readonly ConcurrentQueue<ResourceNode> _reloadQueue = new();
    private readonly ConcurrentQueue<Action> _publishQueue = new();
    private readonly List<Func<bool>> _flushCallbacks = new();
    private readonly List<ResourceNode> _flushNodes = new();
    private readonly List<object> _deferredDispose = new();
    private bool _autoReload;
    private volatile bool _graphDirty;
    private Action? _deferredIdleHook;

    /// <summary>Resource システムを生成。Source/Step はすべて呼び出し側が構築済みインスタンスで渡す。
    /// 組込み (<see cref="FileSource"/>/<see cref="HttpSource"/>/<see cref="TexDecoder"/>) も自動登録しない ─ 必要なら
    /// <see cref="ResourceSystemDefaults"/> のヘルパを使うか、直接 new して配列に含める。</summary>
    /// <param name="sources">登録する Source インスタンス配列。</param>
    /// <param name="steps">登録する Step インスタンス配列。</param>
    public ResourceSystem(
        IReadOnlyList<IResourceSource>? sources = null,
        IReadOnlyList<IResourceStep>? steps = null)
    {
        _pipeline = new Pipeline();
        int n = Environment.ProcessorCount;
        _io = new ResourceLane(Math.Max(4, n));
        _cpu = new ResourceLane(n);
        _gpu = new ResourceLane(Math.Max(2, n));

        if (sources is not null) foreach (var s in sources) _pipeline.AddSource(s);
        if (steps is not null) foreach (var s in steps) _pipeline.AddStep(s);
    }

    internal StageAwaitable IoStage => new(_io);
    internal StageAwaitable CpuStage => new(_cpu);
    internal StageAwaitable GpuStage => new(_gpu);
    internal StageAwaitable Stage(Executor e) => e switch { Executor.Io => IoStage, Executor.Gpu => GpuStage, _ => CpuStage };

    /// <summary>Source インスタンスを追加登録 (実行時追加、通常はコンストラクタ配列を推奨)。</summary>
    public void AddSource(IResourceSource source) => _pipeline.AddSource(source);
    /// <summary>Step インスタンスを追加登録 (実行時追加、通常はコンストラクタ配列を推奨)。</summary>
    public void AddStep(IResourceStep step) => _pipeline.AddStep(step);

    /// <summary>ファイル変更等による自動リロードを有効化。</summary>
    public void Watch() => _autoReload = true;

    public ResourceHandle<T> Load<T>(string uri)
    {
        ResourceNode node = GetOrCreate(typeof(T), new ResourceUri(uri), explicitLoader: null);
        Interlocked.Increment(ref node.RefCount);
        return new ResourceHandle<T>(this, node);
    }

    /// <summary>URI 不要でプログラム値を登録する overload。key は "published://&lt;TypeName&gt;/&lt;HashCode&gt;"。</summary>
    public ResourceHandle<T> Publish<T>(T value) where T : class
    {
        int hash = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(value);
        string uri = $"published://{typeof(T).Name}/{hash:x8}";
        return Publish(uri, value);
    }

    /// <summary>外部所有の値を uri に登録する。以後 <see cref="Load{T}(string)"/> で取得可能。</summary>
    public ResourceHandle<T> Publish<T>(string uri, T value) where T : class
    {
        string key = typeof(T).FullName + "|" + new ResourceUri(uri).Key;
        lock (_lock)
        {
            if (_cache.TryGetValue(key, out ResourceNode? existing))
                throw new InvalidOperationException($"既に登録済み: {uri}");
            var node = new ResourceNode
            {
                Type = typeof(T),
                Uri = new ResourceUri(uri),
                Key = key,
                StepName = "publish",
                StepExecutor = Executor.Cpu,
                Value = value,
                Status = ResourceStatus.Ready,
                Computed = Task.FromResult<object>(value),
                Loader = _ => Task.FromResult<object>(value),
            };
            _cache[key] = node;
            Interlocked.Increment(ref node.RefCount);
            _graphDirty = true;
            return new ResourceHandle<T>(this, node);
        }
    }

    /// <summary>Publish 済み uri の値を差し替える (次 Pump で Reloaded 伝播)。</summary>
    public void Republish<T>(string uri, T value) where T : class
    {
        string key = typeof(T).FullName + "|" + new ResourceUri(uri).Key;
        ResourceNode node;
        lock (_lock)
        {
            if (!_cache.TryGetValue(key, out ResourceNode? found))
                throw new InvalidOperationException($"Publish されていない uri: {uri}");
            node = found;
        }
        _publishQueue.Enqueue(() => NotifyPublish(node, value));
    }

    /// <summary>Publish 済み handle に「Pump 時に呼ぶ dirty flush callback」を登録。RenderBuffer/RenderTarget 等が利用する公開 API。</summary>
    public void RegisterPumpFlush<T>(ResourceHandle<T> handle, Func<bool> flushCallback)
    {
        lock (_lock)
        {
            _flushCallbacks.Add(flushCallback);
            _flushNodes.Add(handle.Node);
        }
    }

    /// <summary>Pump 時に <c>_deferredDispose</c> を実際に破棄する前に呼ばれる hook (GPU の WaitIdle 用)。</summary>
    public void SetDeferredDisposeIdleHook(Action hook) => _deferredIdleHook = hook;

    internal ResourceNode GetPublishedNode<T>(string uri) where T : class
    {
        string key = typeof(T).FullName + "|" + new ResourceUri(uri).Key;
        lock (_lock)
        {
            if (!_cache.TryGetValue(key, out ResourceNode? found))
                throw new InvalidOperationException($"Publish されていない uri: {uri}");
            return found;
        }
    }

    public ResourceHandle<T> Load<T>(string uri, Loader<T> loader)
    {
        ResourceNode node = GetOrCreate(typeof(T), new ResourceUri(uri), Wrap(loader));
        Interlocked.Increment(ref node.RefCount);
        return new ResourceHandle<T>(this, node);
    }

    internal ResourceHandle<U> LoadDependency<U>(string uri, ResourceNode owner)
    {
        ResourceNode node = GetOrCreate(typeof(U), new ResourceUri(uri), null);
        AddEdge(owner, node);
        Interlocked.Increment(ref node.RefCount);
        return new ResourceHandle<U>(this, node);
    }

    internal ResourceHandle<U> LoadDependency<U>(string uri, Loader<U> loader, ResourceNode owner)
    {
        ResourceNode node = GetOrCreate(typeof(U), new ResourceUri(uri), Wrap(loader));
        AddEdge(owner, node);
        Interlocked.Increment(ref node.RefCount);
        return new ResourceHandle<U>(this, node);
    }

    private static Func<LoadContext, Task<object>> Wrap<T>(Loader<T> loader)
        => async ctx => (object)(await loader(ctx))!;

    private ResourceNode GetOrCreate(Type type, ResourceUri uri, Func<LoadContext, Task<object>>? explicitLoader)
    {
        string key = type.FullName + "|" + uri.Key;
        lock (_lock)
        {
            if (_cache.TryGetValue(key, out ResourceNode? existing)) return existing;
            var node = new ResourceNode { Type = type, Uri = uri, Key = key };
            if (explicitLoader != null) { node.Loader = explicitLoader; node.StepName = "loader"; node.StepExecutor = Executor.Cpu; }
            else node.Loader = Compose(type, uri, node);
            _cache[key] = node;
            _graphDirty = true;
            if (_autoReload) RegisterWatch(node);
            StartLoad(node, isReload: false);
            return node;
        }
    }

    private Func<LoadContext, Task<object>> Compose(Type type, ResourceUri uri, ResourceNode node)
    {
        if (type == typeof(byte[]))
        {
            IResourceSource src = _pipeline.Source(uri.Scheme)
                ?? throw new InvalidOperationException($"スキーム '{uri.Scheme}' のソース未登録 ({uri})。");
            node.StepName = "source:" + src.GetType().Name;
            node.StepExecutor = Executor.Io;
            return async ctx => { await ctx.Io; return (object)await src.ReadAsync(uri, ctx); };
        }
        StepAdapter step = _pipeline.Select(type, uri.Extension, uri.Fragment)
            ?? throw new InvalidOperationException($"型 {type.Name} を生成するステップ未登録 (uri={uri}, ext={uri.Extension}, frag={uri.Fragment})。");
        node.StepName = step.Name;
        node.StepExecutor = step.Executor;
        Type inType = step.Input;
        ResourceUri depUri = uri.Fragment.Length > 0 ? uri.WithoutFragment() : uri;
        if (uri.Fragment.Length > 0) node.IsBorrowed = true;
        return async ctx =>
        {
            ResourceNode dep = GetOrCreateDep(inType, depUri, node);
            await dep.Computed;
            await Stage(step.Executor);
            return await step.Run(dep.Value!, uri, ctx);
        };
    }

    private ResourceNode GetOrCreateDep(Type type, ResourceUri uri, ResourceNode owner)
    {
        ResourceNode dep = GetOrCreate(type, uri, null);
        AddEdge(owner, dep);
        return dep;
    }

    private void AddEdge(ResourceNode owner, ResourceNode dep)
    {
        lock (_lock) { dep.Dependents.Add(owner); owner.Dependencies.Add(dep); }
    }

    private void StartLoad(ResourceNode node, bool isReload)
    {
        var ctx = new LoadContext(this, node, node.Uri);
        node.Status = ResourceStatus.Loading;
        node.Computed = RunLoad(node, ctx, isReload);
    }

    private async Task<object> RunLoad(ResourceNode node, LoadContext ctx, bool isReload)
    {
        try
        {
            object v = await node.Loader(ctx);
            node.Status = ResourceStatus.Ready;
            if (isReload) _publishQueue.Enqueue(() => NotifyPublish(node, v));
            else node.Value = v;
            _graphDirty = true;
            return v;
        }
        catch (Exception e)
        {
            node.Status = ResourceStatus.Failed;
            node.Error = e;
            throw;
        }
    }

    public void Pump()
    {
        lock (_lock)
        {
            for (int i = 0; i < _flushCallbacks.Count; i++)
            {
                if (_flushCallbacks[i]())
                {
                    _flushNodes[i].Version++;
                    _flushNodes[i].FireReloaded();
                    _graphDirty = true;
                }
            }
        }
        while (_publishQueue.TryDequeue(out Action? a)) a();
        while (_reloadQueue.TryDequeue(out ResourceNode? n)) StartLoad(n, isReload: true);
        if (_deferredDispose.Count > 0)
        {
            _deferredIdleHook?.Invoke();
            foreach (object o in _deferredDispose) (o as IDisposable)?.Dispose();
            _deferredDispose.Clear();
        }
        if (_graphDirty && EngineDiagnostics.IsEnabled(EngineDiagnostics.Resources))
        {
            _graphDirty = false;
            EmitGraph();
        }
    }

    private void EmitGraph()
    {
        DiagResourceNode[] nodes;
        lock (_lock)
        {
            nodes = _cache.Values.Select(n => new DiagResourceNode(
                Key: $"{n.Type.Name} {n.Uri}",
                Type: n.Type.Name,
                Uri: n.Uri.ToString(),
                Status: n.Status.ToString(),
                Version: n.Version,
                Step: n.StepName,
                Executor: n.StepExecutor.ToString(),
                Inputs: n.Dependencies.Select(d => $"{d.Type.Name} {d.Uri}").ToArray())).ToArray();
        }
        EngineDiagnostics.Emit(EngineDiagnostics.Resources, new DiagResources(nodes));
    }

    private void NotifyPublish(ResourceNode node, object newValue)
    {
        object? old = node.Value;
        node.Value = newValue;
        node.Version++;
        if (old != null && !ReferenceEquals(old, newValue)) _deferredDispose.Add(old);
        node.FireReloaded();
        _graphDirty = true;
        ResourceNode[] deps;
        lock (_lock) deps = node.Dependents.ToArray();
        foreach (ResourceNode d in deps) _reloadQueue.Enqueue(d);
    }

    public void NotifyDeviceLost()
    {
        ResourceNode[] all;
        lock (_lock) all = _cache.Values.ToArray();
        foreach (ResourceNode n in all) _reloadQueue.Enqueue(n);
    }

    private void RegisterWatch(ResourceNode node)
    {
        if (node.Type != typeof(byte[])) return;
        IResourceSource? src = _pipeline.Source(node.Uri.Scheme);
        node.WatchToken = src?.Watch(node.Uri, () => _reloadQueue.Enqueue(node));
    }

    internal void Release(ResourceNode node)
    {
        lock (_lock)
        {
            if (Interlocked.Decrement(ref node.RefCount) > 0) return;
            TryEvict(node);
        }
    }

    private void TryEvict(ResourceNode node)
    {
        if (node.RefCount > 0 || node.Dependents.Count > 0) return;
        _cache.Remove(node.Key);
        _graphDirty = true;
        node.WatchToken?.Dispose();
        try { node.Cts.Cancel(); } catch { }
        if (node.Value != null && !node.IsBorrowed) _deferredDispose.Add(node.Value);
        foreach (ResourceNode dep in node.Dependencies.ToArray())
        {
            dep.Dependents.Remove(node);
            if (dep.RefCount == 0 && dep.Dependents.Count == 0) TryEvict(dep);
        }
        node.Dependencies.Clear();
    }

    public void Dispose()
    {
        ResourceNode[] all;
        lock (_lock) { all = _cache.Values.ToArray(); _cache.Clear(); }
        foreach (ResourceNode n in all)
        {
            n.WatchToken?.Dispose();
            if (!n.IsBorrowed) (n.Value as IDisposable)?.Dispose();
        }
        foreach (object o in _deferredDispose) (o as IDisposable)?.Dispose();
        _deferredDispose.Clear();
    }
}
