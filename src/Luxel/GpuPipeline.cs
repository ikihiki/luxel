using Luxel.Abstraction;

namespace Luxel;

/// <summary>compute / graphics / mesh パイプライン。</summary>
public sealed class GpuPipeline : IDisposable
{
    private readonly IGpuBackendPipeline _pipeline;
    private bool _disposed;

    internal GpuPipeline(IGpuBackendPipeline pipeline) => _pipeline = pipeline;

    public bool IsCompute => _pipeline.IsCompute;

    internal IGpuBackendPipeline Backend => _pipeline;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _pipeline.Dispose();
    }
}
