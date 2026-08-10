namespace Luxel.Resources;

public readonly record struct ResourceManagerId
{
    public ResourceManagerId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value.Trim();
    }
    public string Value { get; }
    public override string ToString() => Value;
}

public readonly record struct ResourceManagerHandle(ResourceManagerId Id);
public readonly record struct ResourceManagementContext(string? Qualifier = null);
public readonly record struct ResourceAllocationInfo(
    string AllocationId,
    long LogicalSize,
    long CommittedSize,
    long ResidentSize,
    string MemoryClass = "managed",
    string? ArenaId = null,
    long Alignment = 0,
    bool Relocatable = false,
    bool Pinned = false,
    double FragmentationContribution = 0,
    long DeviceGeneration = 0);
public readonly record struct ResourceIndexToken(string IndexSpaceId, int Index, ResourceManagerId ManagerId, long DeviceGeneration = 0);
public readonly record struct ResourceIndexSet(IReadOnlyList<ResourceIndexToken> Values)
{
    public static ResourceIndexSet Empty { get; } = new(Array.Empty<ResourceIndexToken>());
}

[Flags]
public enum ResourceManagerCapabilities { None = 0, AllocationAccounting = 1, AsyncRetirement = 2, Pump = 4, Compaction = 8, Indexes = 16 }
public enum ResourceRetireReason { Replaced, StaleCompletion, Evicted, Shutdown }

public readonly record struct ResourceManagerBuildContext(ResourceManagerId Id, ResourceExecutionDomainHandle DefaultDomain);
public readonly record struct ResourceAdoptionContext(Type Type, ResourceUri Uri, long Generation, ResourceOwnership Ownership, ResourceManagementContext ManagementContext, CancellationToken CancellationToken);
public readonly record struct ResourceManagerPumpContext(CancellationToken CancellationToken);
public readonly record struct ResourceManagerSnapshot(ResourceManagerId Id, long AdoptedCount, long RetiredCount, long LogicalBytes, int PendingRetirements);

public sealed record ResourceManagementRecord(
    ResourceManagerId ManagerId,
    ResourceOwnership Ownership,
    ResourceAllocationInfo? Allocation = null,
    ResourceIndexSet? Indexes = null,
    ResourceManagementContext Context = default);

internal sealed record ManagedResourceGeneration(long Generation, object Value, ResourceManagementRecord Management);

public interface IResourceManager : IAsyncDisposable
{
    ResourceManagerId Id { get; }
    ResourceManagerCapabilities Capabilities { get; }
    ValueTask StartAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    ValueTask<ResourceManagementRecord> AdoptAsync(object value, ResourceAdoptionContext context);
    ValueTask RetireAsync(object value, ResourceManagementRecord record, ResourceRetireReason reason, CancellationToken cancellationToken = default);
    ValueTask PumpAsync(ResourceManagerPumpContext context) => ValueTask.CompletedTask;
    ResourceManagerSnapshot CaptureSnapshot();
    ValueTask ShutdownAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
}

public class CpuResourceManager : IResourceManager
{
    private long _adopted, _retired, _bytes;
    public CpuResourceManager(ResourceManagerId id) => Id = id;
    public ResourceManagerId Id { get; }
    public virtual ResourceManagerCapabilities Capabilities => ResourceManagerCapabilities.AsyncRetirement;

    public virtual ValueTask<ResourceManagementRecord> AdoptAsync(object value, ResourceAdoptionContext context)
    {
        Interlocked.Increment(ref _adopted);
        ResourceAllocationInfo? allocation = value is byte[] bytes
            ? new($"{context.Type.FullName}:{context.Generation}", bytes.LongLength, bytes.LongLength, bytes.LongLength)
            : null;
        if (allocation is { } adoptedAllocation) Interlocked.Add(ref _bytes, adoptedAllocation.LogicalSize);
        return ValueTask.FromResult(new ResourceManagementRecord(Id, context.Ownership, allocation, ResourceIndexSet.Empty, context.ManagementContext));
    }

    public virtual async ValueTask RetireAsync(object value, ResourceManagementRecord record, ResourceRetireReason reason, CancellationToken cancellationToken = default)
    {
        if (record.Ownership == ResourceOwnership.Owned)
        {
            if (value is IAsyncDisposable asyncDisposable) await asyncDisposable.DisposeAsync().ConfigureAwait(false);
            else if (value is IDisposable disposable) disposable.Dispose();
        }
        if (record.Allocation is { } allocation) Interlocked.Add(ref _bytes, -allocation.LogicalSize);
        Interlocked.Increment(ref _retired);
    }

    public ResourceManagerSnapshot CaptureSnapshot() => new(Id, Interlocked.Read(ref _adopted), Interlocked.Read(ref _retired), Interlocked.Read(ref _bytes), 0);
    public ValueTask ShutdownAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

public sealed class IoResourceManager(ResourceManagerId id) : CpuResourceManager(id)
{
    public override ResourceManagerCapabilities Capabilities => ResourceManagerCapabilities.AllocationAccounting | ResourceManagerCapabilities.AsyncRetirement;
}

internal sealed class ResourceManagerTable(
    IReadOnlyDictionary<ResourceManagerId, IResourceManager> managers,
    IReadOnlyDictionary<Type, ResourceManagerId> exactBindings,
    IReadOnlyDictionary<ResourceManagerId, ResourceExecutionDomainHandle> defaultDomains,
    ResourceManagerId? defaultManager)
{
    private readonly IReadOnlyDictionary<ResourceManagerId, IResourceManager> _managers = managers;
    private readonly IReadOnlyDictionary<Type, ResourceManagerId> _exactBindings = exactBindings;
    private readonly IReadOnlyDictionary<ResourceManagerId, ResourceExecutionDomainHandle> _defaultDomains = defaultDomains;
    private readonly ResourceManagerId? _defaultManager = defaultManager;

    public IResourceManager Get(ResourceManagerHandle handle) => Get(handle.Id);
    public IResourceManager Get(ResourceManagerId id) => _managers.TryGetValue(id, out var manager)
        ? manager : throw new InvalidOperationException($"Resource manager '{id}' is not registered.");

    public IResourceManager Resolve(Type type, ResourceManagerHandle? explicitManager = null)
    {
        if (explicitManager is { } handle) return Get(handle);
        if (_exactBindings.TryGetValue(type, out var id)) return Get(id);
        if (_defaultManager is { } fallback) return Get(fallback);
        throw new InvalidOperationException($"No resource manager is registered for output type '{type}'.");
    }

    public ResourceExecutionDomainHandle DefaultDomain(ResourceManagerId id) => _defaultDomains.TryGetValue(id, out var domain)
        ? domain : throw new InvalidOperationException($"Resource manager '{id}' has no default execution domain.");

    public IEnumerable<IResourceManager> Values => _managers.Values;
}
