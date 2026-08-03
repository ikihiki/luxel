using Luxel.Resources;

namespace Luxel.AssetsGpu;

internal sealed record GpuPipelineRequest(
    GpuShaderCode Code,
    bool IsCompute,
    GpuRasterDesc Raster,
    string ComputeEntry,
    string VertexEntry,
    string PixelEntry);

internal enum GpuTextureRequestKind
{
    Sampled,
    RenderTarget,
    DepthTarget,
}

internal sealed record GpuTextureRequest(
    GpuTextureRequestKind Kind,
    uint Width,
    uint Height,
    GpuFormat Format,
    ReadOnlyMemory<byte> Data);

internal sealed record GpuSamplerRequest(GpuSamplerFilter Filter, GpuSamplerAddress Address);
internal sealed record GpuBufferRequest(ulong SizeInBytes, GpuMemoryKind Kind);

/// <summary>Pipeline descriptor → GPU pipeline。device-bound registry は ctor 注入される。</summary>
internal sealed class GpuPipelineCreationStep(AssetGpuRegistry registry)
    : IResourceStep<GpuPipelineRequest, GpuPipeline>
{
    public Executor Executor => Executor.External;

    public Task<GpuPipeline> RunAsync(GpuPipelineRequest input, ResourceUri uri, LoadContext ctx)
    {
        ctx.MarkOwned();
        return Task.FromResult(registry.Create(input));
    }
}

/// <summary>Texture descriptor → GPU texture。device-bound registry は ctor 注入される。</summary>
internal sealed class GpuTextureCreationStep(AssetGpuRegistry registry)
    : IResourceStep<GpuTextureRequest, GpuTexture>
{
    public Executor Executor => Executor.External;

    public Task<GpuTexture> RunAsync(GpuTextureRequest input, ResourceUri uri, LoadContext ctx)
    {
        ctx.MarkOwned();
        return Task.FromResult(registry.Create(input));
    }
}

/// <summary>Sampler descriptor → GPU sampler。device-bound registry は ctor 注入される。</summary>
internal sealed class GpuSamplerCreationStep(AssetGpuRegistry registry)
    : IResourceStep<GpuSamplerRequest, GpuSampler>
{
    public Executor Executor => Executor.External;

    public Task<GpuSampler> RunAsync(GpuSamplerRequest input, ResourceUri uri, LoadContext ctx)
    {
        ctx.MarkOwned();
        return Task.FromResult(registry.Create(input));
    }
}

/// <summary>Buffer descriptor → GPU buffer。device-bound registry は ctor 注入される。</summary>
internal sealed class GpuBufferCreationStep(AssetGpuRegistry registry)
    : IResourceStep<GpuBufferRequest, GpuBuffer>
{
    public Executor Executor => Executor.External;

    public Task<GpuBuffer> RunAsync(GpuBufferRequest input, ResourceUri uri, LoadContext ctx)
    {
        ctx.MarkOwned();
        return Task.FromResult(registry.Create(input));
    }
}
