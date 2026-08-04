using Luxel.Resources;

namespace Luxel.AssetsGpu;

internal sealed record GpuPipelineRequest(
    GpuShaderCode? Code,
    ResourceHandle<GpuShaderCode>? Shader,
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

/// <summary>Immutable float array → initialized host-mapped GPU buffer.</summary>
internal sealed class Float32ArrayToGpuBufferStep(AssetGpuRegistry registry)
    : IResourceStep<float[], GpuBuffer>
{
    public Executor Executor => Executor.External;

    public Task<GpuBuffer> RunAsync(float[] input, ResourceUri uri, LoadContext ctx)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.Length == 0) throw new ArgumentException("GPU buffer source array cannot be empty.", nameof(input));
        ctx.MarkOwned();
        return Task.FromResult(registry.Create(input));
    }
}

/// <summary>Pipeline descriptor → GPU pipeline。device-bound registry は ctor 注入される。</summary>
internal sealed class GpuPipelineCreationStep(AssetGpuRegistry registry)
    : IResourceStep<GpuPipelineRequest, GpuPipeline>
{
    public Executor Executor => Executor.External;

    public async Task<GpuPipeline> RunAsync(GpuPipelineRequest input, ResourceUri uri, LoadContext ctx)
    {
        GpuShaderCode code = input.Shader is not null
            ? await ctx.Require(input.Shader).ConfigureAwait(false)
            : input.Code ?? throw new InvalidOperationException("GPU pipeline request has no shader code or shader resource handle.");
        ctx.MarkOwned();
        return registry.Create(input, code);
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
