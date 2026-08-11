using System.Collections.Concurrent;
using Luxel.Resources;

namespace Luxel.AssetsGpu;

public sealed class GpuResourceManagerOptions
{
    public long SoftBudgetBytes { get; set; } = long.MaxValue;
    public long HardBudgetBytes { get; set; } = long.MaxValue;
}

public readonly record struct GpuResourceGeneration(GpuDevice Device, GpuDeviceGeneration Identity);

public readonly record struct GpuResourceRetirementContext(
    GpuResourceGeneration Generation,
    ResourceRetireReason Reason,
    CancellationToken CancellationToken);

public readonly record struct GpuResourceRelocationContext(
    GpuResourceGeneration Generation,
    CancellationToken CancellationToken);

public readonly record struct GpuResourceRelocationResult(
    long MovedBytes,
    long ReclaimedBytes,
    bool Relocated);

public readonly record struct GpuResourceManagerSnapshot(
    ResourceManagerId Id,
    GpuDeviceGeneration Device,
    bool IsPaused,
    long ResourceCount,
    long OwnedCount,
    long BorrowedCount,
    long LogicalBytes,
    long CommittedBytes,
    long ResidentBytes,
    long PeakResidentBytes,
    long SoftBudgetBytes,
    long HardBudgetBytes,
    long SoftBudgetExceededCount,
    int PendingRetirements,
    int IndexCapacity,
    int IndexUsed,
    int IndexPendingRecycle,
    long CompactionCount,
    long MovedBytes,
    long ReclaimedBytes,
    long RecoveryCount);

internal interface IGpuResourcePolicy
{
    Type ResourceType { get; }
    ResourceOwnership? Ownership { get; }
    ResourceAllocationInfo? DescribeAllocation(object value, GpuResourceGeneration generation, string allocationId);
    IReadOnlyList<string> IndexSpaces { get; }
    ValueTask RetireAsync(object value, GpuResourceRetirementContext context);
    ValueTask FlushAsync(object value, CancellationToken cancellationToken);
    ValueTask<GpuResourceRelocationResult> RelocateAsync(object value, GpuResourceRelocationContext context);
    bool IsRelocatable { get; }
    ulong? GetDeviceGeneration(object value);
}

internal sealed class GpuResourcePolicy<T> : IGpuResourcePolicy
{
    public ResourceOwnership? Ownership { get; set; }
    public Func<T, GpuResourceGeneration, string, ResourceAllocationInfo?>? Allocation { get; set; }
    public Func<T, GpuResourceRetirementContext, ValueTask>? Retirement { get; set; }
    public Func<T, CancellationToken, ValueTask>? Flush { get; set; }
    public Func<T, GpuResourceRelocationContext, ValueTask<GpuResourceRelocationResult>>? Relocation { get; set; }
    public Func<T, ulong?>? DeviceGeneration { get; set; }
    public List<string> MutableIndexSpaces { get; } = [];

    public Type ResourceType => typeof(T);
    public IReadOnlyList<string> IndexSpaces => MutableIndexSpaces;
    public bool IsRelocatable => Relocation is not null;
    public ResourceAllocationInfo? DescribeAllocation(object value, GpuResourceGeneration generation, string allocationId)
        => Allocation?.Invoke((T)value, generation, allocationId);
    public ValueTask RetireAsync(object value, GpuResourceRetirementContext context)
        => Retirement?.Invoke((T)value, context) ?? ValueTask.CompletedTask;
    public ValueTask FlushAsync(object value, CancellationToken cancellationToken)
        => Flush?.Invoke((T)value, cancellationToken) ?? ValueTask.CompletedTask;
    public ValueTask<GpuResourceRelocationResult> RelocateAsync(object value, GpuResourceRelocationContext context)
        => Relocation?.Invoke((T)value, context) ?? ValueTask.FromResult(default(GpuResourceRelocationResult));
    public ulong? GetDeviceGeneration(object value) => DeviceGeneration?.Invoke((T)value);
}

public sealed class GpuResourcePolicyBuilder<T>
{
    private readonly ResourceSystemBuilder _builder;
    private readonly GpuResourceManagerHandle _installation;
    private readonly GpuResourcePolicy<T> _policy = new();

    internal GpuResourcePolicyBuilder(ResourceSystemBuilder builder, GpuResourceManagerHandle installation)
    {
        _builder = builder;
        _installation = installation;
    }

    public GpuResourcePolicyBuilder<T> Owned() { _policy.Ownership = ResourceOwnership.Owned; return this; }
    public GpuResourcePolicyBuilder<T> Borrowed() { _policy.Ownership = ResourceOwnership.Borrowed; return this; }
    public GpuResourcePolicyBuilder<T> DescribeAllocation(Func<T, GpuResourceGeneration, ResourceAllocationInfo?> describe)
    {
        ArgumentNullException.ThrowIfNull(describe);
        _policy.Allocation = (value, generation, _) => describe(value, generation);
        return this;
    }
    public GpuResourcePolicyBuilder<T> DescribeAllocation(Func<T, ResourceAllocationInfo?> describe)
    {
        ArgumentNullException.ThrowIfNull(describe);
        _policy.Allocation = (value, _, _) => describe(value);
        return this;
    }
    public GpuResourcePolicyBuilder<T> RetireAsync(Func<T, GpuResourceRetirementContext, ValueTask> retire)
    { _policy.Retirement = retire ?? throw new ArgumentNullException(nameof(retire)); return this; }
    public GpuResourcePolicyBuilder<T> FlushAsync(Func<T, CancellationToken, ValueTask> flush)
    { _policy.Flush = flush ?? throw new ArgumentNullException(nameof(flush)); return this; }
    public GpuResourcePolicyBuilder<T> RelocateAsync(Func<T, GpuResourceRelocationContext, ValueTask<GpuResourceRelocationResult>> relocate)
    { _policy.Relocation = relocate ?? throw new ArgumentNullException(nameof(relocate)); return this; }
    public GpuResourcePolicyBuilder<T> ValidateDeviceGeneration(Func<T, ulong?> generation)
    { _policy.DeviceGeneration = generation ?? throw new ArgumentNullException(nameof(generation)); return this; }
    public GpuResourcePolicyBuilder<T> WithIndexSpace(string indexSpaceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(indexSpaceId);
        _policy.MutableIndexSpaces.Add(indexSpaceId.Trim());
        return this;
    }

    public void Register()
    {
        _installation.RegisterPolicy(_policy);
        _builder.Managers.Manage<T>().With(_installation.Manager).Register();
    }
}

public sealed class GpuResourceManagerHandle
{
    private readonly Dictionary<Type, IGpuResourcePolicy> _policies = [];
    private GpuResourceManager? _instance;
    private GpuResourceExecutionDomain? _domainInstance;

    internal GpuResourceManagerHandle(ResourceExecutionDomainHandle domain, ResourceManagerHandle manager,
        AssetGpuRegistry registry, GpuResourceGeneration initialGeneration, GpuResourceManagerOptions options)
    {
        CreateDomain = domain;
        Manager = manager;
        Registry = registry;
        InitialGeneration = initialGeneration;
        Options = options;
    }

    public ResourceExecutionDomainHandle CreateDomain { get; }
    public ResourceManagerHandle Manager { get; }
    public AssetGpuRegistry Registry { get; }
    public GpuResourceGeneration InitialGeneration { get; }
    internal GpuResourceManagerOptions Options { get; }
    public GpuResourceManager ManagerInstance => _instance ?? throw new InvalidOperationException("The ResourceSystem has not been built yet.");
    public GpuResourceExecutionDomain DomainInstance => _domainInstance ?? throw new InvalidOperationException("The ResourceSystem has not been built yet.");
    internal IReadOnlyDictionary<Type, IGpuResourcePolicy> Policies => _policies;
    internal void Attach(GpuResourceManager manager) => _instance = manager;
    internal void Attach(GpuResourceExecutionDomain domain) => _domainInstance = domain;
    internal string? Validate(Type type) => _policies.ContainsKey(type) ? null : $"no typed GPU resource policy is registered for '{type}'.";
    internal void RegisterPolicy(IGpuResourcePolicy policy)
    {
        if (!_policies.TryAdd(policy.ResourceType, policy))
            throw new InvalidOperationException($"A GPU resource policy for '{policy.ResourceType}' is already registered.");
    }
    public GpuResourcePolicyBuilder<T> Manage<T>(ResourceSystemBuilder builder) => new(builder, this);
}

internal sealed class GpuIndexSpace
{
    private readonly Stack<int> _free = new();
    private int _next;
    public GpuIndexSpace(string id) => Id = id;
    public string Id { get; }
    public int Used { get; private set; }
    public int Capacity => _next;
    public int Allocate() { Used++; return _free.TryPop(out int index) ? index : _next++; }
    public void Recycle(int index) { _free.Push(index); Used--; }
}

public sealed class GpuResourceManager : IResourceManager
{
    private sealed record Entry(object Value, IGpuResourcePolicy Policy, ResourceManagementRecord Record,
        GpuResourceGeneration Generation, string AllocationKey);
    private sealed record Retirement(Entry Entry, ResourceRetireReason Reason);

    private readonly object _gate = new();
    private readonly IReadOnlyDictionary<Type, IGpuResourcePolicy> _policies;
    private readonly AssetGpuRegistry _registry;
    private readonly Dictionary<string, Entry> _entries = [];
    private readonly Queue<Retirement> _retirements = new();
    private readonly Dictionary<(string Id, ulong Generation), GpuIndexSpace> _indexSpaces = [];
    private readonly GpuResourceManagerOptions _options;
    private GpuResourceGeneration _generation;
    private bool _paused;
    private bool _disposed;
    private long _adopted, _retired, _logical, _committed, _resident, _peakResident;
    private long _owned, _borrowed, _softExceeded, _compactions, _moved, _reclaimed, _recoveries;

    internal GpuResourceManager(ResourceManagerId id, GpuResourceGeneration generation,
        IReadOnlyDictionary<Type, IGpuResourcePolicy> policies, GpuResourceManagerOptions options,
        AssetGpuRegistry registry)
    {
        Id = id;
        _generation = generation;
        _policies = policies;
        _options = options;
        _registry = registry;
    }

    public ResourceManagerId Id { get; }
    public GpuResourceGeneration CurrentGeneration { get { lock (_gate) return _generation; } }
    public bool IsPaused { get { lock (_gate) return _paused; } }
    public ResourceManagerCapabilities Capabilities => ResourceManagerCapabilities.AllocationAccounting |
        ResourceManagerCapabilities.AsyncRetirement | ResourceManagerCapabilities.Pump |
        ResourceManagerCapabilities.Compaction | ResourceManagerCapabilities.Indexes;

    public ValueTask<ResourceManagementRecord> AdoptAsync(object value, ResourceAdoptionContext context)
    {
        ArgumentNullException.ThrowIfNull(value);
        Entry entry;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_paused) throw new InvalidOperationException($"GPU resource manager '{Id}' is paused for device recovery.");
            if (!_policies.TryGetValue(context.Type, out IGpuResourcePolicy? policy))
                throw new InvalidOperationException($"No typed GPU resource policy is registered for '{context.Type}'.");
            if (policy.GetDeviceGeneration(value) is ulong actual && actual != _generation.Identity.Generation)
                throw new InvalidOperationException($"GPU resource '{context.Type}' belongs to device generation {actual}, current generation is {_generation.Identity.Generation}.");

            string key = $"{Id.Value}:{_generation.Identity.Generation}:{Guid.NewGuid():N}";
            ResourceAllocationInfo? described = policy.DescribeAllocation(value, _generation, key);
            ResourceAllocationInfo allocation = described is null
                ? new(key, 0, 0, 0, "gpu", DeviceGeneration: (long)_generation.Identity.Generation)
                : described.Value with { AllocationId = string.IsNullOrWhiteSpace(described.Value.AllocationId) ? key : described.Value.AllocationId,
                    DeviceGeneration = described.Value.DeviceGeneration == 0 ? (long)_generation.Identity.Generation : described.Value.DeviceGeneration };
            long nextResident = checked(_resident + allocation.ResidentSize);
            if (nextResident > _options.HardBudgetBytes)
                throw new InvalidOperationException($"GPU manager '{Id}' hard budget of {_options.HardBudgetBytes} bytes would be exceeded by '{context.Type}'.");
            if (nextResident > _options.SoftBudgetBytes) _softExceeded++;

            var indexes = new List<ResourceIndexToken>(policy.IndexSpaces.Count);
            foreach (string spaceId in policy.IndexSpaces)
            {
                var spaceKey = (spaceId, _generation.Identity.Generation);
                if (!_indexSpaces.TryGetValue(spaceKey, out GpuIndexSpace? space)) _indexSpaces.Add(spaceKey, space = new(spaceId));
                indexes.Add(new(spaceId, space.Allocate(), Id, (long)_generation.Identity.Generation));
            }
            ResourceOwnership ownership = policy.Ownership ?? context.Ownership;
            var record = new ResourceManagementRecord(Id, ownership, allocation, new(indexes), context.ManagementContext);
            entry = new(value, policy, record, _generation, allocation.AllocationId);
            _entries.Add(entry.AllocationKey, entry);
            _adopted++; _logical += allocation.LogicalSize; _committed += allocation.CommittedSize;
            _resident = nextResident; _peakResident = Math.Max(_peakResident, _resident);
            if (ownership == ResourceOwnership.Owned) _owned++; else _borrowed++;
        }
        return ValueTask.FromResult(entry.Record);
    }

    public ValueTask RetireAsync(object value, ResourceManagementRecord record, ResourceRetireReason reason,
        CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (record.Allocation is not { } allocation || !_entries.Remove(allocation.AllocationId, out Entry? entry))
                return ValueTask.CompletedTask;
            _retirements.Enqueue(new(entry, reason));
        }
        return ValueTask.CompletedTask;
    }

    public async ValueTask PumpAsync(ResourceManagerPumpContext context)
    {
        Retirement[] pending;
        Entry[] active;
        lock (_gate)
        {
            pending = _retirements.ToArray();
            _retirements.Clear();
            active = _entries.Values.ToArray();
        }
        foreach (Entry entry in active) await entry.Policy.FlushAsync(entry.Value, context.CancellationToken).ConfigureAwait(false);
        if (pending.Length == 0) return;
        await WaitGenerationIdleAsync(pending.Select(item => item.Entry.Generation).Distinct(), context.CancellationToken).ConfigureAwait(false);
        foreach (Retirement retirement in pending) await CompleteRetirementAsync(retirement, context.CancellationToken).ConfigureAwait(false);
    }

    public async ValueTask PauseAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate) _paused = true;
        await _generation.Device.MainQueue.WaitIdleAsync(cancellationToken).ConfigureAwait(false);
    }

    public void ActivateGeneration(GpuResourceGeneration generation)
    {
        lock (_gate)
        {
            if (!_paused) throw new InvalidOperationException("Pause the GPU manager before activating a replacement generation.");
            if (generation.Identity.DeviceId != _generation.Identity.DeviceId)
                throw new InvalidOperationException("A replacement generation must retain the stable GPU device id.");
            if (generation.Identity.Generation <= _generation.Identity.Generation)
                throw new InvalidOperationException("A replacement GPU generation must be newer than the current generation.");
            _generation = generation;
            _recoveries++;
        }
    }

    public void Resume() { lock (_gate) { ObjectDisposedException.ThrowIf(_disposed, this); _paused = false; } }

    public async ValueTask<GpuResourceRelocationResult> CompactAsync(CancellationToken cancellationToken = default)
    {
        Entry[] entries;
        GpuResourceGeneration generation;
        lock (_gate) { entries = _entries.Values.Where(e => e.Policy.IsRelocatable).ToArray(); generation = _generation; }
        long moved = 0, reclaimed = 0; bool relocated = false;
        foreach (Entry entry in entries)
        {
            GpuResourceRelocationResult result = await entry.Policy.RelocateAsync(entry.Value, new(generation, cancellationToken)).ConfigureAwait(false);
            moved += result.MovedBytes; reclaimed += result.ReclaimedBytes; relocated |= result.Relocated;
        }
        lock (_gate) { _compactions++; _moved += moved; _reclaimed += reclaimed; }
        return new(moved, reclaimed, relocated);
    }

    public ResourceManagerSnapshot CaptureSnapshot()
    {
        lock (_gate) return new(Id, _adopted, _retired, _logical, _retirements.Count);
    }

    public GpuResourceManagerSnapshot CaptureGpuSnapshot()
    {
        lock (_gate)
        {
            return new(Id, _generation.Identity, _paused, _entries.Count, _owned, _borrowed,
                _logical, _committed, _resident, _peakResident, _options.SoftBudgetBytes, _options.HardBudgetBytes,
                _softExceeded, _retirements.Count, _indexSpaces.Values.Sum(s => s.Capacity),
                _indexSpaces.Values.Sum(s => s.Used), _retirements.Sum(r => r.Entry.Record.Indexes?.Values.Count ?? 0),
                _compactions, _moved, _reclaimed, _recoveries);
        }
    }

    private static async ValueTask WaitGenerationIdleAsync(IEnumerable<GpuResourceGeneration> generations, CancellationToken cancellationToken)
    {
        foreach (GpuResourceGeneration generation in generations)
            await generation.Device.MainQueue.WaitIdleAsync(cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask CompleteRetirementAsync(Retirement retirement, CancellationToken cancellationToken)
    {
        Entry entry = retirement.Entry;
        if (entry.Record.Ownership == ResourceOwnership.Owned)
            await entry.Policy.RetireAsync(entry.Value, new(entry.Generation, retirement.Reason, cancellationToken)).ConfigureAwait(false);
        lock (_gate)
        {
            if (entry.Record.Indexes is { } indexes)
                foreach (ResourceIndexToken token in indexes.Values)
                    if (token.DeviceGeneration == (long)entry.Generation.Identity.Generation &&
                        _indexSpaces.TryGetValue((token.IndexSpaceId, entry.Generation.Identity.Generation), out GpuIndexSpace? space))
                        space.Recycle(token.Index);
            ResourceAllocationInfo allocation = entry.Record.Allocation!.Value;
            _logical -= allocation.LogicalSize; _committed -= allocation.CommittedSize; _resident -= allocation.ResidentSize;
            if (entry.Record.Ownership == ResourceOwnership.Owned) _owned--; else _borrowed--;
            _retired++;
        }
    }

    public async ValueTask ShutdownAsync(CancellationToken cancellationToken = default)
    {
        Entry[] active;
        lock (_gate)
        {
            if (_disposed) return;
            _paused = true;
            active = _entries.Values.ToArray();
            _entries.Clear();
            foreach (Entry entry in active) _retirements.Enqueue(new(entry, ResourceRetireReason.Shutdown));
        }
        await PumpAsync(new(cancellationToken)).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        await ShutdownAsync().ConfigureAwait(false);
        GpuResourceGeneration generation;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            generation = _generation;
        }
        await generation.Device.MainQueue.WaitIdleAsync().ConfigureAwait(false);
        _registry.Dispose();
    }
}
