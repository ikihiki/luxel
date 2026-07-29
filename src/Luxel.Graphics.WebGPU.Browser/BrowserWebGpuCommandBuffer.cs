using Luxel.Graphics.Abstraction;

namespace Luxel.Graphics.WebGPU.Browser;

internal sealed class BrowserWebGpuCommandBuffer : BrowserWebGpuHandle, IGpuBackendCommandBuffer
{
    private bool _finished;
    private bool _submitted;
    private bool _rendering;
    private bool _computePipeline;
    private bool _graphicsPipeline;

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
        var value = Owner.RequirePipeline(pipeline, false);
        Owner.Interop.CommandSetGraphicsPipeline(Handle, value.Handle);
        _graphicsPipeline = true;
    }

    public void BeginRendering(IGpuBackendTexture color, IGpuBackendTexture? depth, float r, float g, float b, float a, float clearDepth)
    {
        EnsureRecording();
        if (_rendering) throw new InvalidOperationException("A render pass is already active.");
        BrowserWebGpuTexture colorValue = Owner.RequireTexture(color, nameof(color));
        if (colorValue.Format == GpuFormat.D32Float) throw new ArgumentException("Color target cannot use a depth format.", nameof(color));
        BrowserWebGpuTexture? depthValue = depth is null ? null : Owner.RequireTexture(depth, nameof(depth));
        if (depthValue is not null && depthValue.Format != GpuFormat.D32Float) throw new ArgumentException("Depth target must use D32Float.", nameof(depth));
        if (depthValue is not null && (depthValue.Width != colorValue.Width || depthValue.Height != colorValue.Height))
            throw new ArgumentException("Color and depth target dimensions must match.", nameof(depth));
        Owner.Interop.CommandBeginRendering(Handle, colorValue.Handle, depthValue?.Handle ?? 0, r, g, b, a, clearDepth);
        _rendering = true;
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
        if (!_graphicsPipeline) throw new InvalidOperationException("SetGraphicsPipeline must be called before Draw.");
        if (vertexCount == 0 || instanceCount == 0) throw new ArgumentOutOfRangeException(nameof(vertexCount));
        Owner.Interop.CommandDraw(Handle, CheckedInt(vertexCount), CheckedInt(instanceCount));
    }

    public void CopyTextureToBuffer(IGpuBackendTexture source, IGpuBackendBuffer destination, uint rowLengthPixels)
    {
        EnsureRecording();
        if (_rendering) throw new InvalidOperationException("EndRendering must be called before copy operations.");
        BrowserWebGpuTexture texture = Owner.RequireTexture(source, nameof(source));
        BrowserWebGpuBuffer buffer = Owner.RequireBuffer(destination, nameof(destination));
        uint bytesPerPixel = texture.Format == GpuFormat.D32Float ? throw new NotSupportedException("Depth texture readback is unsupported.") : 4u;
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
