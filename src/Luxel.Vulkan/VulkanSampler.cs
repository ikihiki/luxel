using Luxel.Abstraction;
using Silk.NET.Vulkan;

namespace Luxel.Vulkan;

internal sealed unsafe class VulkanSampler : IGpuBackendSampler
{
    private readonly Vk _vk;
    private readonly Device _device;
    private Sampler _sampler;
    private bool _disposed;

    public VulkanSampler(Vk vk, Device device, Sampler sampler, uint bindlessIndex)
    {
        _vk = vk;
        _device = device;
        _sampler = sampler;
        BindlessIndex = bindlessIndex;
    }

    public uint BindlessIndex { get; }
    internal Sampler Handle => _sampler;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _vk.DestroySampler(_device, _sampler, null);
    }
}
