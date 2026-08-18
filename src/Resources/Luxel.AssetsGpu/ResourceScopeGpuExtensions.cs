using System.Runtime.CompilerServices;
using Luxel.Resources;

namespace Luxel.AssetsGpu;

/// <summary>
/// GPU creation descriptors are registered as scope-local inputs and converted by AssetsGpu Steps.
/// Each Step receives the device-bound <see cref="AssetGpuRegistry"/> through constructor injection;
/// callers therefore never pass a device to these factory methods.
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
        var request = new GpuPipelineRequest(
            code, Shader: null, IsCompute: true, default, entryPoint);
        return scope.Create<GpuPipelineRequest, GpuPipeline>(localKey, request);
    }

    public static ResourceHandle<GpuPipeline> CreateComputePipeline(
        this ResourceScope scope,
        string localKey,
        ResourceHandle<GpuShaderCode> shader,
        string entryPoint = "main")
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(shader);
        var request = new GpuPipelineRequest(
            Code: null, Shader: shader, IsCompute: true, default, entryPoint);
        return scope.Create<GpuPipelineRequest, GpuPipeline>(localKey, request);
    }

    public static ResourceHandle<GpuPipeline> CreateGraphicsPipeline(
        this ResourceScope scope, string localKey, GpuShaderCode code, GpuGraphicsPipelineDesc description)
    {
        ArgumentNullException.ThrowIfNull(scope); ArgumentNullException.ThrowIfNull(code);
        return scope.Create<GpuPipelineRequest, GpuPipeline>(localKey,
            new GpuPipelineRequest(code, null, false, description, "main"));
    }

    public static ResourceHandle<GpuPipeline> CreateGraphicsPipeline(
        this ResourceScope scope, string localKey, ResourceHandle<GpuShaderCode> shader, GpuGraphicsPipelineDesc description)
    {
        ArgumentNullException.ThrowIfNull(scope); ArgumentNullException.ThrowIfNull(shader);
        return scope.Create<GpuPipelineRequest, GpuPipeline>(localKey,
            new GpuPipelineRequest(null, shader, false, description, "main"));
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
        var request = new GpuTextureRequest(
            GpuTextureRequestKind.Sampled, width, height, format, data);
        return scope.Create<GpuTextureRequest, GpuTexture>(localKey, request);
    }

    public static ResourceHandle<GpuSampler> CreateSampler(
        this ResourceScope scope,
        string localKey,
        GpuSamplerFilter filter = GpuSamplerFilter.Linear,
        GpuSamplerAddress address = GpuSamplerAddress.Clamp)
    {
        ArgumentNullException.ThrowIfNull(scope);
        return scope.Create<GpuSamplerRequest, GpuSampler>(
            localKey, new GpuSamplerRequest(filter, address));
    }

    public static ResourceHandle<GpuTexture> CreateRenderTarget(
        this ResourceScope scope,
        string localKey,
        uint width,
        uint height,
        GpuFormat format = GpuFormat.Rgba8Unorm)
    {
        ArgumentNullException.ThrowIfNull(scope);
        var request = new GpuTextureRequest(
            GpuTextureRequestKind.RenderTarget, width, height, format, ReadOnlyMemory<byte>.Empty);
        return scope.Create<GpuTextureRequest, GpuTexture>(localKey, request);
    }

    public static ResourceHandle<GpuTexture> CreateDepthTarget(
        this ResourceScope scope,
        string localKey,
        uint width,
        uint height,
        GpuFormat format = GpuFormat.D32Float)
    {
        ArgumentNullException.ThrowIfNull(scope);
        var request = new GpuTextureRequest(
            GpuTextureRequestKind.DepthTarget, width, height, format, ReadOnlyMemory<byte>.Empty);
        return scope.Create<GpuTextureRequest, GpuTexture>(localKey, request);
    }

    public static ResourceHandle<GpuBuffer> CreateBuffer(
        this ResourceScope scope,
        string localKey,
        ulong sizeInBytes,
        GpuMemoryKind kind = GpuMemoryKind.HostMapped)
    {
        ArgumentNullException.ThrowIfNull(scope);
        return scope.Create<GpuBufferRequest, GpuBuffer>(
            localKey, new GpuBufferRequest(sizeInBytes, kind));
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
