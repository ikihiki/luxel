using Luxel.Graphics.Abstraction;
using Silk.NET.Vulkan;

namespace Luxel.Graphics.Vulkan;

internal sealed unsafe class VulkanSampler : IGpuBackendSampler
{
    private readonly Vk _vk;
    private readonly Device _device;
    private Sampler _sampler;
    private readonly Action _releaseDescriptor;
    private bool _disposed;

    public VulkanSampler(Vk vk, Device device, Sampler sampler, uint bindlessIndex, Action releaseDescriptor)
    {
        _vk = vk;
        _device = device;
        _sampler = sampler;
        BindlessIndex = bindlessIndex;
        _releaseDescriptor = releaseDescriptor;
    }

    public uint BindlessIndex { get; }
    internal Sampler Handle => _sampler;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _vk.DestroySampler(_device, _sampler, null);
        _releaseDescriptor();
    }
}
