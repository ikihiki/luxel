using Luxel.Graphics.Abstraction;
using Vortice.Direct3D12;

namespace Luxel.Graphics.DirectX12;

internal sealed unsafe class D3D12Buffer : IGpuBackendBuffer
{
    private ID3D12Resource _resource;
    private readonly bool _mapped;
    private readonly Action _releaseDescriptor;
    private void* _ptr;
    private bool _disposed;

    public D3D12Buffer(ID3D12Resource resource, ulong size, ulong gpuVirtualAddress,
                       uint bindlessIndex, void* mappedPtr, GpuMemoryKind memoryKind,
                       Action releaseDescriptor)
    {
        _resource = resource;
        Size = size;
        DeviceAddress = gpuVirtualAddress;
        BindlessIndex = bindlessIndex;
        MemoryKind = memoryKind;
        _ptr = mappedPtr;
        _mapped = mappedPtr != null;
        _releaseDescriptor = releaseDescriptor;
    }

    public ulong Size { get; }
    public ulong DeviceAddress { get; }
    public uint BindlessIndex { get; }
    public void* MappedPointer => _ptr;

    internal ID3D12Resource Resource => _resource;
    internal GpuMemoryKind MemoryKind { get; }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            if (_mapped) _resource.Unmap(0);
            _resource.Dispose();
        }
        finally
        {
            _releaseDescriptor();
        }
    }
}
