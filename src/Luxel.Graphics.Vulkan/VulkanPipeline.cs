using Luxel.Graphics.Abstraction;
using Silk.NET.Vulkan;

namespace Luxel.Graphics.Vulkan;

internal sealed unsafe class VulkanPipeline : IGpuBackendPipeline
{
    private readonly Vk _vk;
    private readonly Device _device;
    private Pipeline _pipeline;
    private bool _disposed;

    public VulkanPipeline(Vk vk, Device device, Pipeline pipeline, bool isCompute)
    {
        _vk = vk;
        _device = device;
        _pipeline = pipeline;
        IsCompute = isCompute;
    }

    public bool IsCompute { get; }

    internal Pipeline Handle => _pipeline;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _vk.DestroyPipeline(_device, _pipeline, null);
    }
}
