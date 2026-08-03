using System.Runtime.CompilerServices;
using Luxel.Resources;

namespace Luxel.AssetsGpu;

/// <summary>
/// Creates GPU objects as owned <see cref="ResourceScope"/> resources. The device is resolved
/// from the scope's <see cref="ResourceSystem"/> installation; call
/// <see cref="ResourceSystemExtensions.InstallAssetGpuLifecycle"/> once during host setup.
/// Local keys are qualified by the scope owner, so callers can use stable component-local names
/// without moving graphics descriptors into <c>Luxel.Resources</c>.
/// </summary>
public static class ResourceScopeGpuExtensions
{
    public static ResourceHandle<GpuPipeline> CreateComputePipeline(
        this ResourceScope scope,
        string localKey,
        GpuShaderCode code,
        string entryPoint = "main")
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(code);
        GpuDevice device = AssetGpuInstallations.RequireDevice(scope);
        return scope.Create(localKey,
            _ => Task.FromResult(device.CreateComputePipeline(code, entryPoint)),
            ResourceOwnership.Owned);
    }

    public static ResourceHandle<GpuPipeline> CreateGraphicsPipeline(
        this ResourceScope scope,
        string localKey,
        GpuShaderCode code,
        GpuRasterDesc raster,
        string vertexEntry = "vsMain",
        string pixelEntry = "psMain")
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(code);
        GpuDevice device = AssetGpuInstallations.RequireDevice(scope);
        return scope.Create(localKey,
            _ => Task.FromResult(device.CreateGraphicsPipeline(code, raster, vertexEntry, pixelEntry)),
            ResourceOwnership.Owned);
    }

    public static ResourceHandle<GpuTexture> CreateSampledTexture(
        this ResourceScope scope,
        string localKey,
        uint width,
        uint height,
        ReadOnlyMemory<byte> data,
        GpuFormat format = GpuFormat.Rgba8Unorm)
    {
        ArgumentNullException.ThrowIfNull(scope);
        GpuDevice device = AssetGpuInstallations.RequireDevice(scope);
        return scope.Create(localKey,
            _ => Task.FromResult(device.CreateTexture(width, height, data.Span, format)),
            ResourceOwnership.Owned);
    }

    public static ResourceHandle<GpuSampler> CreateSampler(
        this ResourceScope scope,
        string localKey,
        GpuSamplerFilter filter = GpuSamplerFilter.Linear,
        GpuSamplerAddress address = GpuSamplerAddress.Clamp)
    {
        ArgumentNullException.ThrowIfNull(scope);
        GpuDevice device = AssetGpuInstallations.RequireDevice(scope);
        return scope.Create(localKey,
            _ => Task.FromResult(device.CreateSampler(filter, address)),
            ResourceOwnership.Owned);
    }

    public static ResourceHandle<GpuTexture> CreateRenderTarget(
        this ResourceScope scope,
        string localKey,
        uint width,
        uint height,
        GpuFormat format = GpuFormat.Rgba8Unorm)
    {
        ArgumentNullException.ThrowIfNull(scope);
        GpuDevice device = AssetGpuInstallations.RequireDevice(scope);
        return scope.Create(localKey,
            _ => Task.FromResult(device.CreateRenderTarget(width, height, format)),
            ResourceOwnership.Owned);
    }

    public static ResourceHandle<GpuTexture> CreateDepthTarget(
        this ResourceScope scope,
        string localKey,
        uint width,
        uint height,
        GpuFormat format = GpuFormat.D32Float)
    {
        ArgumentNullException.ThrowIfNull(scope);
        GpuDevice device = AssetGpuInstallations.RequireDevice(scope);
        return scope.Create(localKey,
            _ => Task.FromResult(device.CreateDepthTarget(width, height, format)),
            ResourceOwnership.Owned);
    }

    public static ResourceHandle<GpuBuffer> CreateBuffer(
        this ResourceScope scope,
        string localKey,
        ulong sizeInBytes,
        GpuMemoryKind kind = GpuMemoryKind.HostMapped)
    {
        ArgumentNullException.ThrowIfNull(scope);
        GpuDevice device = AssetGpuInstallations.RequireDevice(scope);
        return scope.Create(localKey,
            _ => Task.FromResult(device.Malloc(sizeInBytes, kind)),
            ResourceOwnership.Owned);
    }

    public static ResourceHandle<GpuBuffer> CreateBuffer<T>(
        this ResourceScope scope,
        string localKey,
        int count,
        GpuMemoryKind kind = GpuMemoryKind.HostMapped)
        where T : unmanaged
    {
        if (count <= 0) throw new ArgumentOutOfRangeException(nameof(count));
        ulong sizeInBytes = checked((ulong)count * (ulong)Unsafe.SizeOf<T>());
        return scope.CreateBuffer(localKey, sizeInBytes, kind);
    }
}
