using Luxel.Graphics.Abstraction;

namespace Luxel.Graphics.DirectX12;

internal sealed class D3D12Sampler : IGpuBackendSampler
{
    private readonly Action _releaseDescriptor;
    private bool _disposed;

    public D3D12Sampler(uint bindlessIndex, Action releaseDescriptor)
    {
        BindlessIndex = bindlessIndex;
        _releaseDescriptor = releaseDescriptor;
    }

    public uint BindlessIndex { get; }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _releaseDescriptor();
    }
}
