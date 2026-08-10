using Luxel.Graphics.Abstraction;
using Silk.NET.Vulkan;

namespace Luxel.Graphics.Vulkan;

internal sealed unsafe class VulkanPipeline : IGpuBackendPipeline
{
    private readonly Vk _vk;
    private readonly Device _device;
    private Pipeline _pipeline;
    private readonly Func<GpuGraphicsPipelineVariantKey, Pipeline>? _factory;
    private readonly Dictionary<GpuGraphicsPipelineVariantKey, VulkanPipeline>? _variants;
    private ulong _hits, _misses;
    private bool _disposed;

    public VulkanPipeline(Vk vk, Device device, Pipeline pipeline, bool isCompute)
    { _vk = vk; _device = device; _pipeline = pipeline; IsCompute = isCompute; }

    public VulkanPipeline(Vk vk, Device device, GpuGraphicsPipelineDesc description, Func<GpuGraphicsPipelineVariantKey, Pipeline> factory)
    { _vk = vk; _device = device; GraphicsDescription = description; _factory = factory; _variants = new(); }

    public bool IsCompute { get; }
    public GpuGraphicsPipelineDesc? GraphicsDescription { get; }
    public GpuPipelineDiagnostics Diagnostics => new(_hits, _misses, (ulong)(_variants?.Count ?? (_pipeline.Handle != 0 ? 1 : 0)));
    internal Pipeline Handle => _pipeline;

    public IGpuBackendPipeline ResolveGraphicsVariant(GpuRasterizerState rasterizer, GpuDepthStencilState depthStencil, GpuBlendState blend)
    {
        if (GraphicsDescription is not { } desc) return this;
        var key = new GpuGraphicsPipelineVariantKey(desc.Attachments, desc.Topology, rasterizer, depthStencil.Normalize(), blend);
        if (_variants!.TryGetValue(key, out var value)) { _hits++; return value; }
        _misses++;
        value = new VulkanPipeline(_vk, _device, _factory!(key), false);
        _variants.Add(key, value);
        return value;
    }

    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
        if (_variants is not null) foreach (var variant in _variants.Values) variant.Dispose();
        if (_pipeline.Handle != 0) _vk.DestroyPipeline(_device, _pipeline, null);
    }
}
