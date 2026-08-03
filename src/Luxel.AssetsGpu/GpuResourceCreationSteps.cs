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

/// <summary>Pipeline descriptor → GPU pipeline。GpuDevice はインストール時に ctor 注入される。</summary>
internal sealed class GpuPipelineCreationStep(GpuDevice device)
    : IResourceStep<GpuPipelineRequest, GpuPipeline>
{
    public Executor Executor => Executor.External;

    public Task<GpuPipeline> RunAsync(GpuPipelineRequest input, ResourceUri uri, LoadContext ctx)
    {
        ctx.MarkOwned();
        GpuPipeline pipeline = input.IsCompute
            ? device.CreateComputePipeline(input.Code, input.ComputeEntry)
            : device.CreateGraphicsPipeline(input.Code, input.Raster, input.VertexEntry, input.PixelEntry);
        return Task.FromResult(pipeline);
    }
}

/// <summary>Texture descriptor → GPU texture。GpuDevice はインストール時に ctor 注入される。</summary>
internal sealed class GpuTextureCreationStep(GpuDevice device)
    : IResourceStep<GpuTextureRequest, GpuTexture>
{
    public Executor Executor => Executor.External;

    public Task<GpuTexture> RunAsync(GpuTextureRequest input, ResourceUri uri, LoadContext ctx)
    {
        ctx.MarkOwned();
        GpuTexture texture = input.Kind switch
        {
            GpuTextureRequestKind.Sampled =>
                device.CreateTexture(input.Width, input.Height, input.Data.Span, input.Format),
            GpuTextureRequestKind.RenderTarget =>
                device.CreateRenderTarget(input.Width, input.Height, input.Format),
            GpuTextureRequestKind.DepthTarget =>
                device.CreateDepthTarget(input.Width, input.Height, input.Format),
            _ => throw new ArgumentOutOfRangeException(nameof(input), input.Kind, "Unknown texture request kind."),
        };
        return Task.FromResult(texture);
    }
}

/// <summary>Sampler descriptor → GPU sampler。GpuDevice はインストール時に ctor 注入される。</summary>
internal sealed class GpuSamplerCreationStep(GpuDevice device)
    : IResourceStep<GpuSamplerRequest, GpuSampler>
{
    public Executor Executor => Executor.External;

    public Task<GpuSampler> RunAsync(GpuSamplerRequest input, ResourceUri uri, LoadContext ctx)
    {
        ctx.MarkOwned();
        return Task.FromResult(device.CreateSampler(input.Filter, input.Address));
    }
}

/// <summary>Buffer descriptor → GPU buffer。GpuDevice はインストール時に ctor 注入される。</summary>
internal sealed class GpuBufferCreationStep(GpuDevice device)
    : IResourceStep<GpuBufferRequest, GpuBuffer>
{
    public Executor Executor => Executor.External;

    public Task<GpuBuffer> RunAsync(GpuBufferRequest input, ResourceUri uri, LoadContext ctx)
    {
        ctx.MarkOwned();
        return Task.FromResult(device.Malloc(input.SizeInBytes, input.Kind));
    }
}
