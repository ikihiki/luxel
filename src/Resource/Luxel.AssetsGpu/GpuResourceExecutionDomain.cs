using Luxel.Resources;

namespace Luxel.AssetsGpu;

/// <summary>Serialized GPU execution domain that can stop admission at a device-generation boundary.</summary>
public sealed class GpuResourceExecutionDomain : IResourceExecutionDomain
{
    private readonly IResourceExecutionDomain _inner;
    private readonly ResourceExecutionDomainCapabilities _capabilities;
    private readonly object _gate = new();
    private TaskCompletionSource _resumed = Completed();
    private bool _paused;

    public GpuResourceExecutionDomain(ResourceExecutionDomainId id)
        : this(new SerialResourceExecutionDomain(id),
            new(1, ResourceThreadAffinity.DeviceThread, ResourceProgressModel.Serialized)) { }

    public GpuResourceExecutionDomain(
        IResourceExecutionDomain inner,
        ResourceExecutionDomainCapabilities capabilities)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _capabilities = capabilities;
    }

    public ResourceExecutionDomainId Id => _inner.Id;
    public ResourceExecutionDomainCapabilities Capabilities => _capabilities;
    public ValueTask StartAsync(CancellationToken cancellationToken = default) => _inner.StartAsync(cancellationToken);

    public async ValueTask<object> DispatchAsync(Func<CancellationToken, ValueTask<object>> work, CancellationToken cancellationToken = default)
    {
        Task wait;
        lock (_gate) wait = _resumed.Task;
        await wait.WaitAsync(cancellationToken).ConfigureAwait(false);
        return await _inner.DispatchAsync(work, cancellationToken).ConfigureAwait(false);
    }

    public void Pause()
    {
        lock (_gate)
        {
            if (_paused) return;
            _paused = true;
            _resumed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    public async ValueTask PauseAsync(CancellationToken cancellationToken = default)
    {
        Pause();
        while (true)
        {
            ResourceExecutionDomainSnapshot snapshot = _inner.CaptureSnapshot();
            if (snapshot.ActiveCount == 0 && snapshot.QueueDepth == 0) return;
            await Task.Delay(1, cancellationToken).ConfigureAwait(false);
        }
    }

    public void Resume()
    {
        lock (_gate)
        {
            if (!_paused) return;
            _paused = false;
            _resumed.TrySetResult();
        }
    }

    public ResourceExecutionDomainSnapshot CaptureSnapshot() => _inner.CaptureSnapshot();
    public ValueTask ShutdownAsync(CancellationToken cancellationToken = default) => _inner.ShutdownAsync(cancellationToken);
    public ValueTask DisposeAsync() => _inner.DisposeAsync();
    private static TaskCompletionSource Completed() { var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); source.SetResult(); return source; }
}
