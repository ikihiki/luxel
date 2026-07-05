using Luxel.Abstraction;
using Vortice.Direct3D12;
using Vortice.DXGI;

namespace Luxel.D3D12;

/// <summary>
/// DXGI スワップチェーン (FlipDiscard)。RGBA8 バッファを CopyTextureRegion でバックバッファへ写し present。
/// ソース行ピッチは 256B 整列 (呼び出し側が幅を 64 の倍数にパディングして確保)。
/// </summary>
internal sealed unsafe class D3D12Surface : IGpuBackendSurface
{
    private const int BufferCount = 2;
    private static readonly Format Fmt = Format.R8G8B8A8_UNorm;

    private readonly ID3D12Device _device;
    private readonly ID3D12CommandQueue _queue;
    private IDXGISwapChain3 _swap;
    private ID3D12Resource[] _backbuffers = [];
    private readonly ID3D12CommandAllocator _alloc;
    private readonly ID3D12GraphicsCommandList _list;
    private readonly ID3D12Fence _fence;
    private ulong _fenceValue;
    private uint _w, _h;
    private bool _disposed;

    public D3D12Surface(IDXGIFactory4 factory, ID3D12Device device, ID3D12CommandQueue queue, nint hwnd, uint w, uint h)
    {
        _device = device; _queue = queue; _w = w; _h = h;

        var desc = new SwapChainDescription1
        {
            Width = w,
            Height = h,
            Format = Fmt,
            Stereo = false,
            SampleDescription = new SampleDescription(1, 0),
            BufferUsage = Usage.RenderTargetOutput,
            BufferCount = BufferCount,
            Scaling = Scaling.Stretch,
            SwapEffect = SwapEffect.FlipDiscard,
            AlphaMode = AlphaMode.Ignore,
            Flags = SwapChainFlags.None,
        };
        using IDXGISwapChain1 sc1 = factory.CreateSwapChainForHwnd(_queue, hwnd, desc);
        _swap = sc1.QueryInterface<IDXGISwapChain3>();
        factory.MakeWindowAssociation(hwnd, WindowAssociationFlags.IgnoreAltEnter);
        GetBuffers();

        _alloc = _device.CreateCommandAllocator(CommandListType.Direct);
        _list = _device.CreateCommandList<ID3D12GraphicsCommandList>(CommandListType.Direct, _alloc, null);
        _list.Close();
        _fence = _device.CreateFence(0, FenceFlags.None);
    }

    private void GetBuffers()
    {
        _backbuffers = new ID3D12Resource[BufferCount];
        for (int i = 0; i < BufferCount; i++) _backbuffers[i] = _swap.GetBuffer<ID3D12Resource>((uint)i);
    }

    private void ReleaseBuffers()
    {
        foreach (ID3D12Resource b in _backbuffers) b.Dispose();
        _backbuffers = [];
    }

    private void WaitIdle()
    {
        ulong target = ++_fenceValue;
        _queue.Signal(_fence, target);
        while (_fence.CompletedValue < target) Thread.Yield();
    }

    public void Present(IGpuBackendBuffer source, uint srcStridePixels, uint width, uint height)
    {
        ID3D12Resource src = ((D3D12Buffer)source).Resource;
        int idx = (int)_swap.CurrentBackBufferIndex;
        ID3D12Resource back = _backbuffers[idx];

        _alloc.Reset();
        _list.Reset(_alloc, null);
        _list.ResourceBarrierTransition(back, ResourceStates.Present, ResourceStates.CopyDest);

        var footprint = new PlacedSubresourceFootPrint
        {
            Offset = 0,
            Footprint = new SubresourceFootPrint(Fmt, Math.Min(width, _w), Math.Min(height, _h), 1u, srcStridePixels * 4),
        };
        var dst = new TextureCopyLocation(back, 0);
        var srcLoc = new TextureCopyLocation(src, footprint);
        _list.CopyTextureRegion(dst, 0, 0, 0, srcLoc, null);

        _list.ResourceBarrierTransition(back, ResourceStates.CopyDest, ResourceStates.Present);
        _list.Close();
        _queue.ExecuteCommandList(_list);
        _swap.Present(1, PresentFlags.None);
        WaitIdle();
    }

    public void Resize(uint width, uint height)
    {
        if (width == 0 || height == 0) return;
        WaitIdle();
        ReleaseBuffers();
        _swap.ResizeBuffers(BufferCount, width, height, Fmt, SwapChainFlags.None);
        _w = width; _h = height;
        GetBuffers();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        WaitIdle();
        ReleaseBuffers();
        _list.Dispose();
        _alloc.Dispose();
        _fence.Dispose();
        _swap.Dispose();
    }
}
