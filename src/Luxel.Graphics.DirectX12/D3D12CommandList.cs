using Luxel.Graphics.Abstraction;
using Vortice;
using Vortice.Direct3D;
using Vortice.Direct3D12;
using Vortice.Mathematics;

namespace Luxel.Graphics.DirectX12;

internal sealed class D3D12CommandList : IGpuBackendCommandBuffer
{
    private readonly ID3D12Device _device;
    private readonly ID3D12RootSignature _rootSignature;
    private readonly ID3D12DescriptorHeap _resourceHeap;
    private readonly ID3D12DescriptorHeap _samplerHeap;
    private ID3D12CommandAllocator _allocator;
    private ID3D12GraphicsCommandList _list;
    private IGpuBackendPipeline? _graphicsPipeline;
    private GpuRasterizerState _rasterizer = GpuRasterizerState.Default;
    private GpuDepthStencilState _depthStencil = GpuDepthStencilState.Default;
    private GpuBlendState _blend = GpuBlendState.None;
    private uint _renderWidth, _renderHeight;
    private GpuFormat _colorFormat;
    private GpuFormat? _depthFormat;
    private bool _rendering;
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
        _graphicsPipeline = pipeline;
        _list.SetGraphicsRootSignature(_rootSignature);
        _list.SetGraphicsRootDescriptorTable(1, ResGpu);
        _list.SetGraphicsRootDescriptorTable(2, ResGpu);
        _list.SetGraphicsRootDescriptorTable(3, SmpGpu);
    }
    public void SetRasterizerState(GpuRasterizerState state) => _rasterizer = state;
    public void SetDepthStencilState(GpuDepthStencilState state) => _depthStencil = state.Normalize();
    public void SetBlendState(GpuBlendState state) => _blend = state;
    public void SetStencilReference(uint reference)
    {
        if (!_rendering) throw new InvalidOperationException("Stencil reference can only be set during rendering.");
        _list.OMSetStencilRef(reference);
    }
    public void SetViewport(GpuViewport value)
    {
        ValidateViewport(value);
        _list.RSSetViewport(value.X, value.Y, value.Width, value.Height, value.MinDepth, value.MaxDepth);
    }
    public void SetScissor(GpuScissorRect value)
    {
        ValidateScissor(value);
        _list.RSSetScissorRect(new RawRect((int)value.X, (int)value.Y, checked((int)(value.X + value.Width)), checked((int)(value.Y + value.Height))));
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
        float r, float g, float b, float a, float clearDepth, uint clearStencil)
    {
        var tex = (D3D12Texture)color;
        Transition(tex, ResourceStates.RenderTarget);

        if (depth is not null)
        {
            var dtex = (D3D12Texture)depth;
            _list.OMSetRenderTargets(tex.Rtv, dtex.Dsv);
            ClearFlags flags = ClearFlags.Depth | (GpuFormatInfo.HasStencil(dtex.Format) ? ClearFlags.Stencil : 0);
            _list.ClearDepthStencilView(dtex.Dsv, flags, clearDepth, (byte)clearStencil);
        }
        else
        {
            _list.OMSetRenderTargets(tex.Rtv, null);
        }
        _list.ClearRenderTargetView(tex.Rtv, new Color4(r, g, b, a));
        _renderWidth = tex.Width; _renderHeight = tex.Height; _colorFormat = tex.Format; _depthFormat = depth?.Format; _rendering = true;
        SetViewport(new GpuViewport(0, 0, tex.Width, tex.Height));
        SetScissor(new GpuScissorRect(0, 0, tex.Width, tex.Height));
        _list.OMSetStencilRef(0);
    }

    public void EndRendering() { _rendering = false; }

    public void Draw(uint vertexCount, uint instanceCount)
    {
        if (!_rendering) throw new InvalidOperationException("BeginRendering must be called before Draw.");
        if (_graphicsPipeline is null) throw new InvalidOperationException("A graphics pipeline must be set before Draw.");
        GpuAttachmentLayout layout = _graphicsPipeline.GraphicsDescription!.Value.Attachments;
        if (layout.ColorFormat != _colorFormat || layout.DepthStencilFormat != _depthFormat) throw new InvalidOperationException("Bound attachments do not match the graphics pipeline attachment layout.");
        var variant = (D3D12Pipeline)_graphicsPipeline.ResolveGraphicsVariant(_rasterizer, _depthStencil, _blend);
        _list.SetPipelineState(variant.Handle);
        GpuPrimitiveTopology topology = _graphicsPipeline.GraphicsDescription!.Value.Topology;
        _list.IASetPrimitiveTopology(topology == GpuPrimitiveTopology.TriangleStrip ? PrimitiveTopology.TriangleStrip : PrimitiveTopology.TriangleList);
        _list.DrawInstanced(vertexCount, instanceCount, 0, 0);
    }

    private void ValidateViewport(GpuViewport value)
    {
        if (!_rendering) throw new InvalidOperationException("Viewport can only be set during rendering.");
        if (!float.IsFinite(value.X) || !float.IsFinite(value.Y) || !float.IsFinite(value.Width) || !float.IsFinite(value.Height) || value.Width <= 0 || value.Height <= 0 || value.X < 0 || value.Y < 0 || value.X + value.Width > _renderWidth || value.Y + value.Height > _renderHeight || value.MinDepth < 0 || value.MaxDepth > 1 || value.MinDepth > value.MaxDepth)
            throw new ArgumentOutOfRangeException(nameof(value));
    }
    private void ValidateScissor(GpuScissorRect value)
    {
        if (!_rendering) throw new InvalidOperationException("Scissor can only be set during rendering.");
        if (value.Width == 0 || value.Height == 0 || (ulong)value.X + value.Width > _renderWidth || (ulong)value.Y + value.Height > _renderHeight)
            throw new ArgumentOutOfRangeException(nameof(value));
    }

    public unsafe void CopyTextureToBuffer(IGpuBackendTexture source, IGpuBackendBuffer destination, uint rowLengthPixels)
    {
        _ = rowLengthPixels; // D3D12 uses GetCopyableFootprints; callers pass its 256-byte-aligned equivalent.
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
