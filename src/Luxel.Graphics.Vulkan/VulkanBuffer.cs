using Luxel.Graphics.Abstraction;
using Silk.NET.Vulkan;

namespace Luxel.Graphics.Vulkan;

internal sealed unsafe class VulkanBuffer : IGpuBackendBuffer
{
    private readonly Vk _vk;
    private readonly Device _device;
    private Silk.NET.Vulkan.Buffer _buffer;
    private DeviceMemory _memory;
    private void* _mapped;
    private bool _disposed;

    public VulkanBuffer(Vk vk, Device device, Silk.NET.Vulkan.Buffer buffer, DeviceMemory memory,
                        ulong size, ulong deviceAddress, uint bindlessIndex, void* mapped)
    {
        _vk = vk;
        _device = device;
        _buffer = buffer;
        _memory = memory;
        Size = size;
        DeviceAddress = deviceAddress;
        BindlessIndex = bindlessIndex;
        _mapped = mapped;
    }

    public ulong Size { get; }
    public ulong DeviceAddress { get; }
    public uint BindlessIndex { get; }
    public void* MappedPointer => _mapped;

    internal Silk.NET.Vulkan.Buffer Handle => _buffer;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_mapped != null)
        {
            _vk.UnmapMemory(_device, _memory);
            _mapped = null;
        }
        _vk.DestroyBuffer(_device, _buffer, null);
        _vk.FreeMemory(_device, _memory, null);
    }
}
