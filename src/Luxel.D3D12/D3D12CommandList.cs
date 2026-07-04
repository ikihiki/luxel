using Luxel.Abstraction;
using Vortice;
using Vortice.Direct3D;
using Vortice.Direct3D12;
using Vortice.Mathematics;

namespace Luxel.D3D12;

internal sealed class D3D12CommandList : IGpuBackendCommandBuffer
{
    private readonly ID3D12Device _device;
    private readonly ID3D12RootSignature _rootSignature;
    private readonly ID3D12DescriptorHeap _resourceHeap;
    private readonly ID3D12DescriptorHeap _samplerHeap;
    private ID3D12CommandAllocator _allocator;
    private ID3D12GraphicsCommandList _list;
    private bool _isGraphics;
    private bool _disposed;

    public D3D12CommandList(ID3D12Device device, ID3D12RootSignature rootSignature,
                            ID3D12DescriptorHeap resourceHeap, ID3D12DescriptorHeap samplerHeap)
    {
        _device = device;
        _rootSignature = rootSignature;
        _resourceHeap = resourceHeap;
        _samplerHeap = samplerHeap;
        _allocator = device.CreateCommandAllocator(CommandListType.Direct);
        _list = device.CreateCommandList<ID3D12GraphicsCommandList>(CommandListType.Direct, _allocator, null);
        _list.SetDescriptorHeaps(new[] { resourceHeap, samplerHeap });
    }

    internal ID3D12GraphicsCommandList Handle => _list;

    // 固定ルートシグネチャの全テーブルを束縛する (1=UAV, 2=SRV → リソースヒープ, 3=sampler → サンプラヒープ)。
    private GpuDescriptorHandle ResGpu => _resourceHeap.GetGPUDescriptorHandleForHeapStart();
    private GpuDescriptorHandle SmpGpu => _samplerHeap.GetGPUDescriptorHandleForHeapStart();

    public void SetComputePipeline(IGpuBackendPipeline pipeline)
    {
        _isGraphics = false;
        _list.SetComputeRootSignature(_rootSignature);
        _list.SetComputeRootDescriptorTable(1, ResGpu);
        _list.SetComputeRootDescriptorTable(2, ResGpu);
        _list.SetComputeRootDescriptorTable(3, SmpGpu);
        _list.SetPipelineState(((D3D12Pipeline)pipeline).Handle);
    }

    public void SetGraphicsPipeline(IGpuBackendPipeline pipeline)
    {
        _isGraphics = true;
        _list.SetGraphicsRootSignature(_rootSignature);
        _list.SetGraphicsRootDescriptorTable(1, ResGpu);
        _list.SetGraphicsRootDescriptorTable(2, ResGpu);
        _list.SetGraphicsRootDescriptorTable(3, SmpGpu);
        _list.SetPipelineState(((D3D12Pipeline)pipeline).Handle);
        _list.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
    }

    public unsafe void SetRootConstants(ReadOnlySpan<byte> data)
    {
        uint dwords = (uint)(data.Length / 4);
        fixed (byte* p = data)
        {
            if (_isGraphics) _list.SetGraphicsRoot32BitConstants(0, dwords, (nint)p, 0);
            else _list.SetComputeRoot32BitConstants(0, dwords, (nint)p, 0);
        }
    }

    public void Dispatch(uint groupCountX, uint groupCountY, uint groupCountZ)
        => _list.Dispatch(groupCountX, groupCountY, groupCountZ);

    public void BeginRendering(IGpuBackendTexture color, IGpuBackendTexture? depth,
        float r, float g, float b, float a, float clearDepth)
    {
        var tex = (D3D12Texture)color;
        Transition(tex, ResourceStates.RenderTarget);

        if (depth is not null)
        {
            var dtex = (D3D12Texture)depth;
            _list.OMSetRenderTargets(tex.Rtv, dtex.Dsv);
            _list.ClearDepthStencilView(dtex.Dsv, ClearFlags.Depth, clearDepth, 0);
        }
        else
        {
            _list.OMSetRenderTargets(tex.Rtv, null);
        }
        _list.ClearRenderTargetView(tex.Rtv, new Color4(r, g, b, a));
        _list.RSSetViewport(0, 0, tex.Width, tex.Height, 0, 1);
        _list.RSSetScissorRect(new RawRect(0, 0, (int)tex.Width, (int)tex.Height));
    }

    public void EndRendering() { /* D3D12 はレンダーパスオブジェクト不要 */ }

    public void Draw(uint vertexCount, uint instanceCount)
        => _list.DrawInstanced(vertexCount, instanceCount, 0, 0);

    public unsafe void CopyTextureToBuffer(IGpuBackendTexture source, IGpuBackendBuffer destination)
    {
        var tex = (D3D12Texture)source;
        var buf = (D3D12Buffer)destination;
        Transition(tex, ResourceStates.CopySource);

        ResourceDescription desc = tex.Resource.Description;
        Span<PlacedSubresourceFootPrint> footprints = stackalloc PlacedSubresourceFootPrint[1];
        Span<uint> numRows = stackalloc uint[1];
        Span<ulong> rowSizes = stackalloc ulong[1];
        _device.GetCopyableFootprints(desc, 0, 1, 0, footprints, numRows, rowSizes, out _);

        var dst = new TextureCopyLocation(buf.Resource, footprints[0]);
        var src = new TextureCopyLocation(tex.Resource, 0);
        _list.CopyTextureRegion(dst, 0, 0, 0, src, null);

        // 実装上の罠 (RG-M5): D3D12 では implicit promotion で buf が COPY_DEST に昇格しているため、
        // 後続の compute UAV/SRV 読みには明示的 transition が必要。COMMON に戻して decay させ、
        // 後続の使用で再 promotion させる (この遷移自体がメモリ可視性の sync として機能)。
        _list.ResourceBarrierTransition(buf.Resource, ResourceStates.CopyDest, ResourceStates.Common);
    }

    public void CopyBufferToBuffer(IGpuBackendBuffer source, IGpuBackendBuffer destination, ulong bytes)
    {
        var src = (D3D12Buffer)source;
        var dst = (D3D12Buffer)destination;
        // バッファは COMMON から COPY_SOURCE/COPY_DEST へ暗黙昇格する (別サブミット前提 —
        // 同一リスト内で直前に UAV 書きしたバッファをコピーする場合は明示 transition が要る)。
        _list.CopyBufferRegion(dst.Resource, 0, src.Resource, 0, bytes);
    }

    public void Barrier(GpuStage source, GpuStage destination, GpuHazard hazard)
        => _list.ResourceBarrierUnorderedAccessView(null!);

    public void Finish() => _list.Close();

    private void Transition(D3D12Texture tex, ResourceStates to)
    {
        if (tex.CurrentState == to) return;
        _list.ResourceBarrierTransition(tex.Resource, tex.CurrentState, to);
        tex.CurrentState = to;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _list.Dispose();
        _allocator.Dispose();
    }
}
