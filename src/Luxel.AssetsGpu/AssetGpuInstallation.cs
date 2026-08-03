namespace Luxel.AssetsGpu;

/// <summary>
/// Owns the registry created by an AssetsGpu installation. Dispose this before the
/// <see cref="GpuDevice"/>; disposal first waits for the main queue and then releases
/// registry-owned GPU resources.
/// </summary>
public sealed class AssetGpuInstallation : IDisposable
{
    private readonly GpuDevice _device;
    private int _disposed;

    internal AssetGpuInstallation(GpuDevice device, AssetGpuRegistry registry)
    {
        _device = device;
        Registry = registry;
    }

    public AssetGpuRegistry Registry { get; }

    internal void WaitIdle()
    {
        if (Volatile.Read(ref _disposed) == 0) _device.MainQueue.WaitIdle();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _device.MainQueue.WaitIdle();
        Registry.Dispose();
    }
}
