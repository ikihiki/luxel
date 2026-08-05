using System.Runtime.InteropServices;
using Luxel.Graphics.Abstraction;

namespace Luxel.Graphics.WebGPU.Browser;

internal sealed unsafe class BrowserWebGpuBuffer : IGpuBackendBuffer
{
    private GCHandle _pin;
    private byte[]? _shadow;
    private bool _disposed;

    internal BrowserWebGpuBuffer(BrowserWebGpuBackend owner, ulong size, ulong physicalSize, ulong allocationSize, ulong offset, GpuMemoryKind kind)
    {
        Owner = owner; Size = size; PhysicalSize = physicalSize; AllocationSize = allocationSize; Offset = offset; Kind = kind;
        if (kind != GpuMemoryKind.DeviceLocal)
        {
            if (physicalSize > int.MaxValue) throw new ArgumentOutOfRangeException(nameof(size));
            _shadow = new byte[checked((int)physicalSize)];
            _pin = GCHandle.Alloc(_shadow, GCHandleType.Pinned);
        }
    }

    public ulong Size { get; }
    public ulong DeviceAddress => Offset;
    public uint BindlessIndex => checked((uint)(Offset / BrowserWebGpuBackend.BufferStride));
    public void* MappedPointer { get { ThrowIfDisposed(); return _shadow is null ? null : (void*)_pin.AddrOfPinnedObject(); } }
    internal BrowserWebGpuBackend Owner { get; }
    internal ulong Offset { get; }
    internal ulong PhysicalSize { get; }
    internal ulong AllocationSize { get; }
    internal GpuMemoryKind Kind { get; }
    internal bool IsDisposed => _disposed;
    internal byte[] Shadow => _shadow ?? throw new InvalidOperationException("DeviceLocal buffers have no CPU shadow.");
    internal void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Owner.RetireBuffer(this, Offset, AllocationSize);
        if (_pin.IsAllocated) _pin.Free();
        _shadow = null;
    }
}

internal abstract class BrowserWebGpuHandle : IDisposable
{
    private bool _disposed;
    protected BrowserWebGpuHandle(BrowserWebGpuBackend owner, int handle) { Owner = owner; Handle = handle > 0 ? handle : throw new InvalidOperationException("JavaScript returned an invalid handle."); }
    internal BrowserWebGpuBackend Owner { get; }
    internal int Handle { get; }
    internal bool IsDisposed => _disposed;
    internal void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
    protected abstract void Release();
    public void Dispose() { if (_disposed) return; _disposed = true; Release(); }
}

internal sealed class BrowserWebGpuTexture : BrowserWebGpuHandle, IGpuBackendTexture
{
    private readonly uint? _slot;
    internal BrowserWebGpuTexture(BrowserWebGpuBackend owner, int handle, uint width, uint height, GpuFormat format, uint? slot) : base(owner, handle)
    { Width = width; Height = height; Format = format; _slot = slot; }
    public uint Width { get; }
    public uint Height { get; }
    public GpuFormat Format { get; }
    public uint BindlessIndex { get { ThrowIfDisposed(); return _slot ?? 0; } }
    protected override void Release() => Owner.RetireTexture(_slot, Handle);
}

internal sealed class BrowserWebGpuSampler : BrowserWebGpuHandle, IGpuBackendSampler
{
    private readonly uint _slot;
    internal BrowserWebGpuSampler(BrowserWebGpuBackend owner, int handle, uint slot) : base(owner, handle) => _slot = slot;
    public uint BindlessIndex { get { ThrowIfDisposed(); return _slot; } }
    protected override void Release() => Owner.RetireSampler(_slot, Handle);
}

internal sealed class BrowserWebGpuPipeline : BrowserWebGpuHandle, IGpuBackendPipeline
{
    private readonly bool _compute;
    private readonly Func<GpuGraphicsPipelineVariantKey, int>? _factory;
    private readonly Dictionary<GpuGraphicsPipelineVariantKey, BrowserWebGpuPipeline>? _variants;
    private ulong _hits, _misses;
    internal BrowserWebGpuPipeline(BrowserWebGpuBackend owner, int handle, bool compute) : base(owner, handle) => _compute = compute;
    internal BrowserWebGpuPipeline(BrowserWebGpuBackend owner, GpuGraphicsPipelineDesc description, Func<GpuGraphicsPipelineVariantKey, int> factory) : base(owner, int.MaxValue)
    { GraphicsDescription = description; _factory = factory; _variants = new(); }
    public bool IsCompute { get { ThrowIfDisposed(); return _compute; } }
    public GpuGraphicsPipelineDesc? GraphicsDescription { get; }
    public GpuPipelineDiagnostics Diagnostics => new(_hits, _misses, (ulong)(_variants?.Count ?? 1));
    public IGpuBackendPipeline ResolveGraphicsVariant(GpuRasterizerState rasterizer, GpuDepthStencilState depthStencil, GpuBlendState blend)
    {
        if (GraphicsDescription is not { } desc) return this;
        var key = new GpuGraphicsPipelineVariantKey(desc.Attachments, desc.Topology, rasterizer, depthStencil.Normalize(), blend);
        if (_variants!.TryGetValue(key, out var value)) { _hits++; return value; }
        _misses++; value = new BrowserWebGpuPipeline(Owner, _factory!(key), false); _variants.Add(key, value); return value;
    }
    protected override void Release()
    {
        if (_variants is not null) foreach (var variant in _variants.Values) variant.Dispose();
        else Owner.ReleaseHandle(BrowserHandleKind.Pipeline, Handle);
    }
}
