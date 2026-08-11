using System.Collections.Concurrent;
using System.Diagnostics;

namespace Luxel.Resources;

public readonly record struct ResourceExecutionDomainId
{
    public ResourceExecutionDomainId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value.Trim();
    }
    public string Value { get; }
    public override string ToString() => Value;
}

public readonly record struct ResourceExecutionDomainHandle(ResourceExecutionDomainId Id);

public enum ResourceThreadAffinity { AnyThread, HostThread, DeviceThread, DedicatedThread }
public enum ResourceProgressModel { Parallel, Serialized, Cooperative }

public readonly record struct ResourceExecutionDomainCapabilities(
    int MaxConcurrency,
    ResourceThreadAffinity Affinity,
    ResourceProgressModel ProgressModel,
    bool AllowsSynchronousBlocking = false,
    TimeSpan? OperationBudget = null);

public readonly record struct ResourceExecutionDomainSnapshot(
    ResourceExecutionDomainId Id,
    int QueueDepth,
    int ActiveCount,
    long CompletedCount,
    TimeSpan TotalQueueDuration,
    TimeSpan TotalRunDuration);

public readonly record struct ResourceExecutionDomainBuildContext(ResourceExecutionDomainId Id, ResourceExecutionDomainCapabilities Capabilities);

public interface IResourceExecutionDomain : IAsyncDisposable
{
    ResourceExecutionDomainId Id { get; }
    ResourceExecutionDomainCapabilities Capabilities { get; }
    ValueTask StartAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    ValueTask<object> DispatchAsync(Func<CancellationToken, ValueTask<object>> work, CancellationToken cancellationToken = default);
    ResourceExecutionDomainSnapshot CaptureSnapshot();
    ValueTask ShutdownAsync(CancellationToken cancellationToken = default);
}

internal sealed record ResourceDomainWorkItem(
    Func<CancellationToken, ValueTask<object>> Work,
    CancellationToken CancellationToken,
    TaskCompletionSource<object> Completion,
    long EnqueuedTimestamp);

public class ThreadPoolResourceExecutionDomain : IResourceExecutionDomain
{
    private readonly object _gate = new();
    private readonly Queue<ResourceDomainWorkItem> _queue = new();
    private readonly CancellationTokenSource _shutdown = new();
    private int _active;
    private bool _started;
    private bool _stopped;
    private long _completed;
    private long _queueTicks;
    private readonly TaskCompletionSource _drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private long _runTicks;

    public ThreadPoolResourceExecutionDomain(ResourceExecutionDomainId id, int maxConcurrency,
        ResourceThreadAffinity affinity = ResourceThreadAffinity.AnyThread,
        ResourceProgressModel progressModel = ResourceProgressModel.Parallel,
        TimeSpan? operationBudget = null)
    {
        if (maxConcurrency < 1) throw new ArgumentOutOfRangeException(nameof(maxConcurrency));
        Id = id;
        Capabilities = new(maxConcurrency, affinity, progressModel, false, operationBudget);
    }

    public ResourceExecutionDomainId Id { get; }
    public ResourceExecutionDomainCapabilities Capabilities { get; }

    public virtual ValueTask StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (_stopped) throw new ObjectDisposedException(GetType().Name);
            _started = true;
        }
        Drain();
        return ValueTask.CompletedTask;
    }

    public ValueTask<object> DispatchAsync(Func<CancellationToken, ValueTask<object>> work, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(work);
        if (cancellationToken.IsCancellationRequested)
            return ValueTask.FromCanceled<object>(cancellationToken);
        var completion = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            if (_stopped) throw new ObjectDisposedException(GetType().Name);
            if (!_started) throw new InvalidOperationException($"Execution domain '{Id}' is not ready.");
            _queue.Enqueue(new(work, cancellationToken, completion, Stopwatch.GetTimestamp()));
        }
        Drain();
        return new(completion.Task);
    }

    private void Drain()
    {
        while (true)
        {
            ResourceDomainWorkItem? item;
            lock (_gate)
            {
                if (!_started || _stopped || _active >= Capabilities.MaxConcurrency || _queue.Count == 0) return;
                item = _queue.Dequeue();
                _active++;
            }
            _ = Task.Run(() => ExecuteAsync(item), CancellationToken.None);
        }
    }

    private async Task ExecuteAsync(ResourceDomainWorkItem item)
    {
        long started = Stopwatch.GetTimestamp();
        Interlocked.Add(ref _queueTicks, started - item.EnqueuedTimestamp);
        try
        {
            item.CancellationToken.ThrowIfCancellationRequested();
            object result = await item.Work(item.CancellationToken).ConfigureAwait(false);
            item.Completion.TrySetResult(result);
        }
        catch (OperationCanceledException e) when (e.CancellationToken == item.CancellationToken)
        {
            item.Completion.TrySetCanceled(item.CancellationToken);
        }
        catch (Exception e) { item.Completion.TrySetException(e); }
        finally
        {
            Interlocked.Add(ref _runTicks, Stopwatch.GetTimestamp() - started);
            Interlocked.Increment(ref _completed);
            lock (_gate)
            {
                _active--;
                if (_stopped && _active == 0) _drained.TrySetResult();
            }
            Drain();
        }
    }

    public ResourceExecutionDomainSnapshot CaptureSnapshot()
    {
        int queued, active;
        lock (_gate) { queued = _queue.Count; active = _active; }
        return new(Id, queued, active, Interlocked.Read(ref _completed),
            Stopwatch.GetElapsedTime(0, Interlocked.Read(ref _queueTicks)),
            Stopwatch.GetElapsedTime(0, Interlocked.Read(ref _runTicks)));
    }

    public virtual async ValueTask ShutdownAsync(CancellationToken cancellationToken = default)
    {
        ResourceDomainWorkItem[] pending;
        lock (_gate)
        {
            if (_stopped)
            {
                pending = [];
            }
            else
            {
                _stopped = true;
                pending = _queue.ToArray();
                _queue.Clear();
                if (_active == 0) _drained.TrySetResult();
            }
        }
        _shutdown.Cancel();
        foreach (ResourceDomainWorkItem item in pending)
            item.Completion.TrySetException(new ObjectDisposedException(GetType().Name));
        await _drained.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        await ShutdownAsync().ConfigureAwait(false);
        _shutdown.Dispose();
    }
}

public sealed class SerialResourceExecutionDomain(ResourceExecutionDomainId id)
    : ThreadPoolResourceExecutionDomain(id, 1, ResourceThreadAffinity.AnyThread, ResourceProgressModel.Serialized);

public sealed class DedicatedThreadResourceExecutionDomain : IResourceExecutionDomain
{
    private readonly BlockingCollection<ResourceDomainWorkItem> _queue = new();
    private Thread? _thread;
    private long _completed, _queueTicks, _runTicks;
    private int _active;
    private bool _stopped;

    public DedicatedThreadResourceExecutionDomain(ResourceExecutionDomainId id, string? threadName = null)
    {
        Id = id;
        ThreadName = threadName ?? $"Luxel.Resource.{id.Value}";
    }

    public string ThreadName { get; }
    public ResourceExecutionDomainId Id { get; }
    public ResourceExecutionDomainCapabilities Capabilities { get; } = new(1, ResourceThreadAffinity.DedicatedThread, ResourceProgressModel.Serialized, true);

    public ValueTask StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_thread is not null) return ValueTask.CompletedTask;
        _thread = new Thread(Run) { IsBackground = true, Name = ThreadName };
        _thread.Start();
        return ValueTask.CompletedTask;
    }

    public ValueTask<object> DispatchAsync(Func<CancellationToken, ValueTask<object>> work, CancellationToken cancellationToken = default)
    {
        if (_thread is null) throw new InvalidOperationException($"Execution domain '{Id}' is not ready.");
        if (_stopped) throw new ObjectDisposedException(nameof(DedicatedThreadResourceExecutionDomain));
        if (cancellationToken.IsCancellationRequested) return ValueTask.FromCanceled<object>(cancellationToken);
        var completion = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
        _queue.Add(new(work, cancellationToken, completion, Stopwatch.GetTimestamp()));
        return new(completion.Task);
    }

    private void Run()
    {
        foreach (ResourceDomainWorkItem item in _queue.GetConsumingEnumerable())
        {
            long started = Stopwatch.GetTimestamp();
            Interlocked.Add(ref _queueTicks, started - item.EnqueuedTimestamp);
            Interlocked.Exchange(ref _active, 1);
            try
            {
                item.CancellationToken.ThrowIfCancellationRequested();
                object value = item.Work(item.CancellationToken).AsTask().GetAwaiter().GetResult();
                item.Completion.TrySetResult(value);
            }
            catch (OperationCanceledException e) when (e.CancellationToken == item.CancellationToken) { item.Completion.TrySetCanceled(item.CancellationToken); }
            catch (Exception e) { item.Completion.TrySetException(e); }
            finally
            {
                Interlocked.Exchange(ref _active, 0);
                Interlocked.Add(ref _runTicks, Stopwatch.GetTimestamp() - started);
                Interlocked.Increment(ref _completed);
            }
        }
    }

    public ResourceExecutionDomainSnapshot CaptureSnapshot() => new(Id, _queue.Count, Volatile.Read(ref _active),
        Interlocked.Read(ref _completed), Stopwatch.GetElapsedTime(0, Interlocked.Read(ref _queueTicks)), Stopwatch.GetElapsedTime(0, Interlocked.Read(ref _runTicks)));

    public ValueTask ShutdownAsync(CancellationToken cancellationToken = default)
    {
        if (_stopped) return ValueTask.CompletedTask;
        _stopped = true;
        _queue.CompleteAdding();
        if (_thread is not null && _thread != Thread.CurrentThread) _thread.Join();
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await ShutdownAsync().ConfigureAwait(false);
        _queue.Dispose();
    }
}

internal sealed class ResourceDomainTable(IReadOnlyDictionary<ResourceExecutionDomainId, IResourceExecutionDomain> domains)
{
    private readonly IReadOnlyDictionary<ResourceExecutionDomainId, IResourceExecutionDomain> _domains = domains;

    public IResourceExecutionDomain Get(ResourceExecutionDomainHandle handle) => Get(handle.Id);
    public IResourceExecutionDomain Get(ResourceExecutionDomainId id) => _domains.TryGetValue(id, out var domain)
        ? domain : throw new InvalidOperationException($"Execution domain '{id}' is not registered.");
    public ResourceExecutionDomainSnapshot[] CaptureSnapshots() => _domains.Values.Select(d => d.CaptureSnapshot()).ToArray();
    public IEnumerable<IResourceExecutionDomain> Values => _domains.Values;
}
