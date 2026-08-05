using Luxel.Graphics.Abstraction;

namespace Luxel.Graphics;

/// <summary>compute / graphics / mesh パイプライン。</summary>
public sealed class GpuPipeline : IDisposable
{
    private readonly IGpuBackendPipeline _pipeline;
    private bool _disposed;

    internal GpuPipeline(IGpuBackendPipeline pipeline) => _pipeline = pipeline;

    public bool IsCompute => _pipeline.IsCompute;
    public GpuGraphicsPipelineDesc? GraphicsDescription => _pipeline.GraphicsDescription;
    public GpuPipelineDiagnostics Diagnostics => _pipeline.Diagnostics;

    internal GpuRasterizerState LegacyRasterizerState { get; set; } = GpuRasterizerState.Default;
    internal GpuDepthStencilState LegacyDepthStencilState { get; set; } = GpuDepthStencilState.Default;
    internal GpuBlendState LegacyBlendState { get; set; } = GpuBlendState.None;
    internal IGpuBackendPipeline Backend => _pipeline;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _pipeline.Dispose();
    }
}
