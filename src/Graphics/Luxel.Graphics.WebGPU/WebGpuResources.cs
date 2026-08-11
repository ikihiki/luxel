using System.Runtime.InteropServices;
using Luxel.Graphics.Abstraction;
using Silk.NET.WebGPU;
using WgpuBuffer = Silk.NET.WebGPU.Buffer;
using WebGpuApi = Silk.NET.WebGPU.WebGPU;

namespace Luxel.Graphics.WebGPU;

internal sealed unsafe class WebGpuBuffer : IGpuBackendBuffer
{
    private readonly WebGpuBackend _backend;
    private readonly ulong _allocationSize;
    private nint _shadow;
    private readonly object _lifetimeSync = new();
    private int _references;
    private bool _disposed;
    private bool _retired;

    internal WebGpuBuffer(WebGpuBackend backend, ulong size, ulong physicalSize, ulong allocationSize, ulong offset, uint bindlessIndex, GpuMemoryKind kind)
    {
        _backend = backend;
        Size = size;
        PhysicalSize = physicalSize;
        _allocationSize = allocationSize;
        Offset = offset;
        BindlessIndex = bindlessIndex;
        Kind = kind;
        if (kind != GpuMemoryKind.DeviceLocal)
        {
            _shadow = (nint)NativeMemory.AllocZeroed((nuint)physicalSize);
            if (_shadow == 0) throw new OutOfMemoryException();
        }
    }

    public ulong Size { get; }
    public ulong DeviceAddress => Offset;
    public uint BindlessIndex { get; }
    public void* MappedPointer { get { ThrowIfDisposed(); return (void*)_shadow; } }
    internal WebGpuBackend Owner => _backend;
    internal ulong Offset { get; }
    internal ulong PhysicalSize { get; }
    internal GpuMemoryKind Kind { get; }
    internal bool IsDisposed => _disposed;

    internal void AddReference()
    {
        lock (_lifetimeSync)
        {
            ThrowIfDisposed();
            checked { _references++; }
        }
    }

    internal void ReleaseReference()
    {
        lock (_lifetimeSync)
        {
            if (_references <= 0) throw new InvalidOperationException("WebGPU buffer reference count underflow.");
            _references--;
        }
        TryRetire();
    }

    internal void Upload(WebGpuApi api, Queue* queue, WgpuBuffer* arena)
    {
        if (Kind != GpuMemoryKind.HostMapped || _shadow == 0) return;
        api.QueueWriteBuffer(queue, arena, Offset, (void*)_shadow, (nuint)PhysicalSize);
    }

    internal void CopyFromMapped(void* source)
    {
        if (Kind != GpuMemoryKind.HostCached || _shadow == 0) return;
        System.Buffer.MemoryCopy(source, (void*)_shadow, PhysicalSize, Size);
    }

    internal void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    public void Dispose()
    {
        lock (_lifetimeSync)
        {
            if (_disposed) return;
            _disposed = true;
        }
        TryRetire();
    }

    internal void TryRetire()
    {
        lock (_lifetimeSync)
        {
            if (!_disposed || _references != 0 || _retired) return;
            if (!_backend.TryRetireBuffer(this, Offset, _allocationSize)) return;
            _retired = true;
            if (_shadow != 0) { NativeMemory.Free((void*)_shadow); _shadow = 0; }
        }
    }
}

internal sealed unsafe class WebGpuTexture : IGpuBackendTexture
{
    private readonly WebGpuBackend _backend;
    private readonly WebGpuApi _api;
    private readonly bool _sampled;
    private readonly object _lifetimeSync = new();
    private Texture* _texture;
    private TextureView* _view;
    private int _references;
    private bool _disposed;
    private bool _retired;

    internal WebGpuTexture(WebGpuBackend backend, Texture* texture, TextureView* view, uint width, uint height,
        GpuFormat format, uint bindlessIndex, bool sampled)
    {
        _backend = backend; _api = backend.Api; _texture = texture; _view = view; _sampled = sampled;
        Width = width; Height = height; Format = format; _bindlessIndex = bindlessIndex;
    }

    private readonly uint _bindlessIndex;
    public uint Width { get; }
    public uint Height { get; }
    public GpuFormat Format { get; }
    public uint BindlessIndex { get { ThrowIfDisposed(); return _bindlessIndex; } }
    internal WebGpuBackend Owner => _backend;
    internal bool IsDisposed => _disposed;
    internal Texture* Handle { get { ThrowIfDisposed(); return _texture; } }
    internal TextureView* View { get { ThrowIfDisposed(); return _view; } }
    internal void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    internal void AddReference()
    {
        lock (_lifetimeSync)
        {
            ThrowIfDisposed();
            checked { _references++; }
        }
    }

    internal void ReleaseReference()
    {
        lock (_lifetimeSync)
        {
            if (_references <= 0) throw new InvalidOperationException("WebGPU texture reference count underflow.");
            _references--;
        }
        TryRetire();
    }

    public void Dispose()
    {
        lock (_lifetimeSync)
        {
            if (_disposed) return;
            _disposed = true;
        }
        TryRetire();
    }

    private void TryRetire()
    {
        lock (_lifetimeSync)
        {
            if (!_disposed || _references != 0 || _retired) return;
            _retired = true;
        }
        if (_sampled) _backend.TryRetireTexture(this, _bindlessIndex);
        else DisposeNative();
    }

    internal void DisposeNative()
    {
        if (_backend.CanReleaseNativeResources && _view != null) _api.TextureViewRelease(_view);
        if (_backend.CanReleaseNativeResources && _texture != null) { _api.TextureDestroy(_texture); _api.TextureRelease(_texture); }
        _view = null; _texture = null;
    }
}

internal sealed unsafe class WebGpuSampler : IGpuBackendSampler
{
    private readonly WebGpuBackend _backend;
    private readonly WebGpuApi _api;
    private readonly object _lifetimeSync = new();
    private readonly uint _bindlessIndex;
    private Sampler* _sampler;
    private int _references;
    private bool _disposed;
    private bool _retired;

    internal WebGpuSampler(WebGpuBackend backend, Sampler* sampler, uint index)
    {
        _backend = backend; _api = backend.Api; _sampler = sampler; _bindlessIndex = index;
    }

    public uint BindlessIndex { get { ThrowIfDisposed(); return _bindlessIndex; } }
    internal WebGpuBackend Owner => _backend;
    internal bool IsDisposed => _disposed;
    internal Sampler* Handle { get { ThrowIfDisposed(); return _sampler; } }
    internal void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    internal void AddReference()
    {
        lock (_lifetimeSync)
        {
            ThrowIfDisposed();
            checked { _references++; }
        }
    }

    internal void ReleaseReference()
    {
        lock (_lifetimeSync)
        {
            if (_references <= 0) throw new InvalidOperationException("WebGPU sampler reference count underflow.");
            _references--;
        }
        TryRetire();
    }

    public void Dispose()
    {
        lock (_lifetimeSync)
        {
            if (_disposed) return;
            _disposed = true;
        }
        TryRetire();
    }

    private void TryRetire()
    {
        lock (_lifetimeSync)
        {
            if (!_disposed || _references != 0 || _retired) return;
            _retired = true;
        }
        _backend.TryRetireSampler(this, _bindlessIndex);
    }

    internal void DisposeNative()
    {
        if (_backend.CanReleaseNativeResources && _sampler != null) _api.SamplerRelease(_sampler);
        _sampler = null;
    }
}

internal unsafe delegate RenderPipeline* WebGpuPipelineFactory(GpuGraphicsPipelineVariantKey key);

internal sealed unsafe class WebGpuPipeline : IGpuBackendPipeline
{
    private readonly WebGpuBackend _backend;
    private readonly WebGpuApi _api;
    private ComputePipeline* _compute;
    private RenderPipeline* _render;
    private readonly WebGpuPipelineFactory? _factory;
    private readonly Dictionary<GpuGraphicsPipelineVariantKey, WebGpuPipeline>? _variants;
    private ulong _hits, _misses;
    private bool _disposed;
    internal WebGpuPipeline(WebGpuBackend backend, ComputePipeline* pipeline) { _backend = backend; _api = backend.Api; _compute = pipeline; }
    internal WebGpuPipeline(WebGpuBackend backend, RenderPipeline* pipeline) { _backend = backend; _api = backend.Api; _render = pipeline; }
    internal WebGpuPipeline(WebGpuBackend backend, GpuGraphicsPipelineDesc description, WebGpuPipelineFactory factory)
    { _backend = backend; _api = backend.Api; GraphicsDescription = description; _factory = factory; _variants = new(); }
    public bool IsCompute { get { ThrowIfDisposed(); return _compute != null; } }
    public GpuGraphicsPipelineDesc? GraphicsDescription { get; }
    public GpuPipelineDiagnostics Diagnostics => new(_hits, _misses, (ulong)(_variants?.Count ?? (_compute != null || _render != null ? 1 : 0)));
    internal WebGpuBackend Owner => _backend;
    internal bool IsDisposed => _disposed;
    internal ComputePipeline* Compute { get { ThrowIfDisposed(); return _compute; } }
    internal RenderPipeline* Render { get { ThrowIfDisposed(); return _render; } }
    public IGpuBackendPipeline ResolveGraphicsVariant(GpuRasterizerState rasterizer, GpuDepthStencilState depthStencil, GpuBlendState blend)
    {
        if (GraphicsDescription is not { } desc) return this;
        var key = new GpuGraphicsPipelineVariantKey(desc.Attachments, desc.Topology, rasterizer, depthStencil.Normalize(), blend);
        if (_variants!.TryGetValue(key, out var value)) { _hits++; return value; }
        _misses++; value = new WebGpuPipeline(_backend, _factory!(key)); _variants.Add(key, value); return value;
    }
    internal void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
        if (_variants is not null) foreach (var variant in _variants.Values) variant.Dispose();
        if (!_backend.IsDisposed && _compute != null) _api.ComputePipelineRelease(_compute);
        if (!_backend.IsDisposed && _render != null) _api.RenderPipelineRelease(_render);
        _compute = null; _render = null;
    }
}
