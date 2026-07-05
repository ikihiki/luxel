using Luxel.Abstraction;
using Vortice.Direct3D12;
using Vortice.DXGI;

namespace Luxel.D3D12;

internal sealed class D3D12Texture : IGpuBackendTexture
{
    private ID3D12Resource _resource;
    private bool _disposed;

    public D3D12Texture(ID3D12Resource resource, uint width, uint height,
                        GpuFormat format, Format dxgiFormat, CpuDescriptorHandle rtv,
                        uint bindlessIndex, ResourceStates initialState)
    {
        _resource = resource;
        Width = width;
        Height = height;
        Format = format;
        DxgiFormat = dxgiFormat;
        Rtv = rtv;
        BindlessIndex = bindlessIndex;
        CurrentState = initialState;
    }

    public uint Width { get; }
    public uint Height { get; }
    public GpuFormat Format { get; }
    public uint BindlessIndex { get; }

    internal Format DxgiFormat { get; }
    internal CpuDescriptorHandle Rtv { get; }
    internal CpuDescriptorHandle Dsv { get; set; }
    internal ID3D12Resource Resource => _resource;
    internal ResourceStates CurrentState { get; set; }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _resource.Dispose();
    }
}
