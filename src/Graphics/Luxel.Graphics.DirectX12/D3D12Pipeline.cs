using Luxel.Graphics.Abstraction;
using Vortice.Direct3D12;

namespace Luxel.Graphics.DirectX12;

internal sealed class D3D12Pipeline : IGpuBackendPipeline
{
    private ID3D12PipelineState? _pso;
    private readonly Func<GpuGraphicsPipelineVariantKey, ID3D12PipelineState>? _factory;
    private readonly Dictionary<GpuGraphicsPipelineVariantKey, D3D12Pipeline>? _variants;
    private ulong _hits, _misses;
    private bool _disposed;

    public D3D12Pipeline(ID3D12PipelineState pso, bool isCompute) { _pso = pso; IsCompute = isCompute; }
    public D3D12Pipeline(GpuGraphicsPipelineDesc description, Func<GpuGraphicsPipelineVariantKey, ID3D12PipelineState> factory)
    { GraphicsDescription = description; _factory = factory; _variants = new(); }
    public bool IsCompute { get; }
    public GpuGraphicsPipelineDesc? GraphicsDescription { get; }
    public GpuPipelineDiagnostics Diagnostics => new(_hits, _misses, (ulong)(_variants?.Count ?? (_pso is null ? 0 : 1)));
    internal ID3D12PipelineState Handle => _pso ?? throw new InvalidOperationException("Logical pipeline must be resolved before binding.");
    public IGpuBackendPipeline ResolveGraphicsVariant(GpuRasterizerState rasterizer, GpuDepthStencilState depthStencil, GpuBlendState blend)
    {
        if (GraphicsDescription is not { } desc) return this;
        var key = new GpuGraphicsPipelineVariantKey(desc.Attachments, desc.Topology, rasterizer, depthStencil.Normalize(), blend);
        if (_variants!.TryGetValue(key, out var value)) { _hits++; return value; }
        _misses++; value = new D3D12Pipeline(_factory!(key), false); _variants.Add(key, value); return value;
    }
    public void Dispose() { if (_disposed) return; _disposed = true; if (_variants is not null) foreach (var v in _variants.Values) v.Dispose(); _pso?.Dispose(); }
}
