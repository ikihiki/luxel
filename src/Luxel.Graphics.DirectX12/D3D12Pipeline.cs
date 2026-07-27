using Luxel.Graphics.Abstraction;
using Vortice.Direct3D12;

namespace Luxel.Graphics.DirectX12;

internal sealed class D3D12Pipeline : IGpuBackendPipeline
{
    private ID3D12PipelineState _pso;
    private bool _disposed;

    public D3D12Pipeline(ID3D12PipelineState pso, bool isCompute)
    {
        _pso = pso;
        IsCompute = isCompute;
    }

    public bool IsCompute { get; }
    internal ID3D12PipelineState Handle => _pso;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _pso.Dispose();
    }
}
