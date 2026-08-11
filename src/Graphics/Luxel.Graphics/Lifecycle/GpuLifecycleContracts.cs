using System.Collections.Concurrent;

namespace Luxel.Graphics;

/// <summary>Identifies one logical GPU device and one concrete backend generation.</summary>
public readonly record struct GpuDeviceGeneration
{
    public GpuDeviceGeneration(string deviceId, ulong generation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);
        if (generation == 0) throw new ArgumentOutOfRangeException(nameof(generation));
        DeviceId = deviceId;
        Generation = generation;
    }

    public string DeviceId { get; }
    public ulong Generation { get; }
}

public enum GpuDeviceLifecycleState
{
    Creating,
    Ready,
    Faulted,
    Lost,
    Recovering,
    Recovered,
    Disposing,
    Disposed,
}

public enum GpuLifecycleReason
{
    None,
    Validation,
    DeviceRemoved,
    DeviceReset,
    DeviceHung,
    OutOfMemory,
    ExplicitDispose,
    SurfaceLost,
    SurfaceOutdated,
    Timeout,
    Unknown,
}

public enum GpuValidationSeverity
{
    Info,
    Warning,
    Error,
}

public enum GpuSurfaceLifecycleState
{
    Created,
    Resized,
    Lost,
    Outdated,
    Disposing,
    Disposed,
}

/// <summary>Immutable device lifecycle notification produced by a graphics backend.</summary>
public sealed record GpuDeviceLifecycleEvent(
    GpuDeviceGeneration Device,
    GpuBackendKind BackendKind,
    string BackendName,
    long Sequence,
    DateTimeOffset Timestamp,
    GpuDeviceLifecycleState State,
    GpuLifecycleReason Reason = GpuLifecycleReason.None,
    long? NativeResult = null,
    string? NativeReason = null,
    string? Message = null,
    Exception? Exception = null,
    bool IsExpected = false);

/// <summary>Immutable validation or uncaptured-error notification.</summary>
public sealed record GpuValidationEvent(
    GpuDeviceGeneration Device,
    GpuBackendKind BackendKind,
    string BackendName,
    long Sequence,
    DateTimeOffset Timestamp,
    GpuValidationSeverity Severity,
    GpuLifecycleReason Reason,
    string Message,
    long? NativeResult = null,
    string? NativeReason = null,
    Exception? Exception = null);

/// <summary>Immutable presentation-surface lifecycle notification.</summary>
public sealed record GpuSurfaceLifecycleEvent(
    GpuDeviceGeneration Device,
    GpuBackendKind BackendKind,
    string BackendName,
    string SurfaceId,
    long Sequence,
    DateTimeOffset Timestamp,
    GpuSurfaceLifecycleState State,
    uint Width,
    uint Height,
    GpuLifecycleReason Reason = GpuLifecycleReason.None,
    long? NativeResult = null,
    string? Message = null,
    Exception? Exception = null);

/// <summary>Transport-neutral destination for backend lifecycle notifications.</summary>
public interface IGpuLifecycleSink
{
    void Publish(GpuDeviceLifecycleEvent message);
    void Publish(GpuValidationEvent message);
    void Publish(GpuSurfaceLifecycleEvent message);
}

/// <summary>A sink that intentionally discards all notifications.</summary>
public sealed class NullGpuLifecycleSink : IGpuLifecycleSink
{
    public static NullGpuLifecycleSink Instance { get; } = new();
    private NullGpuLifecycleSink() { }
    public void Publish(GpuDeviceLifecycleEvent message) { }
    public void Publish(GpuValidationEvent message) { }
    public void Publish(GpuSurfaceLifecycleEvent message) { }
}

/// <summary>
/// Thread-safe callback boundary. Backends enqueue only; <see cref="Pump"/> forwards messages on the owner frame/Pump thread.
/// </summary>
public sealed class GpuLifecycleEventQueue : IGpuLifecycleSink
{
    private readonly ConcurrentQueue<object> _messages = new();

    public int Count => _messages.Count;

    public void Publish(GpuDeviceLifecycleEvent message) => _messages.Enqueue(message ?? throw new ArgumentNullException(nameof(message)));
    public void Publish(GpuValidationEvent message) => _messages.Enqueue(message ?? throw new ArgumentNullException(nameof(message)));
    public void Publish(GpuSurfaceLifecycleEvent message) => _messages.Enqueue(message ?? throw new ArgumentNullException(nameof(message)));

    public int Pump(IGpuLifecycleSink destination, int maximumCount = int.MaxValue)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (maximumCount < 0) throw new ArgumentOutOfRangeException(nameof(maximumCount));
        int count = 0;
        while (count < maximumCount && _messages.TryDequeue(out object? message))
        {
            switch (message)
            {
                case GpuDeviceLifecycleEvent device: destination.Publish(device); break;
                case GpuValidationEvent validation: destination.Publish(validation); break;
                case GpuSurfaceLifecycleEvent surface: destination.Publish(surface); break;
            }
            count++;
        }
        return count;
    }
}

/// <summary>Backend-owned event source that stamps immutable messages with generation, sequence, and time.</summary>
public sealed class GpuLifecycleSource
{
    private readonly IGpuLifecycleSink _sink;
    private long _sequence;

    public GpuLifecycleSource(
        GpuBackendKind backendKind,
        string backendName,
        IGpuLifecycleSink? sink = null,
        string? deviceId = null,
        ulong generation = 1)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(backendName);
        BackendKind = backendKind;
        BackendName = backendName;
        Device = new(deviceId ?? Guid.NewGuid().ToString("N"), generation);
        _sink = sink ?? NullGpuLifecycleSink.Instance;
    }

    public GpuDeviceGeneration Device { get; }
    public GpuBackendKind BackendKind { get; }
    public string BackendName { get; private set; }

    public void SetBackendName(string backendName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(backendName);
        BackendName = backendName;
    }

    public void DeviceEvent(GpuDeviceLifecycleState state, GpuLifecycleReason reason = GpuLifecycleReason.None,
        long? nativeResult = null, string? nativeReason = null, string? message = null,
        Exception? exception = null, bool isExpected = false)
        => _sink.Publish(new GpuDeviceLifecycleEvent(Device, BackendKind, BackendName, NextSequence(), DateTimeOffset.UtcNow,
            state, reason, nativeResult, nativeReason, message, exception, isExpected));

    public void Validation(GpuValidationSeverity severity, string message,
        GpuLifecycleReason reason = GpuLifecycleReason.Validation, long? nativeResult = null,
        string? nativeReason = null, Exception? exception = null)
        => _sink.Publish(new GpuValidationEvent(Device, BackendKind, BackendName, NextSequence(), DateTimeOffset.UtcNow,
            severity, reason, message, nativeResult, nativeReason, exception));

    public void Surface(string surfaceId, GpuSurfaceLifecycleState state, uint width, uint height,
        GpuLifecycleReason reason = GpuLifecycleReason.None, long? nativeResult = null,
        string? message = null, Exception? exception = null)
        => _sink.Publish(new GpuSurfaceLifecycleEvent(Device, BackendKind, BackendName, surfaceId, NextSequence(), DateTimeOffset.UtcNow,
            state, width, height, reason, nativeResult, message, exception));

    private long NextSequence() => Interlocked.Increment(ref _sequence);
}
