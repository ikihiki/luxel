using System.Diagnostics;

namespace Luxel.Resources.Browser;

/// <summary>Serializes resource work cooperatively on the browser-WASM runtime event loop.</summary>
public sealed class BrowserResourceExecutionDomain : IResourceExecutionDomain
{
    private sealed record WorkItem(
        Func<CancellationToken, ValueTask<object>> Work,
        CancellationToken CancellationToken,
        TaskCompletionSource<object> Completion,
        long EnqueuedTimestamp);

    private readonly object _gate = new();
    private readonly Queue<WorkItem> _queue = new();
    private readonly TaskCompletionSource _drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _started;
    private bool _scheduled;
    private bool _stopped;
    private int _active;
    private long _completed;
    private long _queueTicks;
    private long _runTicks;

    public BrowserResourceExecutionDomain(ResourceExecutionDomainId id, TimeSpan? operationBudget = null)
    {
        Id = id;
        Capabilities = new(1, ResourceThreadAffinity.HostThread, ResourceProgressModel.Cooperative, false, operationBudget);
    }

    public ResourceExecutionDomainId Id { get; }
    public ResourceExecutionDomainCapabilities Capabilities { get; }

    public ValueTask StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_stopped, this);
            _started = true;
            ScheduleLocked();
        }
        return ValueTask.CompletedTask;
    }

    public ValueTask<object> DispatchAsync(Func<CancellationToken, ValueTask<object>> work, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(work);
        if (cancellationToken.IsCancellationRequested) return ValueTask.FromCanceled<object>(cancellationToken);
        var completion = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_stopped, this);
            if (!_started) throw new InvalidOperationException($"Execution domain '{Id}' is not ready.");
            _queue.Enqueue(new(work, cancellationToken, completion, Stopwatch.GetTimestamp()));
            ScheduleLocked();
        }
        return new(completion.Task);
    }

    private void ScheduleLocked()
    {
        if (_scheduled || !_started || _stopped || _active != 0 || _queue.Count == 0) return;
        _scheduled = true;
        _ = RunScheduledAsync();
    }

    private async Task RunScheduledAsync()
    {
        // Do not capture ambient dispatch state. The timer continuation creates an
        // independent cooperative event-loop turn before this FIFO item starts.
        await Task.Delay(1).ConfigureAwait(false);
        RunNext();
    }

    private async void RunNext()
    {
        WorkItem? item;
        lock (_gate)
        {
            _scheduled = false;
            if (_stopped || _active != 0 || _queue.Count == 0)
            {
                CompleteDrainLocked();
                return;
            }
            item = _queue.Dequeue();
            _active = 1;
        }

        long started = Stopwatch.GetTimestamp();
        Interlocked.Add(ref _queueTicks, started - item.EnqueuedTimestamp);
        try
        {
            item.CancellationToken.ThrowIfCancellationRequested();
            object result = await item.Work(item.CancellationToken);
            item.Completion.TrySetResult(result);
        }
        catch (OperationCanceledException error) when (error.CancellationToken == item.CancellationToken)
        {
            item.Completion.TrySetCanceled(item.CancellationToken);
        }
        catch (Exception error)
        {
            item.Completion.TrySetException(error);
        }
        finally
        {
            Interlocked.Add(ref _runTicks, Stopwatch.GetTimestamp() - started);
            Interlocked.Increment(ref _completed);
            lock (_gate)
            {
                _active = 0;
                CompleteDrainLocked();
                // Rescheduling, rather than directly looping, gives the browser event loop a fairness point.
                ScheduleLocked();
            }
        }
    }

    public ResourceExecutionDomainSnapshot CaptureSnapshot()
    {
        int queued;
        int active;
        lock (_gate) { queued = _queue.Count; active = _active; }
        return new(Id, queued, active, Interlocked.Read(ref _completed),
            Stopwatch.GetElapsedTime(0, Interlocked.Read(ref _queueTicks)),
            Stopwatch.GetElapsedTime(0, Interlocked.Read(ref _runTicks)));
    }

    public async ValueTask ShutdownAsync(CancellationToken cancellationToken = default)
    {
        WorkItem[] pending;
        lock (_gate)
        {
            if (!_stopped)
            {
                _stopped = true;
                pending = _queue.ToArray();
                _queue.Clear();
                CompleteDrainLocked();
            }
            else pending = [];
        }
        foreach (WorkItem item in pending)
            item.Completion.TrySetException(new ObjectDisposedException(nameof(BrowserResourceExecutionDomain)));
        await _drained.Task.WaitAsync(cancellationToken);
    }

    private void CompleteDrainLocked()
    {
        if (_stopped && _active == 0) _drained.TrySetResult();
    }

    public ValueTask DisposeAsync() => ShutdownAsync();
}
