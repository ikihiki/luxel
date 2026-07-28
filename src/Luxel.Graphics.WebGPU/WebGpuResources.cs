using System.Runtime.InteropServices;
using Luxel.Graphics.Abstraction;
using Silk.NET.WebGPU;
using WgpuBuffer = Silk.NET.WebGPU.Buffer;
using WebGpuApi = Silk.NET.WebGPU.WebGPU;

namespace Luxel.Graphics.WebGPU;

internal sealed unsafe class WebGpuBuffer : IGpuBackendBuffer
{
    private readonly WebGpuBackend _backend;
    private nint _shadow;
    private bool _disposed;

    internal WebGpuBuffer(WebGpuBackend backend, ulong size, ulong offset, uint bindlessIndex, GpuMemoryKind kind)
    {
        _backend = backend;
        Size = size;
        Offset = offset;
        BindlessIndex = bindlessIndex;
        Kind = kind;
        if (kind != GpuMemoryKind.DeviceLocal)
        {
            _shadow = (nint)NativeMemory.AllocZeroed((nuint)size);
            if (_shadow == 0) throw new OutOfMemoryException();
        }
    }

    public ulong Size { get; }
    public ulong DeviceAddress => Offset;
    public uint BindlessIndex { get; }
    public void* MappedPointer => (void*)_shadow;
    internal ulong Offset { get; }
    internal GpuMemoryKind Kind { get; }

    internal void Upload(WebGpuApi api, Queue* queue, WgpuBuffer* arena)
    {
        if (_disposed || Kind != GpuMemoryKind.HostMapped || _shadow == 0) return;
        api.QueueWriteBuffer(queue, arena, Offset, (void*)_shadow, (nuint)Size);
    }

    internal void CopyFromMapped(void* source)
    {
        if (_disposed || Kind != GpuMemoryKind.HostCached || _shadow == 0) return;
        System.Buffer.MemoryCopy(source, (void*)_shadow, Size, Size);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_shadow != 0) { NativeMemory.Free((void*)_shadow); _shadow = 0; }
        _backend.RemoveBuffer(this);
    }
}

internal sealed unsafe class WebGpuTexture : IGpuBackendTexture
{
    private readonly WebGpuApi _api;
    private Texture* _texture;
    private TextureView* _view;
    private bool _disposed;

    internal WebGpuTexture(WebGpuApi api, Texture* texture, TextureView* view, uint width, uint height, GpuFormat format, uint bindlessIndex)
    {
        _api = api; _texture = texture; _view = view;
        Width = width; Height = height; Format = format; BindlessIndex = bindlessIndex;
    }

    public uint Width { get; }
    public uint Height { get; }
    public GpuFormat Format { get; }
    public uint BindlessIndex { get; }
    internal Texture* Handle => _texture;
    internal TextureView* View => _view;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _api.TextureViewRelease(_view);
        _api.TextureDestroy(_texture);
        _api.TextureRelease(_texture);
        _view = null; _texture = null;
    }
}

internal sealed unsafe class WebGpuSampler : IGpuBackendSampler
{
    private readonly WebGpuApi _api;
    private Sampler* _sampler;
    private bool _disposed;
    internal WebGpuSampler(WebGpuApi api, Sampler* sampler, uint index) { _api = api; _sampler = sampler; BindlessIndex = index; }
    public uint BindlessIndex { get; }
    public void Dispose() { if (_disposed) return; _disposed = true; _api.SamplerRelease(_sampler); _sampler = null; }
}

internal sealed unsafe class WebGpuPipeline : IGpuBackendPipeline
{
    private readonly WebGpuApi _api;
    private ComputePipeline* _compute;
    private RenderPipeline* _render;
    private bool _disposed;
    internal WebGpuPipeline(WebGpuApi api, ComputePipeline* pipeline) { _api = api; _compute = pipeline; }
    internal WebGpuPipeline(WebGpuApi api, RenderPipeline* pipeline) { _api = api; _render = pipeline; }
    public bool IsCompute => _compute != null;
    internal ComputePipeline* Compute => _compute;
    internal RenderPipeline* Render => _render;
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_compute != null) _api.ComputePipelineRelease(_compute);
        if (_render != null) _api.RenderPipelineRelease(_render);
        _compute = null; _render = null;
    }
}
