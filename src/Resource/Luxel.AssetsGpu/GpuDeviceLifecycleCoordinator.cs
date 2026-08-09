using System.Collections.Concurrent;
using Luxel.Resources;

namespace Luxel.AssetsGpu;

public enum GpuDeviceOwnership
{
    Owned,
    Borrowed,
}

public sealed class GpuDeviceLifecycleCoordinatorOptions
{
    public GpuDeviceOwnership Ownership { get; set; } = GpuDeviceOwnership.Borrowed;
    public Func<ulong, IGpuLifecycleSink, CancellationToken, ValueTask<GpuDevice>>? OwnedDeviceFactory { get; set; }
    public int RecoveryPumpIterations { get; set; } = 3;
}

public readonly record struct GpuDeviceRecoverySnapshot(
    GpuDeviceGeneration Device,
    GpuDeviceLifecycleState State,
    GpuLifecycleReason Reason,
    long RecoveryCount,
    Exception? Error = null);

/// <summary>
/// Queues backend notifications and performs generation replacement only when PumpAsync is called
/// by the frame/ResourceSystem pump thread.
/// </summary>
public sealed class GpuDeviceLifecycleCoordinator : IGpuLifecycleSink
{
    private readonly ConcurrentQueue<GpuDeviceLifecycleEvent> _events = new();
    private readonly ConcurrentQueue<GpuResourceGeneration> _borrowedReplacements = new();
    private readonly ResourceSystem _resources;
    private readonly GpuResourceManagerHandle _installation;
    private readonly GpuDeviceLifecycleCoordinatorOptions _options;
    private readonly object _gate = new();
    private GpuDeviceRecoverySnapshot _snapshot;
    private long _lastTerminalSequence;
    private bool _recovering;

    public GpuDeviceLifecycleCoordinator(ResourceSystem resources, GpuResourceManagerHandle installation,
        GpuDeviceLifecycleCoordinatorOptions? options = null)
    {
        _resources = resources ?? throw new ArgumentNullException(nameof(resources));
        _installation = installation ?? throw new ArgumentNullException(nameof(installation));
        _options = options ?? new();
        if (_options.Ownership == GpuDeviceOwnership.Owned && _options.OwnedDeviceFactory is null)
            throw new InvalidOperationException("Owned GPU recovery requires an OwnedDeviceFactory.");
        _snapshot = new(installation.InitialGeneration.Identity, GpuDeviceLifecycleState.Ready, GpuLifecycleReason.None, 0);
    }

    public GpuDeviceRecoverySnapshot Snapshot { get { lock (_gate) return _snapshot; } }
    public event Action<GpuDeviceRecoverySnapshot>? StateChanged;

    public void Publish(GpuDeviceLifecycleEvent message) => _events.Enqueue(message ?? throw new ArgumentNullException(nameof(message)));
    public void Publish(GpuValidationEvent message) { }
    public void Publish(GpuSurfaceLifecycleEvent message) { }

    public void ProvideBorrowedReplacement(GpuDevice device, ulong generation)
    {
        ArgumentNullException.ThrowIfNull(device);
        GpuDeviceGeneration current = _installation.ManagerInstance.CurrentGeneration.Identity;
        _borrowedReplacements.Enqueue(new(device, new(current.DeviceId, generation)));
    }

    public async ValueTask<int> PumpAsync(CancellationToken cancellationToken = default)
    {
        int handled = 0;
        while (_events.TryDequeue(out GpuDeviceLifecycleEvent? message))
        {
            handled++;
            if (message.State is not (GpuDeviceLifecycleState.Lost or GpuDeviceLifecycleState.Faulted)) continue;
            GpuDeviceGeneration current = _installation.ManagerInstance.CurrentGeneration.Identity;
            if (message.Device != current || message.IsExpected || message.Sequence <= Volatile.Read(ref _lastTerminalSequence)) continue;
            Volatile.Write(ref _lastTerminalSequence, message.Sequence);
            await RecoverAsync(message, cancellationToken).ConfigureAwait(false);
        }

        if (_options.Ownership == GpuDeviceOwnership.Borrowed && Snapshot.State == GpuDeviceLifecycleState.Lost &&
            _borrowedReplacements.TryDequeue(out GpuResourceGeneration replacement))
        {
            await ActivateReplacementAsync(replacement, disposeOldDevice: false, Snapshot.Reason, cancellationToken).ConfigureAwait(false);
            handled++;
        }
        return handled;
    }

    private async ValueTask RecoverAsync(GpuDeviceLifecycleEvent message, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_recovering) return;
            _recovering = true;
        }
        try
        {
            PublishState(message.State, message.Reason);
            await _installation.DomainInstance.PauseAsync(cancellationToken).ConfigureAwait(false);
            await _installation.ManagerInstance.PauseAsync(cancellationToken).ConfigureAwait(false);

            if (_options.Ownership == GpuDeviceOwnership.Borrowed)
            {
                PublishState(GpuDeviceLifecycleState.Lost, message.Reason);
                return;
            }

            PublishState(GpuDeviceLifecycleState.Recovering, message.Reason);
            GpuResourceGeneration old = _installation.ManagerInstance.CurrentGeneration;
            ulong nextGeneration = checked(old.Identity.Generation + 1);
            GpuDevice nextDevice = await _options.OwnedDeviceFactory!(nextGeneration, this, cancellationToken).ConfigureAwait(false);
            var replacement = new GpuResourceGeneration(nextDevice, new(old.Identity.DeviceId, nextGeneration));
            await ActivateReplacementAsync(replacement, disposeOldDevice: true, message.Reason, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception error)
        {
            PublishState(GpuDeviceLifecycleState.Faulted, message.Reason, error);
            throw;
        }
        finally
        {
            lock (_gate) _recovering = false;
        }
    }

    private async ValueTask ActivateReplacementAsync(GpuResourceGeneration replacement, bool disposeOldDevice,
        GpuLifecycleReason reason, CancellationToken cancellationToken)
    {
        GpuResourceManager manager = _installation.ManagerInstance;
        GpuResourceGeneration old = manager.CurrentGeneration;
        if (!manager.IsPaused)
        {
            await _installation.DomainInstance.PauseAsync(cancellationToken).ConfigureAwait(false);
            await manager.PauseAsync(cancellationToken).ConfigureAwait(false);
        }

        manager.ActivateGeneration(replacement);
        _installation.Registry.ActivateGeneration(replacement.Device, replacement.Identity);

        _resources.InvalidateManager(manager.Id);
        manager.Resume();
        _installation.DomainInstance.Resume();
        for (int i = 0; i < Math.Max(1, _options.RecoveryPumpIterations); i++)
        {
            await _resources.PumpAsync(cancellationToken).ConfigureAwait(false);
            await Task.Yield();
        }
        if (disposeOldDevice)
        {
            try { old.Device.Dispose(); }
            catch { }
        }
        PublishState(GpuDeviceLifecycleState.Recovered, reason);
        PublishState(GpuDeviceLifecycleState.Ready, GpuLifecycleReason.None);
    }

    private void PublishState(GpuDeviceLifecycleState state, GpuLifecycleReason reason, Exception? error = null)
    {
        GpuDeviceRecoverySnapshot snapshot;
        lock (_gate)
        {
            long recoveries = _snapshot.RecoveryCount + (state == GpuDeviceLifecycleState.Recovered ? 1 : 0);
            _snapshot = snapshot = new(_installation.ManagerInstance.CurrentGeneration.Identity, state, reason, recoveries, error);
        }
        StateChanged?.Invoke(snapshot);
    }
}
