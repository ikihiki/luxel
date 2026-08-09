using System.Collections.Concurrent;
using Luxel.Diagnostics;

namespace Luxel.Resources;

public readonly record struct ResourceState(ResourceStatus Status, bool HasValue, int Version, Exception? Error);

internal sealed class ResourceNode
{
    public required Type Type;
    public required ResourceUri Uri;
    public required string Key;
    public required Func<LoadContext, Task<object>> Loader;
    public required IResourceManager Manager;
    public required ResourceExecutionDomainId ExecutionDomainId;
    public ResourceOwnership Ownership;
    public volatile object? Value;
    public ManagedResourceGeneration? CurrentGeneration;
    public int Version;
    public ResourceStatus Status = ResourceStatus.Loading;
    public Task<object> Computed = null!;
    public Exception? Error;
    public bool HasValue;
    public long LoadGeneration;
    public int ReloadQueued;
    public int RefCount;
    public readonly HashSet<ResourceNode> Dependents = [];
    public readonly HashSet<ResourceNode> Dependencies = [];
    public IReloadToken? WatchToken;
    public CancellationTokenSource? Cts;
    public string StepName = "?";
    public event Action? Reloaded;
    public void FireReloaded() => Reloaded?.Invoke();
    public readonly List<ResourceStateSubscription> StateSubscriptions = [];
    public bool IsEvicted;
}

public sealed class ResourceHandle<T> : IDisposable
{
    private readonly ResourceSystem _system;
    internal readonly ResourceNode Node;
    private int _disposed;
    internal ResourceHandle(ResourceSystem system, ResourceNode node) { _system = system; Node = node; }
    internal ResourceSystem System => _system;
    public T Value => Node.Value is T value ? value : default!;
    public bool IsReady => Node.Status == ResourceStatus.Ready;
    public ResourceStatus Status => Node.Status;
    public ResourceUri Uri => Node.Uri;
    public Task Ready => Node.Computed;
    public int Version => Node.Version;
    public bool HasValue => Node.HasValue;
    public Exception? LastReloadError => Node.Error;
    public Exception? Error => Node.Error;
    public ResourceState State => new(Node.Status, Node.HasValue, Node.Version, Node.Error);
    public event Action Reloaded { add => Node.Reloaded += value; remove => Node.Reloaded -= value; }
    public IDisposable SubscribeState(Action<ResourceState> callback) => _system.SubscribeState(this, callback);
    public void Dispose() { if (Interlocked.Exchange(ref _disposed, 1) == 0) _system.Release(Node); }
}

internal sealed class ResourceStateSubscription : IDisposable
{
    private readonly ResourceSystem _system;
    internal readonly ResourceNode Node;
    internal readonly Action<ResourceState> Callback;
    private int _disposed;
    internal ResourceStateSubscription(ResourceSystem system, ResourceNode node, Action<ResourceState> callback) { _system = system; Node = node; Callback = callback; }
    internal bool IsDisposed => Volatile.Read(ref _disposed) != 0;
    public void Dispose() { if (Interlocked.Exchange(ref _disposed, 1) == 0) _system.RemoveStateSubscription(this); }
}

public sealed class LoadContext
{
    private readonly ResourceSystem _system;
    internal readonly ResourceNode Owner;
    private readonly CancellationToken _token;
    public ResourceUri Uri { get; }
    internal LoadContext(ResourceSystem system, ResourceNode owner, ResourceUri uri, CancellationToken token) { _system = system; Owner = owner; Uri = uri; _token = token; }
    public CancellationToken Token => _token;

    public async ValueTask DispatchAsync(ResourceExecutionDomainHandle domain, Func<CancellationToken, ValueTask> work)
    {
        ArgumentNullException.ThrowIfNull(work);
        await _system.DispatchAsync(domain, async token => { await work(token).ConfigureAwait(false); return (object)true; }, _token).ConfigureAwait(false);
    }

    public async ValueTask<T> DispatchAsync<T>(ResourceExecutionDomainHandle domain, Func<CancellationToken, ValueTask<T>> work)
    {
        ArgumentNullException.ThrowIfNull(work);
        object result = await _system.DispatchAsync(domain, async token => (object)(await work(token).ConfigureAwait(false))!, _token).ConfigureAwait(false);
        return (T)result;
    }

    public async ValueTask YieldAsync() => await Task.Yield();
    public ResourceHandle<U> Load<U>(string uri) => _system.LoadDependency<U>(uri, Owner);
    public ResourceHandle<U> Load<U>(string uri, Loader<U> loader) => _system.LoadDependency(uri, loader, Owner);
    public ResourceHandle<U> LoadRelative<U>(string relativeUri) => _system.LoadDependency<U>(Uri.Resolve(relativeUri).Url, Owner);
    public ResourceHandle<U> LoadRelative<U>(string relativeUri, Loader<U> loader) => _system.LoadDependency(Uri.Resolve(relativeUri).Url, loader, Owner);
    public Task<U> Require<U>(ResourceHandle<U> dependency) => _system.RequireDependency(dependency, Owner, _token);
}

public sealed class ResourceScope : IDisposable
{
    private readonly ResourceSystem _system;
    private readonly object _gate = new();
    private List<IDisposable>? _leases = [];
    internal ResourceScope(ResourceSystem system, string ownerId) { _system = system; OwnerId = ownerId; }
    public string OwnerId { get; }
    public ResourceHandle<T> Load<T>(string uri) => Track(_system.Load<T>(uri));
    public ResourceHandle<T> Create<T>(string localKey, Loader<T> loader) => Create(localKey, loader, ResourceOwnership.Owned);
    public ResourceHandle<T> Create<T>(string localKey, Loader<T> loader, ResourceOwnership ownership)
        => Track(_system.Load(Qualify(localKey), loader, ownership));
    public ResourceHandle<TOutput> Create<TInput, TOutput>(string localKey, TInput input) where TInput : class
        => Create<TInput, TOutput>(localKey, input, null);
    public ResourceHandle<TOutput> Create<TInput, TOutput>(string localKey, TInput input, string? fragment) where TInput : class
    {
        ArgumentNullException.ThrowIfNull(input);
        string uri = Qualify(localKey);
        Track(_system.Load(uri, _ => Task.FromResult(input), ResourceOwnership.Borrowed));
        return Track(_system.LoadFrom<TInput, TOutput>(string.IsNullOrWhiteSpace(fragment) ? uri : uri + "#" + fragment.Trim()));
    }
    private string Qualify(string localKey) { ArgumentException.ThrowIfNullOrWhiteSpace(localKey); return $"scope://{Uri.EscapeDataString(OwnerId)}/{Uri.EscapeDataString(localKey)}"; }
    private ResourceHandle<T> Track<T>(ResourceHandle<T> handle)
    {
        lock (_gate)
        {
            if (_leases is null) { handle.Dispose(); throw new ObjectDisposedException(nameof(ResourceScope)); }
            _leases.Add(handle); return handle;
        }
    }
    public void Dispose()
    {
        List<IDisposable>? leases;
        lock (_gate) { leases = _leases; _leases = null; }
        if (leases is not null) foreach (IDisposable lease in leases) lease.Dispose();
    }
}

public sealed class ResourceSystem : IDisposable, IAsyncDisposable
{
    private readonly ResourceDomainTable _domains;
    private readonly ResourceStepTable _steps;
    private readonly ResourceManagerTable _managers;
    private readonly Dictionary<string, ResourceNode> _cache = [];
    private readonly object _gate = new();
    private readonly ConcurrentQueue<ResourceNode> _reloadQueue = new();
    private readonly ConcurrentQueue<Action> _publishQueue = new();
    private readonly ConcurrentQueue<(ManagedResourceGeneration Generation, ResourceRetireReason Reason)> _retireQueue = new();
    private bool _autoReload;
    private volatile bool _disposed;
    private volatile bool _graphDirty;

    internal ResourceSystem(ResourceDomainTable domains, ResourceStepTable steps, ResourceManagerTable managers)
    { _domains = domains; _steps = steps; _managers = managers; }

    internal ValueTask<object> DispatchAsync(ResourceExecutionDomainHandle domain, Func<CancellationToken, ValueTask<object>> work, CancellationToken token)
        => _domains.Get(domain).DispatchAsync(work, token);

    public ResourceExecutionDomainSnapshot[] CaptureDomainSnapshots() => _domains.CaptureSnapshots();
    public ResourceManagerSnapshot[] CaptureManagerSnapshots() => _managers.Values.Select(manager => manager.CaptureSnapshot()).ToArray();
    public ResourceScope CreateScope(string ownerId) { ArgumentException.ThrowIfNullOrWhiteSpace(ownerId); ThrowIfDisposed(); return new(this, ownerId); }
    public void Watch() => _autoReload = true;

    public ResourceHandle<T> Load<T>(string uri)
    {
        ThrowIfDisposed();
        ResourceNode node = GetOrCreate(typeof(T), new(uri), null, null);
        Interlocked.Increment(ref node.RefCount);
        return new(this, node);
    }

    public ResourceHandle<T> Load<T>(string uri, Loader<T> loader) => Load(uri, loader, ResourceOwnership.Owned);
    public ResourceHandle<T> Load<T>(string uri, Loader<T> loader, ResourceOwnership ownership)
    {
        ArgumentNullException.ThrowIfNull(loader); ThrowIfDisposed();
        ResourceNode node = GetOrCreate(typeof(T), new(uri), async context => (object)(await loader(context).ConfigureAwait(false))!, ownership);
        Interlocked.Increment(ref node.RefCount); return new(this, node);
    }

    public ResourceHandle<T> Publish<T>(T value) where T : class
        => Publish($"published://{typeof(T).Name}/{System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(value):x8}", value);
    public ResourceHandle<T> Publish<T>(string uri, T value) where T : class => Publish(uri, value, ResourceOwnership.Owned);
    public ResourceHandle<T> Publish<T>(string uri, T value, ResourceOwnership ownership) where T : class
    {
        ThrowIfDisposed();
        ResourceUri parsed = new(uri);
        IResourceManager manager = _managers.Resolve(typeof(T));
        long generation = 1;
        ResourceManagementRecord record = manager.AdoptAsync(value, new(typeof(T), parsed, generation, ownership, default, CancellationToken.None)).AsTask().GetAwaiter().GetResult();
        string key = CacheKey(typeof(T), parsed);
        lock (_gate)
        {
            if (_cache.ContainsKey(key)) throw new InvalidOperationException($"Already registered: {uri}");
            var node = new ResourceNode
            {
                Type = typeof(T), Uri = parsed, Key = key, Manager = manager, Ownership = ownership,
                ExecutionDomainId = _managers.DefaultDomain(manager.Id).Id, StepName = "publish",
                Loader = _ => Task.FromResult<object>(value), Value = value, HasValue = true, Status = ResourceStatus.Ready,
                Computed = Task.FromResult<object>(value), LoadGeneration = generation,
                CurrentGeneration = new(generation, value, record), RefCount = 1,
            };
            _cache.Add(key, node); _graphDirty = true; return new(this, node);
        }
    }

    public void Republish<T>(string uri, T value) where T : class
    {
        ResourceNode node = FindNode(typeof(T), new(uri));
        long generation;
        lock (node) generation = ++node.LoadGeneration;
        ResourceManagementRecord record = node.Manager.AdoptAsync(value, new(typeof(T), node.Uri, generation, node.Ownership, default, CancellationToken.None)).AsTask().GetAwaiter().GetResult();
        _publishQueue.Enqueue(() => NotifyPublish(node, new(generation, value, record)));
    }

    internal ResourceHandle<TOutput> LoadFrom<TInput, TOutput>(string uri)
    {
        ResourceNode node = GetOrCreate(typeof(TOutput), new(uri), null, null, typeof(TInput));
        Interlocked.Increment(ref node.RefCount); return new(this, node);
    }

    internal ResourceHandle<U> LoadDependency<U>(string uri, ResourceNode owner)
    {
        ResourceNode node = GetOrCreate(typeof(U), new(uri), null, null); AddEdge(owner, node);
        Interlocked.Increment(ref node.RefCount); return new(this, node);
    }
    internal ResourceHandle<U> LoadDependency<U>(string uri, Loader<U> loader, ResourceNode owner)
    {
        ResourceNode node = GetOrCreate(typeof(U), new(uri), async context => (object)(await loader(context).ConfigureAwait(false))!, ResourceOwnership.Owned);
        AddEdge(owner, node); Interlocked.Increment(ref node.RefCount); return new(this, node);
    }

    internal async Task<U> RequireDependency<U>(ResourceHandle<U> dependency, ResourceNode owner, CancellationToken cancellationToken)
    {
        if (!ReferenceEquals(dependency.System, this)) throw new InvalidOperationException("Resource dependency handle belongs to another ResourceSystem.");
        AddEdge(owner, dependency.Node);
        Task<object> computed; lock (dependency.Node) computed = dependency.Node.Computed;
        await computed.WaitAsync(cancellationToken).ConfigureAwait(false);
        return dependency.Node.Value is U value ? value : throw new InvalidOperationException($"Resource dependency '{dependency.Uri}' completed without a {typeof(U).Name} value.");
    }

    private ResourceNode GetOrCreate(Type type, ResourceUri uri, Func<LoadContext, Task<object>>? explicitLoader,
        ResourceOwnership? ownership, Type? preferredInput = null)
    {
        string key = CacheKey(type, uri);
        ResourceNode node;
        bool created = false;
        lock (_gate)
        {
            if (_cache.TryGetValue(key, out ResourceNode? existing)) return existing;
            node = CreateNode(type, uri, key, explicitLoader, ownership, preferredInput);
            _cache.Add(key, node); _graphDirty = true; created = true;
        }
        if (created)
        {
            if (_autoReload) RegisterWatch(node);
            StartLoad(node, false);
        }
        return node;
    }

    private ResourceNode CreateNode(Type type, ResourceUri uri, string key, Func<LoadContext, Task<object>>? explicitLoader,
        ResourceOwnership? ownership, Type? preferredInput)
    {
        if (explicitLoader is not null)
        {
            IResourceManager manager = _managers.Resolve(type);
            ResourceExecutionDomainHandle domain = _managers.DefaultDomain(manager.Id);
            return NewNode(type, uri, key, explicitLoader, manager, domain.Id, ownership ?? ResourceOwnership.Owned, "loader");
        }
        if (type == typeof(byte[]))
        {
            ResourceSourceDescriptor source = _steps.Source(uri.Scheme) ?? throw new InvalidOperationException($"No source registered for scheme '{uri.Scheme}' ({uri}).");
            IResourceManager manager = _managers.Resolve(type, source.Manager);
            Func<LoadContext, Task<object>> loader = context => DispatchAsync(source.Domain,
                async _ => (object)await source.Source.ReadAsync(uri, context).ConfigureAwait(false), context.Token).AsTask();
            return NewNode(type, uri, key, loader, manager, source.Domain.Id, ResourceOwnership.Owned, "source:" + source.Source.GetType().Name);
        }
        ResourceStepDescriptor step = _steps.Select(type, uri.Extension, uri.Fragment, preferredInput)
            ?? throw new InvalidOperationException($"No step registered to produce '{type}' (input={preferredInput}, uri={uri}).");
        IResourceManager stepManager = _managers.Resolve(type, step.Manager);
        Func<LoadContext, Task<object>> composed = async context =>
        {
            ResourceUri dependencyUri = uri.Fragment.Length > 0 ? uri.WithoutFragment() : uri;
            ResourceNode dependency = GetOrCreateDependency(step.Input, dependencyUri, context.Owner);
            await dependency.Computed.ConfigureAwait(false);
            return await DispatchAsync(step.Domain, token => new(step.Run(dependency.Value!, uri, context)), context.Token).ConfigureAwait(false);
        };
        return NewNode(type, uri, key, composed, stepManager, step.Domain.Id, step.Ownership, step.Name);
    }

    private static ResourceNode NewNode(Type type, ResourceUri uri, string key, Func<LoadContext, Task<object>> loader,
        IResourceManager manager, ResourceExecutionDomainId domain, ResourceOwnership ownership, string name) => new()
    {
        Type = type, Uri = uri, Key = key, Loader = loader, Manager = manager,
        ExecutionDomainId = domain, Ownership = ownership, StepName = name,
    };

    private ResourceNode GetOrCreateDependency(Type type, ResourceUri uri, ResourceNode owner)
    { ResourceNode node = GetOrCreate(type, uri, null, null); AddEdge(owner, node); return node; }
    private void AddEdge(ResourceNode owner, ResourceNode dependency)
    { lock (_gate) { dependency.Dependents.Add(owner); owner.Dependencies.Add(dependency); } }

    private void StartLoad(ResourceNode node, bool reload)
    {
        var cts = new CancellationTokenSource();
        CancellationTokenSource? previous;
        long generation;
        lock (node)
        {
            previous = node.Cts; node.Cts = cts; generation = ++node.LoadGeneration; node.Status = ResourceStatus.Loading;
            node.Computed = RunLoad(node, new(this, node, node.Uri, cts.Token), reload, generation, cts);
            if (reload) QueueState(node, generation, Snapshot(node));
        }
        try { previous?.Cancel(); } catch (ObjectDisposedException) { }
    }

    private async Task<object> RunLoad(ResourceNode node, LoadContext context, bool reload, long generation, CancellationTokenSource cts)
    {
        try
        {
            object value = await node.Loader(context).ConfigureAwait(false);
            ResourceManagementRecord record = await node.Manager.AdoptAsync(value,
                new(node.Type, node.Uri, generation, node.Ownership, default, context.Token)).ConfigureAwait(false);
            var managed = new ManagedResourceGeneration(generation, value, record);
            bool stale;
            lock (node)
            {
                stale = node.LoadGeneration != generation || node.IsEvicted;
                if (!stale)
                {
                    node.Status = ResourceStatus.Ready; node.Error = null;
                    if (reload) _publishQueue.Enqueue(() => NotifyPublish(node, managed));
                    else { node.Value = value; node.CurrentGeneration = managed; node.HasValue = true; QueueState(node, generation, Snapshot(node)); }
                }
            }
            if (stale)
            {
                await node.Manager.RetireAsync(value, record, ResourceRetireReason.StaleCompletion).ConfigureAwait(false);
                return value;
            }
            _graphDirty = true; return value;
        }
        catch (Exception error)
        {
            lock (node) if (node.LoadGeneration == generation)
            { node.Status = node.HasValue ? ResourceStatus.Ready : ResourceStatus.Failed; node.Error = error; QueueState(node, generation, Snapshot(node)); _graphDirty = true; }
            throw;
        }
        finally
        {
            lock (node) if (ReferenceEquals(node.Cts, cts)) node.Cts = null;
            cts.Dispose();
        }
    }

    public void Pump() => PumpAsync().AsTask().GetAwaiter().GetResult();
    public async ValueTask PumpAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        while (_publishQueue.TryDequeue(out Action? publish)) publish();
        while (_reloadQueue.TryDequeue(out ResourceNode? node))
        { Interlocked.Exchange(ref node.ReloadQueued, 0); if (!node.IsEvicted) StartLoad(node, true); }
        while (_retireQueue.TryDequeue(out var retirement))
        {
            IResourceManager manager = _managers.Get(retirement.Generation.Management.ManagerId);
            await manager.RetireAsync(retirement.Generation.Value, retirement.Generation.Management, retirement.Reason, cancellationToken).ConfigureAwait(false);
        }
        foreach (IResourceManager manager in _managers.Values)
            await manager.PumpAsync(new(cancellationToken)).ConfigureAwait(false);
        EmitGraphIfDirty();
    }

    private void NotifyPublish(ResourceNode node, ManagedResourceGeneration next)
    {
        ManagedResourceGeneration? previous;
        lock (node)
        {
            if (node.IsEvicted || node.LoadGeneration != next.Generation)
            { _retireQueue.Enqueue((next, ResourceRetireReason.StaleCompletion)); return; }
            previous = node.CurrentGeneration; node.CurrentGeneration = next; node.Value = next.Value;
            node.HasValue = true; node.Status = ResourceStatus.Ready; node.Error = null; node.Version++;
        }
        if (previous is not null && !ReferenceEquals(previous.Value, next.Value)) _retireQueue.Enqueue((previous, ResourceRetireReason.Replaced));
        node.FireReloaded(); FireState(node, Snapshot(node)); _graphDirty = true;
        ResourceNode[] dependents; lock (_gate) dependents = node.Dependents.ToArray();
        foreach (ResourceNode dependent in dependents) EnqueueReload(dependent);
    }

    public void InvalidateAll() { ThrowIfDisposed(); ResourceNode[] all; lock (_gate) all = _cache.Values.ToArray(); foreach (ResourceNode node in all) EnqueueReload(node); }
    public void InvalidateManager(ResourceManagerId managerId)
    {
        ThrowIfDisposed(); ResourceNode[] nodes; lock (_gate) nodes = _cache.Values.Where(node => node.Manager.Id == managerId).ToArray();
        foreach (ResourceNode node in nodes) EnqueueReload(node);
    }

    private void EnqueueReload(ResourceNode node)
    { if (!node.IsEvicted && Interlocked.CompareExchange(ref node.ReloadQueued, 1, 0) == 0) _reloadQueue.Enqueue(node); }
    private void RegisterWatch(ResourceNode node)
    { if (node.Type == typeof(byte[])) node.WatchToken = _steps.Source(node.Uri.Scheme)?.Source.Watch(node.Uri, () => EnqueueReload(node)); }

    internal IDisposable SubscribeState<T>(ResourceHandle<T> handle, Action<ResourceState> callback)
    {
        if (!ReferenceEquals(handle.System, this)) throw new InvalidOperationException("Resource handle belongs to another ResourceSystem.");
        var subscription = new ResourceStateSubscription(this, handle.Node, callback);
        lock (_gate) { if (handle.Node.IsEvicted) throw new ObjectDisposedException(nameof(handle)); handle.Node.StateSubscriptions.Add(subscription); }
        return subscription;
    }
    internal void RemoveStateSubscription(ResourceStateSubscription subscription) { lock (_gate) subscription.Node.StateSubscriptions.Remove(subscription); }

    internal void Release(ResourceNode node)
    {
        lock (_gate) { if (node.IsEvicted || Interlocked.Decrement(ref node.RefCount) > 0) return; TryEvict(node); }
    }
    private void TryEvict(ResourceNode node)
    {
        if (node.IsEvicted || node.RefCount > 0 || node.Dependents.Count > 0) return;
        ManagedResourceGeneration? generation;
        lock (node) { node.IsEvicted = true; generation = node.CurrentGeneration; node.CurrentGeneration = null; node.Value = null; node.HasValue = false; }
        _cache.Remove(node.Key); node.StateSubscriptions.Clear(); node.WatchToken?.Dispose(); try { node.Cts?.Cancel(); } catch { }
        if (generation is not null) _retireQueue.Enqueue((generation, ResourceRetireReason.Evicted));
        foreach (ResourceNode dependency in node.Dependencies.ToArray())
        { dependency.Dependents.Remove(node); if (dependency.RefCount == 0 && dependency.Dependents.Count == 0) TryEvict(dependency); }
        node.Dependencies.Clear(); _graphDirty = true;
    }

    private ResourceNode FindNode(Type type, ResourceUri uri)
    { lock (_gate) return _cache.TryGetValue(CacheKey(type, uri), out ResourceNode? node) ? node : throw new InvalidOperationException($"Resource is not published: {uri}"); }
    private static string CacheKey(Type type, ResourceUri uri) => type.FullName + "|" + uri.Key;
    private static ResourceState Snapshot(ResourceNode node) => new(node.Status, node.HasValue, node.Version, node.Error);
    private void QueueState(ResourceNode node, long generation, ResourceState state) => _publishQueue.Enqueue(() =>
    { lock (node) if (node.IsEvicted || node.LoadGeneration != generation) return; FireState(node, state); });
    private void FireState(ResourceNode node, ResourceState state)
    { ResourceStateSubscription[] subscriptions; lock (_gate) subscriptions = node.StateSubscriptions.Where(s => !s.IsDisposed).ToArray(); foreach (var subscription in subscriptions) subscription.Callback(state); }

    private void EmitGraphIfDirty()
    {
        if (!_graphDirty || !EngineDiagnostics.IsEnabled(EngineDiagnostics.Resources)) return;
        _graphDirty = false;
        DiagResourceNode[] nodes;
        lock (_gate) nodes = _cache.Values.Select(node => new DiagResourceNode(
            $"{node.Type.Name} {node.Uri}", node.Type.Name, node.Uri.ToString(), node.Status.ToString(), node.Version,
            node.StepName, node.ExecutionDomainId.Value, node.Dependencies.Select(d => $"{d.Type.Name} {d.Uri}").ToArray())).ToArray();
        EngineDiagnostics.Emit(EngineDiagnostics.Resources, new DiagResources(nodes));
    }

    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();
    public async ValueTask DisposeAsync()
    {
        ResourceNode[] all;
        lock (_gate)
        {
            if (_disposed) return; _disposed = true; all = _cache.Values.ToArray(); _cache.Clear();
            foreach (ResourceNode node in all)
            {
                node.IsEvicted = true; node.WatchToken?.Dispose(); try { node.Cts?.Cancel(); } catch { }
                if (node.CurrentGeneration is { } generation) _retireQueue.Enqueue((generation, ResourceRetireReason.Shutdown));
                node.CurrentGeneration = null; node.Value = null; node.StateSubscriptions.Clear();
            }
        }
        while (_retireQueue.TryDequeue(out var retirement))
        {
            try { await _managers.Get(retirement.Generation.Management.ManagerId).RetireAsync(retirement.Generation.Value, retirement.Generation.Management, retirement.Reason).ConfigureAwait(false); }
            catch { }
        }
        foreach (IResourceExecutionDomain domain in _domains.Values.Reverse()) { try { await domain.ShutdownAsync().ConfigureAwait(false); } catch { } }
        foreach (IResourceManager manager in _managers.Values.Reverse()) { try { await manager.ShutdownAsync().ConfigureAwait(false); } catch { } }
        foreach (IResourceExecutionDomain domain in _domains.Values.Reverse()) { try { await domain.DisposeAsync().ConfigureAwait(false); } catch { } }
        foreach (IResourceManager manager in _managers.Values.Reverse()) { try { await manager.DisposeAsync().ConfigureAwait(false); } catch { } }
    }

    private void ThrowIfDisposed() { if (_disposed) throw new ObjectDisposedException(nameof(ResourceSystem)); }
}
