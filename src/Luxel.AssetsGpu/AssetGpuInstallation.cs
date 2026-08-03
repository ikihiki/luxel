using System.Runtime.CompilerServices;
using Luxel.Resources;

namespace Luxel.AssetsGpu;

/// <summary>
/// Owns the registry created by an AssetsGpu installation. Dispose this before the
/// <see cref="GpuDevice"/>; disposal first waits for the main queue and then releases
/// registry-owned GPU resources.
/// </summary>
public sealed class AssetGpuInstallation : IDisposable
{
    private readonly ResourceSystem _resources;
    private readonly GpuDevice _device;
    private int _disposed;

    internal AssetGpuInstallation(ResourceSystem resources, GpuDevice device, AssetGpuRegistry registry)
    {
        _resources = resources;
        _device = device;
        Registry = registry;
        AssetGpuInstallations.Register(resources, this);
    }

    public AssetGpuRegistry Registry { get; }
    internal GpuDevice Device => _device;

    internal void WaitIdle()
    {
        if (Volatile.Read(ref _disposed) == 0) _device.MainQueue.WaitIdle();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        AssetGpuInstallations.Unregister(_resources, this);
        _device.MainQueue.WaitIdle();
        Registry.Dispose();
    }
}

internal static class AssetGpuInstallations
{
    private static readonly ConditionalWeakTable<ResourceSystem, AssetGpuInstallation> Installations = new();
    private static readonly object Gate = new();

    public static void Register(ResourceSystem resources, AssetGpuInstallation installation)
    {
        lock (Gate)
        {
            if (Installations.TryGetValue(resources, out _))
                throw new InvalidOperationException("AssetsGpu is already installed for this ResourceSystem.");
            Installations.Add(resources, installation);
        }
    }

    public static void Unregister(ResourceSystem resources, AssetGpuInstallation installation)
    {
        lock (Gate)
        {
            if (Installations.TryGetValue(resources, out AssetGpuInstallation? current) &&
                ReferenceEquals(current, installation))
                Installations.Remove(resources);
        }
    }

    public static GpuDevice RequireDevice(ResourceScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        lock (Gate)
        {
            if (Installations.TryGetValue(scope.System, out AssetGpuInstallation? installation))
                return installation.Device;
        }
        throw new InvalidOperationException(
            "AssetsGpu is not installed for this ResourceSystem. Call InstallAssetGpuLifecycle(device) before creating GPU resources.");
    }
}
