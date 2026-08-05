using Luxel.Graphics.Abstraction;
using Silk.NET.WebGPU;
using WgpuBuffer = Silk.NET.WebGPU.Buffer;
using WebGpuApi = Silk.NET.WebGPU.WebGPU;

namespace Luxel.Graphics.WebGPU;

internal sealed unsafe class WebGpuCommandBuffer : IGpuBackendCommandBuffer
{
    private readonly WebGpuBackend _backend;
    private readonly WebGpuApi _api;
    private CommandEncoder* _encoder;
    private WgpuBuffer* _rootBuffer;
    private BindGroup* _computeBindGroup;
    private BindGroup* _graphicsBindGroup;
    private BindGroup* _resourceBindGroup;
    private ComputePassEncoder* _computePass;
    private RenderPassEncoder* _renderPass;
    private WebGpuPipeline? _graphicsPipeline;
    private CommandBuffer* _commandBuffer;
    private readonly List<nint> _temporaryBuffers = [];
    private readonly HashSet<WebGpuBuffer> _referencedBuffers = [];
    private readonly List<WebGpuTexture> _referencedTextures = [];
    private readonly List<WebGpuSampler> _referencedSamplers = [];
    private readonly byte[] _rootData = new byte[WebGpuBackend.RootBufferSize];
    private uint _rootOffset;
    private uint _currentRootOffset;
    private bool _finished;
    private bool _submitted;
    private bool _disposed;
    private bool _backendCommandCompleted;

    internal WebGpuCommandBuffer(WebGpuBackend backend)
    {
        _backend = backend;
        _api = backend.Api;
        var encoderDescriptor = new CommandEncoderDescriptor();
        _encoder = _api.DeviceCreateCommandEncoder(backend.Device, in encoderDescriptor);
        if (_encoder == null) throw new InvalidOperationException("Failed to create WebGPU command encoder.");
        var rootDescriptor = new BufferDescriptor { Size = WebGpuBackend.RootBufferSize, Usage = BufferUsage.Uniform | BufferUsage.CopyDst };
        _rootBuffer = _api.DeviceCreateBuffer(backend.Device, in rootDescriptor);
        if (_rootBuffer == null) throw new InvalidOperationException("Failed to create WebGPU root constant buffer.");
        _computeBindGroup = backend.CreateCommandBindGroup(_rootBuffer, true);
        _graphicsBindGroup = backend.CreateCommandBindGroup(_rootBuffer, false);
        _resourceBindGroup = backend.CreateResourceBindGroup(_referencedTextures, _referencedSamplers);
        backend.RegisterCommand();
    }

    internal WebGpuBackend Owner => _backend;
    internal bool IsDisposed => _disposed;
    internal bool IsFinished => _finished && _commandBuffer != null;
    internal bool IsSubmitted => _submitted;
    internal CommandBuffer* Handle { get { ObjectDisposedException.ThrowIf(_disposed, this); return _commandBuffer; } }

    internal void UploadRoots(Queue* queue)
    {
        if (_rootOffset == 0) return;
        fixed (byte* data = _rootData)
            _api.QueueWriteBuffer(queue, _rootBuffer, 0, data, _rootOffset);
    }

    public void SetComputePipeline(IGpuBackendPipeline pipeline)
    {
        EnsureRecording();
        EndPasses();
        var webPipeline = RequirePipeline(pipeline, true);
        var descriptor = new ComputePassDescriptor();
        _computePass = _api.CommandEncoderBeginComputePass(_encoder, in descriptor);
        _api.ComputePassEncoderSetPipeline(_computePass, webPipeline.Compute);
    }

    public void SetRootConstants(ReadOnlySpan<byte> data)
    {
        EnsureRecording();
        if (data.Length > 256) throw new ArgumentOutOfRangeException(nameof(data), "WebGPU root constants are limited to 256 bytes.");
        uint offset = checked((uint)WebGpuBackend.AlignUp(_rootOffset, 256));
        if ((ulong)offset + 256 > WebGpuBackend.RootBufferSize) throw new InvalidOperationException("Per-command root constant buffer exhausted.");
        data.CopyTo(_rootData.AsSpan((int)offset, data.Length));
        _currentRootOffset = offset;
        _rootOffset = offset + 256;
    }

    public void Dispatch(uint groupCountX, uint groupCountY, uint groupCountZ)
    {
        EnsureRecording();
        if (_computePass == null) throw new InvalidOperationException("SetComputePipeline must be called before Dispatch.");
        uint dynamicOffset = _currentRootOffset;
        _api.ComputePassEncoderSetBindGroup(_computePass, 0, _computeBindGroup, 1, in dynamicOffset);
        _api.ComputePassEncoderSetBindGroup(_computePass, 1, _resourceBindGroup, 0, null);
        _api.ComputePassEncoderDispatchWorkgroups(_computePass, groupCountX, groupCountY, groupCountZ);
    }

    public void SetGraphicsPipeline(IGpuBackendPipeline pipeline)
    {
        EnsureRecording();
        _graphicsPipeline = RequirePipeline(pipeline, false);
        if (_computePass != null) EndComputePass();
        if (_renderPass != null) _api.RenderPassEncoderSetPipeline(_renderPass, _graphicsPipeline.Render);
    }

    public void BeginRendering(IGpuBackendTexture color, IGpuBackendTexture? depth,
        float r, float g, float b, float a, float clearDepth)
    {
        EnsureRecording();
        EndPasses();
        var colorTexture = RequireTexture(color);
        var colorAttachment = new RenderPassColorAttachment
        {
            View = colorTexture.View,
            DepthSlice = WebGpuApi.DepthSliceUndefined,
            LoadOp = LoadOp.Clear,
            StoreOp = StoreOp.Store,
            ClearValue = new Color { R = r, G = g, B = b, A = a },
        };
        RenderPassDepthStencilAttachment depthAttachment = default;
        if (depth is not null)
        {
            var depthTexture = RequireTexture(depth);
            depthAttachment = new RenderPassDepthStencilAttachment
            {
                View = depthTexture.View,
                DepthLoadOp = LoadOp.Clear,
                DepthStoreOp = StoreOp.Store,
                DepthClearValue = clearDepth,
                StencilLoadOp = LoadOp.Undefined,
                StencilStoreOp = StoreOp.Undefined,
            };
        }
        var descriptor = new RenderPassDescriptor
        {
            ColorAttachmentCount = 1,
            ColorAttachments = &colorAttachment,
            DepthStencilAttachment = depth is null ? null : &depthAttachment,
        };
        _renderPass = _api.CommandEncoderBeginRenderPass(_encoder, in descriptor);
        if (_graphicsPipeline is not null) _api.RenderPassEncoderSetPipeline(_renderPass, _graphicsPipeline.Render);
        _api.RenderPassEncoderSetViewport(_renderPass, 0, 0, colorTexture.Width, colorTexture.Height, 0, 1);
        _api.RenderPassEncoderSetScissorRect(_renderPass, 0, 0, colorTexture.Width, colorTexture.Height);
    }

    public void EndRendering()
    {
        if (_renderPass == null) throw new InvalidOperationException("No render pass is active.");
        _api.RenderPassEncoderEnd(_renderPass);
        _api.RenderPassEncoderRelease(_renderPass);
        _renderPass = null;
    }

    public void Draw(uint vertexCount, uint instanceCount)
    {
        EnsureRecording();
        if (_renderPass == null) throw new InvalidOperationException("BeginRendering must be called before Draw.");
        if (_graphicsPipeline is null) throw new InvalidOperationException("SetGraphicsPipeline must be called before Draw.");
        uint dynamicOffset = _currentRootOffset;
        _api.RenderPassEncoderSetBindGroup(_renderPass, 0, _graphicsBindGroup, 1, in dynamicOffset);
        _api.RenderPassEncoderSetBindGroup(_renderPass, 1, _resourceBindGroup, 0, null);
        _api.RenderPassEncoderDraw(_renderPass, vertexCount, instanceCount, 0, 0);
    }

    public void CopyTextureToBuffer(IGpuBackendTexture source, IGpuBackendBuffer destination, uint rowLengthPixels)
    {
        EnsureRecording();
        EndPasses();
        var texture = RequireTexture(source);
        var buffer = RequireBuffer(destination);
        if (texture.Format == GpuFormat.D32Float)
            throw new NotSupportedException("Depth texture readback is not supported.");
        uint bytesPerPixel = GpuFormatInfo.BytesPerPixel(texture.Format);
        uint rowPixels = rowLengthPixels == 0 ? texture.Width : rowLengthPixels;
        if (rowPixels < texture.Width)
            throw new ArgumentOutOfRangeException(nameof(rowLengthPixels), "Destination row length cannot be smaller than the texture width.");
        uint bytesPerRow = checked(rowPixels * bytesPerPixel);
        uint copiedRowBytes = checked(texture.Width * bytesPerPixel);
        if (texture.Height > 1 && (bytesPerRow & 255) != 0)
            throw new ArgumentException("WebGPU texture readback rows must be 256-byte aligned; provide rowLengthPixels accordingly.", nameof(rowLengthPixels));
        ulong requiredSize = checked((ulong)bytesPerRow * (texture.Height - 1) + copiedRowBytes);
        if (requiredSize > buffer.Size) throw new ArgumentException("Destination buffer is too small.", nameof(destination));
        var src = new ImageCopyTexture { Texture = texture.Handle, Aspect = TextureAspect.All };
        var dst = new ImageCopyBuffer
        {
            Buffer = _backend.Arena,
            Layout = new TextureDataLayout { Offset = buffer.Offset, BytesPerRow = texture.Height == 1 ? uint.MaxValue : bytesPerRow, RowsPerImage = texture.Height == 1 ? uint.MaxValue : texture.Height },
        };
        var extent = new Extent3D { Width = texture.Width, Height = texture.Height, DepthOrArrayLayers = 1 };
        _api.CommandEncoderCopyTextureToBuffer(_encoder, in src, in dst, in extent);
    }

    public void CopyBufferToBuffer(IGpuBackendBuffer source, IGpuBackendBuffer destination, ulong bytes)
    {
        EnsureRecording();
        EndPasses();
        var src = RequireBuffer(source);
        var dst = RequireBuffer(destination);
        if (bytes > src.Size || bytes > dst.Size) throw new ArgumentOutOfRangeException(nameof(bytes));
        if (bytes == 0) throw new ArgumentOutOfRangeException(nameof(bytes));
        ulong copySize = WebGpuBackend.CheckedAlignUp(bytes, 4);
        var descriptor = new BufferDescriptor { Size = copySize, Usage = BufferUsage.CopySrc | BufferUsage.CopyDst };
        WgpuBuffer* temporary = _api.DeviceCreateBuffer(_backend.Device, in descriptor);
        if (temporary == null) throw new InvalidOperationException("Failed to create temporary WebGPU copy buffer.");
        _temporaryBuffers.Add((nint)temporary);
        _api.CommandEncoderCopyBufferToBuffer(_encoder, _backend.Arena, src.Offset, temporary, 0, copySize);
        _api.CommandEncoderCopyBufferToBuffer(_encoder, temporary, 0, _backend.Arena, dst.Offset, copySize);
    }

    public void Barrier(GpuStage source, GpuStage destination, GpuHazard hazard)
    {
        // WebGPU inserts resource transitions and pass-boundary synchronization automatically.
        EndPasses();
    }

    public void Finish()
    {
        EnsureRecording();
        EndPasses();
        var descriptor = new CommandBufferDescriptor();
        _commandBuffer = _api.CommandEncoderFinish(_encoder, in descriptor);
        if (_commandBuffer == null) throw new InvalidOperationException("Failed to finish WebGPU command buffer.");
        _finished = true;
        _backend.ProcessEventsAndThrowValidationErrors("command buffer finish");
    }

    private void EndComputePass()
    {
        _api.ComputePassEncoderEnd(_computePass);
        _api.ComputePassEncoderRelease(_computePass);
        _computePass = null;
    }

    private void EndPasses()
    {
        if (_computePass != null) EndComputePass();
        if (_renderPass != null)
        {
            _api.RenderPassEncoderEnd(_renderPass);
            _api.RenderPassEncoderRelease(_renderPass);
            _renderPass = null;
        }
    }

    private void EnsureRecording()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _backend.ThrowIfDisposed();
        if (_finished) throw new InvalidOperationException("The command buffer is already finished.");
    }

    private WebGpuPipeline RequirePipeline(IGpuBackendPipeline pipeline, bool compute)
    {
        if (pipeline is not WebGpuPipeline value || !ReferenceEquals(value.Owner, _backend))
            throw new ArgumentException("Pipeline belongs to another backend.", nameof(pipeline));
        value.ThrowIfDisposed();
        if (value.IsCompute != compute) throw new ArgumentException("Pipeline has the wrong type.", nameof(pipeline));
        return value;
    }

    private WebGpuTexture RequireTexture(IGpuBackendTexture texture)
    {
        if (texture is not WebGpuTexture value || !ReferenceEquals(value.Owner, _backend))
            throw new ArgumentException("Texture belongs to another backend.", nameof(texture));
        value.ThrowIfDisposed();
        return value;
    }

    private WebGpuBuffer RequireBuffer(IGpuBackendBuffer buffer)
    {
        if (buffer is not WebGpuBuffer value || !ReferenceEquals(value.Owner, _backend))
            throw new ArgumentException("Buffer belongs to another backend.", nameof(buffer));
        value.ThrowIfDisposed();
        if (_referencedBuffers.Add(value)) value.AddReference();
        return value;
    }

    internal void MarkSubmitted()
    {
        if (_submitted) throw new InvalidOperationException("A WebGPU command buffer can only be submitted once.");
        _submitted = true;
    }

    internal void ReleaseReferencesAfterSubmit()
    {
        if (_resourceBindGroup != null && !_backend.IsDisposed)
            _api.BindGroupRelease(_resourceBindGroup);
        _resourceBindGroup = null;
        foreach (WebGpuBuffer buffer in _referencedBuffers) buffer.ReleaseReference();
        foreach (WebGpuTexture texture in _referencedTextures) texture.ReleaseReference();
        foreach (WebGpuSampler sampler in _referencedSamplers) sampler.ReleaseReference();
        _referencedBuffers.Clear();
        _referencedTextures.Clear();
        _referencedSamplers.Clear();
        CompleteBackendCommand();
    }

    private void CompleteBackendCommand()
    {
        if (_backendCommandCompleted) return;
        _backendCommandCompleted = true;
        _backend.CompleteCommand();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (!_backend.IsDisposed)
        {
            EndPasses();
            if (_commandBuffer != null) _api.CommandBufferRelease(_commandBuffer);
            if (_computeBindGroup != null) _api.BindGroupRelease(_computeBindGroup);
            if (_graphicsBindGroup != null) _api.BindGroupRelease(_graphicsBindGroup);
            if (_rootBuffer != null) { _api.BufferDestroy(_rootBuffer); _api.BufferRelease(_rootBuffer); }
            if (_encoder != null) _api.CommandEncoderRelease(_encoder);
            foreach (nint handle in _temporaryBuffers) { var temporary = (WgpuBuffer*)handle; _api.BufferDestroy(temporary); _api.BufferRelease(temporary); }
        }
        _temporaryBuffers.Clear();
        ReleaseReferencesAfterSubmit();
        CompleteBackendCommand();
        _commandBuffer = null; _computeBindGroup = null; _graphicsBindGroup = null; _rootBuffer = null; _encoder = null;
    }
}
