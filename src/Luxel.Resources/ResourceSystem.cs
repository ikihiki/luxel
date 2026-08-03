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
    public bool HasValue;
    public long LoadGeneration;
    public int ReloadQueued;
    public int RefCount;
    public readonly HashSet<ResourceNode> Dependents = new();
    public readonly HashSet<ResourceNode> Dependencies = new();
    public IReloadToken? WatchToken;
    public CancellationTokenSource? Cts;
    public string StepName = "?";
    public Executor StepExecutor;
    public event Action? Reloaded;
    public void FireReloaded() => Reloaded?.Invoke();
    public ResourceOwnership Ownership = ResourceOwnership.Owned;
    public bool IsEvicted;
}

/// <summary>リソースへの安定参照ハンドル。再ロードで <see cref="Value"/> が差し替わる。</summary>
public sealed class ResourceHandle<T> : IDisposable
{
    private readonly ResourceSystem _sys;
    internal readonly ResourceNode Node;
    private int _disposed;

    internal ResourceHandle(ResourceSystem sys, ResourceNode node) { _sys = sys; Node = node; }

    public T Value => Node.Value is T t ? t : default!;
    public bool IsReady => Node.Status == ResourceStatus.Ready;
    public ResourceStatus Status => Node.Status;
    public ResourceUri Uri => Node.Uri;
    public Task Ready => Node.Computed;
    public int Version => Node.Version;
    /// <summary>初回ロード成功済み、または再ロード失敗後も最後の正常値を保持している。</summary>
    public bool HasValue => Node.HasValue;
    /// <summary>直近のロード失敗。後続ロード成功時にクリアされる。</summary>
    public Exception? LastReloadError => Node.Error;
    public Exception? Error => Node.Error;

    public event Action Reloaded { add => Node.Reloaded += value; remove => Node.Reloaded -= value; }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _sys.Release(Node);
    }
}

/// <summary>ローダ/ステップへ渡す文脈。ステージ hop + 依存ロード。DI コンテナは持たない (Step は ctor で依存を受け取る)。</summary>
public sealed class LoadContext
{
    private readonly ResourceSystem _sys;
    internal readonly ResourceNode Owner;
    private readonly CancellationToken _token;
    public ResourceUri Uri { get; }

    internal LoadContext(ResourceSystem sys, ResourceNode owner, ResourceUri uri, CancellationToken token)
    { _sys = sys; Owner = owner; Uri = uri; _token = token; }

    public CancellationToken Token => _token;
    public StageAwaitable Io => _sys.IoStage;
    public StageAwaitable Cpu => _sys.CpuStage;
    public StageAwaitable External => _sys.ExternalStage;

    /// <summary>現在のロード結果の破棄責任を <see cref="ResourceSystem"/> に設定する。</summary>
    public void MarkOwned() => Owner.Ownership = ResourceOwnership.Owned;

    /// <summary>現在のロード結果を外部所有として扱う。</summary>
    public void MarkBorrowed() => Owner.Ownership = ResourceOwnership.Borrowed;

    /// <summary>依存リソースを (型,uri) で自動合成しロード (キャッシュ共有・リロード伝播)。</summary>
    public ResourceHandle<U> Load<U>(string uri) => _sys.LoadDependency<U>(uri, Owner);
    public ResourceHandle<U> Load<U>(string uri, Loader<U> loader) => _sys.LoadDependency(uri, loader, Owner);
}

internal sealed class PumpFlushRegistration : IDisposable
{
    private readonly ResourceSystem _system;
    internal readonly ResourceNode Node;
    internal readonly Func<bool> Callback;
    private int _disposed;

    internal PumpFlushRegistration(ResourceSystem system, ResourceNode node, Func<bool> callback)
    {
        _system = system;
        Node = node;
        Callback = callback;
    }

    internal bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0) _system.RemovePumpFlush(this);
    }
}

/// <summary>論理 owner に属するリソース lease をまとめて解放するスコープ。</summary>
public sealed class ResourceScope : IDisposable
{
    private readonly ResourceSystem _system;
    private readonly object _lock = new();
    private List<IDisposable>? _leases = new();

    internal ResourceScope(ResourceSystem system, string ownerId)
    {
        _system = system;
        OwnerId = ownerId;
    }

    /// <summary>この scope を所有する ResourceSystem。上位の型付き integration が設定を解決するために使用する。</summary>
    public ResourceSystem System => _system;

    public string OwnerId { get; }

    /// <summary>共有 URI をロードし、このスコープの lease として追跡する。</summary>
    public ResourceHandle<T> Load<T>(string uri) => Track(_system.Load<T>(uri));

    /// <summary>owner 内で一意な key を scope-qualified URI に変換して明示 loader で作成する。</summary>
    public ResourceHandle<T> Create<T>(string localKey, Loader<T> loader)
        => Create(localKey, loader, ResourceOwnership.Owned);

    /// <summary>owner 内で一意な key を scope-qualified URI に変換し、所有権を明示して作成する。</summary>
    public ResourceHandle<T> Create<T>(string localKey, Loader<T> loader, ResourceOwnership ownership)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(localKey);
        ArgumentNullException.ThrowIfNull(loader);
        string uri = $"scope://{Uri.EscapeDataString(OwnerId)}/{Uri.EscapeDataString(localKey)}";
        return Track(_system.Load(uri, loader, ownership));
    }

    private ResourceHandle<T> Track<T>(ResourceHandle<T> handle)
    {
        lock (_lock)
        {
            if (_leases is null)
            {
                handle.Dispose();
                throw new ObjectDisposedException(nameof(ResourceScope));
            }
            _leases.Add(handle);
            return handle;
        }
    }

    public void Dispose()
    {
        List<IDisposable>? leases;
        lock (_lock)
        {
            leases = _leases;
            _leases = null;
        }
        if (leases is null) return;
        foreach (IDisposable lease in leases) lease.Dispose();
    }
}

/// <summary>
/// リソース管理システム。(型,uri) ノードキャッシュ + 出力型逆引きの再帰オートコンポーズ + 自動リロード。
///
/// <para><b>Source/Step は構築済みインスタンスをコンストラクタ配列 or <see cref="AddSource"/> / <see cref="AddStep"/>
/// で登録する</b>。外部サービス等の依存は Step の ctor 引数として呼び出し側 (アプリ) が組み立てて渡す ─ 本システムに
/// DI コンテナは含まれない。</para>
/// </summary>
public sealed class ResourceSystem : IDisposable
{
    private readonly Pipeline _pipeline;
    private readonly ResourceLane _io, _cpu, _external;
    private readonly Dictionary<string, ResourceNode> _cache = new();
    private readonly object _lock = new();
    private readonly ConcurrentQueue<ResourceNode> _reloadQueue = new();
    private readonly ConcurrentQueue<Action> _publishQueue = new();
    private readonly List<PumpFlushRegistration> _flushRegistrations = new();
    private readonly List<object> _deferredDispose = new();
    private bool _autoReload;
    private volatile bool _disposed;
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
        _external = new ResourceLane(Math.Max(2, n));

        if (sources is not null) foreach (var s in sources) _pipeline.AddSource(s);
        if (steps is not null) foreach (var s in steps) _pipeline.AddStep(s);
    }

    internal StageAwaitable IoStage => new(_io);
    internal StageAwaitable CpuStage => new(_cpu);
    internal StageAwaitable ExternalStage => new(_external);
    internal StageAwaitable Stage(Executor e) => e switch { Executor.Io => IoStage, Executor.External => ExternalStage, _ => CpuStage };

    /// <summary>Source インスタンスを追加登録 (実行時追加、通常はコンストラクタ配列を推奨)。</summary>
    public void AddSource(IResourceSource source) => _pipeline.AddSource(source);
    /// <summary>Step インスタンスを追加登録 (実行時追加、通常はコンストラクタ配列を推奨)。</summary>
    public void AddStep(IResourceStep step) => _pipeline.AddStep(step);

    /// <summary>論理 owner に属する resource lease をまとめて管理するスコープを作成する。</summary>
    public ResourceScope CreateScope(string ownerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        ThrowIfDisposed();
        return new ResourceScope(this, ownerId);
    }

    /// <summary>ファイル変更等による自動リロードを有効化。</summary>
    public void Watch() => _autoReload = true;

    public ResourceHandle<T> Load<T>(string uri)
    {
        ThrowIfDisposed();
        ResourceNode node = GetOrCreate(typeof(T), new ResourceUri(uri), explicitLoader: null, ownership: null);
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

    /// <summary>値を uri に登録する。既存動作との互換性のため既定は owned。</summary>
    public ResourceHandle<T> Publish<T>(string uri, T value) where T : class
        => Publish(uri, value, ResourceOwnership.Owned);

    /// <summary>所有権を明示して値を uri に登録する。以後 <see cref="Load{T}(string)"/> で取得可能。</summary>
    public ResourceHandle<T> Publish<T>(string uri, T value, ResourceOwnership ownership) where T : class
    {
        ThrowIfDisposed();
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
                HasValue = true,
                Status = ResourceStatus.Ready,
                Computed = Task.FromResult<object>(value),
                Loader = _ => Task.FromResult<object>(value),
                Ownership = ownership,
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

    /// <summary>handle の node が生存中だけ Pump 時に呼ぶ callback を登録する。</summary>
    public void RegisterPumpFlush<T>(ResourceHandle<T> handle, Func<bool> flushCallback)
        => RegisterPumpFlushLease(handle, flushCallback);

    /// <summary>破棄による明示解除も可能な Pump callback lease を登録する。</summary>
    public IDisposable RegisterPumpFlushLease<T>(ResourceHandle<T> handle, Func<bool> flushCallback)
    {
        ArgumentNullException.ThrowIfNull(handle);
        ArgumentNullException.ThrowIfNull(flushCallback);
        ThrowIfDisposed();
        var registration = new PumpFlushRegistration(this, handle.Node, flushCallback);
        lock (_lock)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(ResourceSystem));
            if (handle.Node.IsEvicted) throw new ObjectDisposedException(nameof(handle));
            _flushRegistrations.Add(registration);
        }
        return registration;
    }

    internal void RemovePumpFlush(PumpFlushRegistration registration)
    {
        lock (_lock) _flushRegistrations.Remove(registration);
    }

    /// <summary>Pump 時に <c>_deferredDispose</c> を実際に破棄する前に呼ばれる汎用 hook。</summary>
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
        => Load(uri, loader, ResourceOwnership.Owned);

    /// <summary>明示 loader の結果の所有権を指定してロードする。</summary>
    public ResourceHandle<T> Load<T>(string uri, Loader<T> loader, ResourceOwnership ownership)
    {
        ArgumentNullException.ThrowIfNull(loader);
        ThrowIfDisposed();
        ResourceNode node = GetOrCreate(typeof(T), new ResourceUri(uri), Wrap(loader), ownership);
        Interlocked.Increment(ref node.RefCount);
        return new ResourceHandle<T>(this, node);
    }

    internal ResourceHandle<U> LoadDependency<U>(string uri, ResourceNode owner)
    {
        ResourceNode node = GetOrCreate(typeof(U), new ResourceUri(uri), null, ownership: null);
        AddEdge(owner, node);
        Interlocked.Increment(ref node.RefCount);
        return new ResourceHandle<U>(this, node);
    }

    internal ResourceHandle<U> LoadDependency<U>(string uri, Loader<U> loader, ResourceNode owner)
    {
        ResourceNode node = GetOrCreate(typeof(U), new ResourceUri(uri), Wrap(loader), ResourceOwnership.Owned);
        AddEdge(owner, node);
        Interlocked.Increment(ref node.RefCount);
        return new ResourceHandle<U>(this, node);
    }

    private static Func<LoadContext, Task<object>> Wrap<T>(Loader<T> loader)
        => async ctx => (object)(await loader(ctx))!;

    private ResourceNode GetOrCreate(
        Type type,
        ResourceUri uri,
        Func<LoadContext, Task<object>>? explicitLoader,
        ResourceOwnership? ownership)
    {
        string key = type.FullName + "|" + uri.Key;
        lock (_lock)
        {
            if (_cache.TryGetValue(key, out ResourceNode? existing)) return existing;
            var node = new ResourceNode { Type = type, Uri = uri, Key = key };
            if (explicitLoader != null)
            {
                node.Loader = explicitLoader;
                node.StepName = "loader";
                node.StepExecutor = Executor.Cpu;
                node.Ownership = ownership ?? ResourceOwnership.Owned;
            }
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
        if (uri.Fragment.Length > 0) node.Ownership = ResourceOwnership.Borrowed;
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
        ResourceNode dep = GetOrCreate(type, uri, null, ownership: null);
        AddEdge(owner, dep);
        return dep;
    }

    private void AddEdge(ResourceNode owner, ResourceNode dep)
    {
        lock (_lock) { dep.Dependents.Add(owner); owner.Dependencies.Add(dep); }
    }

    private void StartLoad(ResourceNode node, bool isReload)
    {
        var cts = new CancellationTokenSource();
        CancellationTokenSource? previous;
        long generation;
        lock (node)
        {
            previous = node.Cts;
            node.Cts = cts;
            generation = ++node.LoadGeneration;
            node.Status = ResourceStatus.Loading;
            var ctx = new LoadContext(this, node, node.Uri, cts.Token);
            node.Computed = RunLoad(node, ctx, isReload, generation, cts);
        }
        try { previous?.Cancel(); } catch (ObjectDisposedException) { }
    }

    private async Task<object> RunLoad(
        ResourceNode node,
        LoadContext ctx,
        bool isReload,
        long generation,
        CancellationTokenSource cts)
    {
        try
        {
            object v = await node.Loader(ctx);
            lock (node)
            {
                if (node.LoadGeneration != generation || node.IsEvicted)
                {
                    if (node.Ownership == ResourceOwnership.Owned) (v as IDisposable)?.Dispose();
                    return v;
                }

                node.Status = ResourceStatus.Ready;
                node.Error = null;
                if (isReload)
                {
                    _publishQueue.Enqueue(() => PublishIfCurrent(node, v, generation));
                }
                else
                {
                    node.Value = v;
                    node.HasValue = true;
                }
            }
            _graphDirty = true;
            return v;
        }
        catch (Exception e)
        {
            lock (node)
            {
                if (node.LoadGeneration == generation)
                {
                    node.Status = node.HasValue ? ResourceStatus.Ready : ResourceStatus.Failed;
                    node.Error = e;
                    _graphDirty = true;
                }
            }
            throw;
        }
        finally
        {
            lock (node)
            {
                if (ReferenceEquals(node.Cts, cts)) node.Cts = null;
            }
            cts.Dispose();
        }
    }

    private void PublishIfCurrent(ResourceNode node, object value, long generation)
        => NotifyPublish(node, value, generation);

    public void Pump()
    {
        lock (_lock)
        {
            foreach (PumpFlushRegistration registration in _flushRegistrations.ToArray())
            {
                if (registration.IsDisposed || registration.Node.IsEvicted) continue;
                if (registration.Callback())
                {
                    registration.Node.Version++;
                    registration.Node.FireReloaded();
                    _graphDirty = true;
                }
            }
        }
        while (_publishQueue.TryDequeue(out Action? a)) a();
        while (_reloadQueue.TryDequeue(out ResourceNode? n))
        {
            Interlocked.Exchange(ref n.ReloadQueued, 0);
            if (!n.IsEvicted) StartLoad(n, isReload: true);
        }
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

    private void NotifyPublish(ResourceNode node, object newValue, long? generation = null)
    {
        object? old;
        lock (node)
        {
            if (node.IsEvicted || (generation.HasValue && node.LoadGeneration != generation.Value))
            {
                if (node.Ownership == ResourceOwnership.Owned) (newValue as IDisposable)?.Dispose();
                return;
            }
            old = node.Value;
            node.Value = newValue;
            node.HasValue = true;
            node.Version++;
        }
        if (old != null && !ReferenceEquals(old, newValue) && node.Ownership == ResourceOwnership.Owned)
            _deferredDispose.Add(old);
        node.FireReloaded();
        _graphDirty = true;
        ResourceNode[] deps;
        lock (_lock) deps = node.Dependents.ToArray();
        foreach (ResourceNode d in deps) EnqueueReload(d);
    }

    private void EnqueueReload(ResourceNode node)
    {
        if (!node.IsEvicted && Interlocked.CompareExchange(ref node.ReloadQueued, 1, 0) == 0)
            _reloadQueue.Enqueue(node);
    }

    /// <summary>現在キャッシュされている全 node を再ロード対象として無効化する。</summary>
    public void InvalidateAll()
    {
        ThrowIfDisposed();
        ResourceNode[] all;
        lock (_lock) all = _cache.Values.ToArray();
        foreach (ResourceNode n in all) EnqueueReload(n);
    }

    private void RegisterWatch(ResourceNode node)
    {
        if (node.Type != typeof(byte[])) return;
        IResourceSource? src = _pipeline.Source(node.Uri.Scheme);
        node.WatchToken = src?.Watch(node.Uri, () => EnqueueReload(node));
    }

    internal void Release(ResourceNode node)
    {
        lock (_lock)
        {
            if (node.IsEvicted) return;
            if (Interlocked.Decrement(ref node.RefCount) > 0) return;
            TryEvict(node);
        }
    }

    private void TryEvict(ResourceNode node)
    {
        if (node.IsEvicted || node.RefCount > 0 || node.Dependents.Count > 0) return;
        object? value;
        lock (node)
        {
            node.IsEvicted = true;
            value = node.Value;
            node.Value = null;
            node.HasValue = false;
        }
        _cache.Remove(node.Key);
        _flushRegistrations.RemoveAll(r => ReferenceEquals(r.Node, node));
        _graphDirty = true;
        node.WatchToken?.Dispose();
        try { node.Cts?.Cancel(); } catch { }
        if (value != null && node.Ownership == ResourceOwnership.Owned) _deferredDispose.Add(value);
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
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            all = _cache.Values.ToArray();
            _cache.Clear();
            _flushRegistrations.Clear();
        }
        foreach (ResourceNode n in all)
        {
            object? value;
            lock (n)
            {
                n.IsEvicted = true;
                value = n.Value;
                n.Value = null;
                n.HasValue = false;
            }
            n.WatchToken?.Dispose();
            try { n.Cts?.Cancel(); } catch { }
            if (n.Ownership == ResourceOwnership.Owned) (value as IDisposable)?.Dispose();
        }
        foreach (object o in _deferredDispose) (o as IDisposable)?.Dispose();
        _deferredDispose.Clear();
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(ResourceSystem));
    }
}
