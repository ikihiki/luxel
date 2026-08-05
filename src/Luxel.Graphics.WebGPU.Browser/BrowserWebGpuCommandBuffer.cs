using Luxel.Graphics.Abstraction;

namespace Luxel.Graphics.WebGPU.Browser;

internal sealed class BrowserWebGpuCommandBuffer : BrowserWebGpuHandle, IGpuBackendCommandBuffer
{
    private bool _finished;
    private bool _submitted;
    private bool _rendering;
    private bool _computePipeline;
    private BrowserWebGpuPipeline? _graphicsPipeline;
    private GpuRasterizerState _rasterizer = GpuRasterizerState.Default;
    private GpuDepthStencilState _depthStencil = GpuDepthStencilState.Default;
    private GpuBlendState _blend = GpuBlendState.None;
    private uint _renderWidth, _renderHeight;
    private GpuFormat _colorFormat;
    private GpuFormat? _depthFormat;

    internal BrowserWebGpuCommandBuffer(BrowserWebGpuBackend owner) : base(owner, owner.Interop.CreateCommandBuffer(owner.Handle)) { }
    internal bool IsFinished => _finished;
    internal bool IsSubmitted => _submitted;

    public void SetComputePipeline(IGpuBackendPipeline pipeline)
    {
        EnsureRecording();
        var value = Owner.RequirePipeline(pipeline, true);
        Owner.Interop.CommandSetComputePipeline(Handle, value.Handle);
        _computePipeline = true;
    }

    public void SetRootConstants(ReadOnlySpan<byte> data)
    {
        EnsureRecording();
        if (data.Length > BrowserWebGpuBackend.RootDataSize)
            throw new ArgumentOutOfRangeException(nameof(data), $"Browser WebGPU root data is limited to {BrowserWebGpuBackend.RootDataSize} bytes ({BrowserWebGpuBackend.RootBindingSize}-byte binding stride).");
        Owner.Interop.CommandSetRootConstants(Handle, Convert.ToBase64String(data));
    }

    public void Dispatch(uint groupCountX, uint groupCountY, uint groupCountZ)
    {
        EnsureRecording();
        if (!_computePipeline) throw new InvalidOperationException("SetComputePipeline must be called before Dispatch.");
        if (groupCountX == 0 || groupCountY == 0 || groupCountZ == 0) throw new ArgumentOutOfRangeException(nameof(groupCountX));
        Owner.Interop.CommandDispatch(Handle, CheckedInt(groupCountX), CheckedInt(groupCountY), CheckedInt(groupCountZ));
    }

    public void SetGraphicsPipeline(IGpuBackendPipeline pipeline)
    {
        EnsureRecording();
        _graphicsPipeline = Owner.RequirePipeline(pipeline, false);
    }
    public void SetRasterizerState(GpuRasterizerState state) => _rasterizer = state;
    public void SetDepthStencilState(GpuDepthStencilState state) => _depthStencil = state.Normalize();
    public void SetBlendState(GpuBlendState state) => _blend = state;
    public void SetStencilReference(uint reference)
    {
        if (!_rendering) throw new InvalidOperationException("Stencil reference can only be set during rendering.");
        Owner.Interop.CommandSetStencilReference(Handle, CheckedInt(reference));
    }
    public void SetViewport(GpuViewport value)
    {
        ValidateViewport(value);
        Owner.Interop.CommandSetViewport(Handle, value.X, value.Y, value.Width, value.Height, value.MinDepth, value.MaxDepth);
    }
    public void SetScissor(GpuScissorRect value)
    {
        ValidateScissor(value);
        Owner.Interop.CommandSetScissor(Handle, CheckedInt(value.X), CheckedInt(value.Y), CheckedInt(value.Width), CheckedInt(value.Height));
    }

    public void BeginRendering(IGpuBackendTexture color, IGpuBackendTexture? depth, float r, float g, float b, float a, float clearDepth, uint clearStencil)
    {
        EnsureRecording();
        if (_rendering) throw new InvalidOperationException("A render pass is already active.");
        BrowserWebGpuTexture colorValue = Owner.RequireTexture(color, nameof(color));
        if (!GpuFormatInfo.IsColor(colorValue.Format)) throw new ArgumentException("Color target must use a color format.", nameof(color));
        BrowserWebGpuTexture? depthValue = depth is null ? null : Owner.RequireTexture(depth, nameof(depth));
        if (depthValue is not null && !GpuFormatInfo.IsDepthStencilAttachment(depthValue.Format)) throw new ArgumentException("Depth target must use a depth-stencil format.", nameof(depth));
        if (depthValue is not null && (depthValue.Width != colorValue.Width || depthValue.Height != colorValue.Height))
            throw new ArgumentException("Color and depth target dimensions must match.", nameof(depth));
        Owner.Interop.CommandBeginRendering(Handle, colorValue.Handle, depthValue?.Handle ?? 0, r, g, b, a, clearDepth, CheckedInt(clearStencil));
        _renderWidth = colorValue.Width; _renderHeight = colorValue.Height; _colorFormat = colorValue.Format; _depthFormat = depthValue?.Format; _rendering = true;
        SetViewport(new GpuViewport(0, 0, colorValue.Width, colorValue.Height));
        SetScissor(new GpuScissorRect(0, 0, colorValue.Width, colorValue.Height));
        Owner.Interop.CommandSetStencilReference(Handle, 0);
    }

    public void EndRendering()
    {
        EnsureRecording();
        if (!_rendering) throw new InvalidOperationException("No render pass is active.");
        Owner.Interop.CommandEndRendering(Handle);
        _rendering = false;
    }

    public void Draw(uint vertexCount, uint instanceCount)
    {
        EnsureRecording();
        if (!_rendering) throw new InvalidOperationException("BeginRendering must be called before Draw.");
        if (_graphicsPipeline is null) throw new InvalidOperationException("SetGraphicsPipeline must be called before Draw.");
        GpuAttachmentLayout layout = _graphicsPipeline.GraphicsDescription!.Value.Attachments;
        if (layout.ColorFormat != _colorFormat || layout.DepthStencilFormat != _depthFormat) throw new InvalidOperationException("Bound attachments do not match the graphics pipeline attachment layout.");
        var variant = (BrowserWebGpuPipeline)_graphicsPipeline.ResolveGraphicsVariant(_rasterizer, _depthStencil, _blend);
        Owner.Interop.CommandSetGraphicsPipeline(Handle, variant.Handle);
        if (vertexCount == 0 || instanceCount == 0) throw new ArgumentOutOfRangeException(nameof(vertexCount));
        Owner.Interop.CommandDraw(Handle, CheckedInt(vertexCount), CheckedInt(instanceCount));
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

    public void CopyTextureToBuffer(IGpuBackendTexture source, IGpuBackendBuffer destination, uint rowLengthPixels)
    {
        EnsureRecording();
        if (_rendering) throw new InvalidOperationException("EndRendering must be called before copy operations.");
        BrowserWebGpuTexture texture = Owner.RequireTexture(source, nameof(source));
        BrowserWebGpuBuffer buffer = Owner.RequireBuffer(destination, nameof(destination));
        if (texture.Format == GpuFormat.D32Float)
            throw new NotSupportedException("Depth texture readback is unsupported.");
        uint bytesPerPixel = GpuFormatInfo.BytesPerPixel(texture.Format);
        uint rowPixels = rowLengthPixels == 0 ? texture.Width : rowLengthPixels;
        if (rowPixels < texture.Width) throw new ArgumentOutOfRangeException(nameof(rowLengthPixels));
        uint bytesPerRow = checked(rowPixels * bytesPerPixel);
        if (texture.Height > 1 && (bytesPerRow & 255) != 0)
            throw new ArgumentException("WebGPU texture readback rows must be 256-byte aligned.", nameof(rowLengthPixels));
        ulong required = checked((ulong)bytesPerRow * (texture.Height - 1) + texture.Width * bytesPerPixel);
        if (required > buffer.Size) throw new ArgumentException("Destination buffer is too small.", nameof(destination));
        Owner.Interop.CommandCopyTextureToBuffer(Handle, texture.Handle, checked((int)buffer.Offset), checked((int)bytesPerRow), checked((int)texture.Width), checked((int)texture.Height));
    }

    public void CopyBufferToBuffer(IGpuBackendBuffer source, IGpuBackendBuffer destination, ulong bytes)
    {
        EnsureRecording();
        if (_rendering) throw new InvalidOperationException("EndRendering must be called before copy operations.");
        BrowserWebGpuBuffer src = Owner.RequireBuffer(source, nameof(source));
        BrowserWebGpuBuffer dst = Owner.RequireBuffer(destination, nameof(destination));
        if (bytes == 0 || bytes > src.Size || bytes > dst.Size) throw new ArgumentOutOfRangeException(nameof(bytes));
        ulong aligned = BrowserWebGpuBackend.AlignUp(bytes, 4);
        if (aligned > src.PhysicalSize || aligned > dst.PhysicalSize) throw new ArgumentOutOfRangeException(nameof(bytes));
        Owner.Interop.CommandCopyBufferToBuffer(Handle, checked((int)src.Offset), checked((int)dst.Offset), checked((int)aligned));
    }

    public void Barrier(GpuStage source, GpuStage destination, GpuHazard hazard)
    {
        EnsureRecording();
        if (_rendering) EndRendering();
        Owner.Interop.CommandBarrier(Handle);
    }

    public void Finish()
    {
        EnsureRecording();
        if (_rendering) EndRendering();
        Owner.Interop.CommandFinish(Handle);
        _finished = true;
    }

    internal void MarkSubmitted()
    {
        ThrowIfDisposed();
        if (!_finished) throw new ArgumentException("A finished Browser WebGPU command buffer is required.");
        if (_submitted) throw new InvalidOperationException("A command buffer can only be submitted once.");
        _submitted = true;
    }

    private void EnsureRecording()
    {
        ThrowIfDisposed(); Owner.ThrowIfDisposed();
        if (_finished) throw new InvalidOperationException("The command buffer is already finished.");
    }
    private static int CheckedInt(uint value) => checked((int)value);
    protected override void Release() => Owner.ReleaseHandle(BrowserHandleKind.Command, Handle);
}
