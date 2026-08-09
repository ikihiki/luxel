using System.Collections.Concurrent;
using Luxel.Diagnostics;

namespace Luxel.Resources;

/// <summary>UI等がPump thread上で観測できるresource nodeの状態snapshot。</summary>
public readonly record struct ResourceState(
    ResourceStatus Status,
    bool HasValue,
    int Version,
    Exception? Error);

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
    public readonly List<ResourceStateSubscription> StateSubscriptions = [];
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
    internal ResourceSystem System => _sys;

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
    public ResourceState State => new(Node.Status, Node.HasValue, Node.Version, Node.Error);

    public event Action Reloaded { add => Node.Reloaded += value; remove => Node.Reloaded -= value; }

    /// <summary>ResourceSystem.Pump() thread上で状態遷移を受け取る。</summary>
    public IDisposable SubscribeState(Action<ResourceState> callback)
        => _sys.SubscribeState(this, callback);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _sys.Release(Node);
    }
}

internal sealed class ResourceStateSubscription : IDisposable
{
    private readonly ResourceSystem _system;
    internal readonly ResourceNode Node;
    internal readonly Action<ResourceState> Callback;
    private int _disposed;

    internal ResourceStateSubscription(ResourceSystem system, ResourceNode node, Action<ResourceState> callback)
    {
        _system = system;
        Node = node;
        Callback = callback;
    }

    internal bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0) _system.RemoveStateSubscription(this);
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

    /// <summary>現在の resource URI を基準に相対 URI を解決して依存ロードする。</summary>
    public ResourceHandle<U> LoadRelative<U>(string relativeUri)
        => _sys.LoadDependency<U>(Uri.Resolve(relativeUri).Url, Owner);

    /// <summary>現在の resource URI を基準に相対 URI を解決し、明示 loader で依存ロードする。</summary>
    public ResourceHandle<U> LoadRelative<U>(string relativeUri, Loader<U> loader)
        => _sys.LoadDependency(Uri.Resolve(relativeUri).Url, loader, Owner);

    /// <summary>既存handleを現在nodeの依存として接続し、その現在generationの値を待つ。</summary>
    public Task<U> Require<U>(ResourceHandle<U> dependency)
        => _sys.RequireDependency(dependency, Owner, _token);
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

    public string OwnerId { get; }

    /// <summary>共有 URI をロードし、このスコープの lease として追跡する。</summary>
    public ResourceHandle<T> Load<T>(string uri) => Track(_system.Load<T>(uri));

    /// <summary>owner 内で一意な key を scope-qualified URI に変換して明示 loader で作成する。</summary>
    public ResourceHandle<T> Create<T>(string localKey, Loader<T> loader)
        => Create(localKey, loader, ResourceOwnership.Owned);

    /// <summary>owner 内で一意な key を scope-qualified URI に変換し、所有権を明示して作成する。</summary>
    public ResourceHandle<T> Create<T>(string localKey, Loader<T> loader, ResourceOwnership ownership)
    {
        ArgumentNullException.ThrowIfNull(loader);
        string uri = Qualify(localKey);
        return Track(_system.Load(uri, loader, ownership));
    }

    /// <summary>
    /// scope-local input を borrowed node として登録し、登録済み Step を通して出力を生成する。
    /// Step の依存は Step コンストラクタで注入する。
    /// </summary>
    public ResourceHandle<TOutput> Create<TInput, TOutput>(string localKey, TInput input)
        where TInput : class
        => Create<TInput, TOutput>(localKey, input, fragment: null);

    /// <summary>
    /// scope-local input を登録し、fragment selector付きのStepで出力を生成する。
    /// shaderの <c>#graphics</c>/<c>#compute</c> のように同じsourceから複数programを作る用途向け。
    /// </summary>
    public ResourceHandle<TOutput> Create<TInput, TOutput>(string localKey, TInput input, string? fragment)
        where TInput : class
    {
        ArgumentNullException.ThrowIfNull(input);
        string uri = Qualify(localKey);
        Track(_system.Load(uri, _ => Task.FromResult(input), ResourceOwnership.Borrowed));
        string outputUri = string.IsNullOrWhiteSpace(fragment)
            ? uri
            : uri + "#" + fragment.Trim();
        return Track(_system.LoadFrom<TInput, TOutput>(outputUri));
    }

    private string Qualify(string localKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(localKey);
        return $"scope://{Uri.EscapeDataString(OwnerId)}/{Uri.EscapeDataString(localKey)}";
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
    private Func<CancellationToken, ValueTask>? _deferredIdleHookAsync;

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

    /// <summary>trimmed/AOT runtime向けにreflectionを使わずStepを追加登録する。</summary>
    public void AddStep<TIn, TOut>(IResourceStep<TIn, TOut> step) => _pipeline.AddStep(step);

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

    internal IDisposable SubscribeState<T>(ResourceHandle<T> handle, Action<ResourceState> callback)
    {
        ArgumentNullException.ThrowIfNull(handle);
        ArgumentNullException.ThrowIfNull(callback);
        ThrowIfDisposed();
        if (!ReferenceEquals(handle.System, this))
            throw new InvalidOperationException("Resource handle belongs to another ResourceSystem.");
        var subscription = new ResourceStateSubscription(this, handle.Node, callback);
        lock (_lock)
        {
            if (handle.Node.IsEvicted) throw new ObjectDisposedException(nameof(handle));
            handle.Node.StateSubscriptions.Add(subscription);
        }
        return subscription;
    }

    internal void RemoveStateSubscription(ResourceStateSubscription subscription)
    {
        lock (_lock) subscription.Node.StateSubscriptions.Remove(subscription);
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

    /// <summary>
    /// Pump 時に deferred resource を実際に破棄する前に呼ばれる idle hook を設定する。
    /// <paramref name="asyncHook"/> を指定すると <see cref="PumpAsync"/> は同期 block を避けてそれを await する。
    /// </summary>
    public void SetDeferredDisposeIdleHook(
        Action hook,
        Func<CancellationToken, ValueTask>? asyncHook = null)
    {
        ArgumentNullException.ThrowIfNull(hook);
        _deferredIdleHook = hook;
        _deferredIdleHookAsync = asyncHook;
    }

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

    internal ResourceHandle<TOutput> LoadFrom<TInput, TOutput>(string uri)
    {
        ThrowIfDisposed();
        ResourceNode node = GetOrCreate(
            typeof(TOutput), new ResourceUri(uri), explicitLoader: null, ownership: null,
            preferredInput: typeof(TInput));
        Interlocked.Increment(ref node.RefCount);
        return new ResourceHandle<TOutput>(this, node);
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

    internal async Task<U> RequireDependency<U>(
        ResourceHandle<U> dependency,
        ResourceNode owner,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dependency);
        if (!ReferenceEquals(dependency.System, this))
            throw new InvalidOperationException("Resource dependency handle belongs to another ResourceSystem.");
        if (dependency.Node.IsEvicted)
            throw new ObjectDisposedException(nameof(dependency));
        AddEdge(owner, dependency.Node);
        Task<object> computed;
        lock (dependency.Node) computed = dependency.Node.Computed;
        await computed.WaitAsync(cancellationToken).ConfigureAwait(false);
        return dependency.Node.Value is U value
            ? value
            : throw new InvalidOperationException($"Resource dependency '{dependency.Uri}' completed without a {typeof(U).Name} value.");
    }

    private static Func<LoadContext, Task<object>> Wrap<T>(Loader<T> loader)
        => async ctx => (object)(await loader(ctx))!;

    private ResourceNode GetOrCreate(
        Type type,
        ResourceUri uri,
        Func<LoadContext, Task<object>>? explicitLoader,
        ResourceOwnership? ownership,
        Type? preferredInput = null)
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
            else node.Loader = Compose(type, uri, node, preferredInput);
            _cache[key] = node;
            _graphDirty = true;
            if (_autoReload) RegisterWatch(node);
            StartLoad(node, isReload: false);
            return node;
        }
    }

    private Func<LoadContext, Task<object>> Compose(
        Type type, ResourceUri uri, ResourceNode node, Type? preferredInput = null)
    {
        if (type == typeof(byte[]))
        {
            IResourceSource src = _pipeline.Source(uri.Scheme)
                ?? throw new InvalidOperationException($"スキーム '{uri.Scheme}' のソース未登録 ({uri})。");
            node.StepName = "source:" + src.GetType().Name;
            node.StepExecutor = Executor.Io;
            return async ctx => { await ctx.Io; return (object)await src.ReadAsync(uri, ctx); };
        }
        StepAdapter step = _pipeline.Select(type, uri.Extension, uri.Fragment, preferredInput)
            ?? throw new InvalidOperationException(
                $"型 {type.Name} を生成するステップ未登録 (input={preferredInput?.Name ?? "auto"}, uri={uri}, ext={uri.Extension}, frag={uri.Fragment})。");
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
            if (isReload) QueueState(node, generation, Snapshot(node));
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
                    QueueState(node, generation, Snapshot(node));
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
                    QueueState(node, generation, Snapshot(node));
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

    private static ResourceState Snapshot(ResourceNode node)
        => new(node.Status, node.HasValue, node.Version, node.Error);

    private void QueueState(ResourceNode node, long generation, ResourceState state)
        => _publishQueue.Enqueue(() =>
        {
            lock (node)
            {
                if (node.IsEvicted || node.LoadGeneration != generation) return;
            }
            FireState(node, state);
        });

    private void FireState(ResourceNode node, ResourceState state)
    {
        ResourceStateSubscription[] subscriptions;
        lock (_lock)
            subscriptions = node.StateSubscriptions.Where(s => !s.IsDisposed).ToArray();
        foreach (ResourceStateSubscription subscription in subscriptions)
            subscription.Callback(state);
    }

    public void Pump()
    {
        PumpCore();
        FlushDeferredDisposals();
        EmitGraphIfDirty();
    }

    /// <summary>
    /// <see cref="Pump"/> と同じ publication/reload 処理を行い、deferred resource の idle hook を非同期に await する。
    /// browser など同期的に queue idle を待てない host はこの overload を使う。
    /// </summary>
    public async ValueTask PumpAsync(CancellationToken cancellationToken = default)
    {
        PumpCore();
        await FlushDeferredDisposalsAsync(cancellationToken).ConfigureAwait(false);
        EmitGraphIfDirty();
    }

    private void PumpCore()
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
                    FireState(registration.Node, Snapshot(registration.Node));
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
    }

    private void FlushDeferredDisposals()
    {
        object[] deferred = TakeDeferredDisposals();
        if (deferred.Length == 0) return;
        try
        {
            _deferredIdleHook?.Invoke();
            DisposeAll(deferred);
        }
        catch
        {
            RestoreDeferredDisposals(deferred);
            throw;
        }
    }

    private async ValueTask FlushDeferredDisposalsAsync(CancellationToken cancellationToken)
    {
        object[] deferred = TakeDeferredDisposals();
        if (deferred.Length == 0) return;
        try
        {
            if (_deferredIdleHookAsync is { } asyncHook)
                await asyncHook(cancellationToken).ConfigureAwait(false);
            else
                _deferredIdleHook?.Invoke();
            DisposeAll(deferred);
        }
        catch
        {
            RestoreDeferredDisposals(deferred);
            throw;
        }
    }

    private object[] TakeDeferredDisposals()
    {
        lock (_lock)
        {
            if (_deferredDispose.Count == 0) return [];
            object[] deferred = _deferredDispose.ToArray();
            _deferredDispose.Clear();
            return deferred;
        }
    }

    private void QueueDeferredDispose(object value)
    {
        lock (_lock) _deferredDispose.Add(value);
    }

    private void RestoreDeferredDisposals(object[] deferred)
    {
        lock (_lock) _deferredDispose.InsertRange(0, deferred);
    }

    private static void DisposeAll(IEnumerable<object> values)
    {
        foreach (object value in values) (value as IDisposable)?.Dispose();
    }

    private void EmitGraphIfDirty()
    {
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
            node.Status = ResourceStatus.Ready;
            node.Error = null;
            node.Version++;
        }
        if (old != null && !ReferenceEquals(old, newValue) && node.Ownership == ResourceOwnership.Owned)
            QueueDeferredDispose(old);
        node.FireReloaded();
        FireState(node, Snapshot(node));
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
        node.StateSubscriptions.Clear();
        _graphDirty = true;
        node.WatchToken?.Dispose();
        try { node.Cts?.Cancel(); } catch { }
        if (value != null && node.Ownership == ResourceOwnership.Owned) QueueDeferredDispose(value);
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
                n.StateSubscriptions.Clear();
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
