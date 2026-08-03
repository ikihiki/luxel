using System.Globalization;
using System.Text;
using System.Text.Json;
using Luxel.Graphics.Abstraction;

namespace Luxel.Graphics.WebGPU.Browser;

/// <summary>Browser WebGPU backend using integer handles and a JavaScript ES-module object registry.</summary>
public sealed class BrowserWebGpuBackend : IGpuBackend
{
    public const ulong ArenaSize = 64UL * 1024 * 1024;
    public const uint BufferStride = 256;
    public const uint RootDataSize = 192;
    public const uint RootBindingSize = 256;
    public const uint MaxSampledTextures = 16;
    public const uint MaxSamplers = 16;

    private readonly object _sync = new();
    private readonly IBrowserWebGpuInterop _interop;
    private readonly List<ArenaRange> _freeRanges = [new(0, ArenaSize)];
    private readonly List<BrowserWebGpuBuffer> _buffers = [];
    private readonly bool[] _textureSlots = new bool[MaxSampledTextures];
    private readonly bool[] _samplerSlots = new bool[MaxSamplers];
    private bool _disposed;

    private BrowserWebGpuBackend(IBrowserWebGpuInterop interop, int handle, string name)
    {
        _interop = interop;
        Handle = handle;
        Name = name;
        MainQueue = new BrowserWebGpuQueue(this);
    }

    public string Name { get; }
    public GpuBackendKind Kind => GpuBackendKind.WebGpu;
    public IGpuBackendQueue MainQueue { get; }
    public IAsyncGpuBackendQueue AsyncQueue => (IAsyncGpuBackendQueue)MainQueue;
    internal int Handle { get; }
    internal IBrowserWebGpuInterop Interop => _interop;
    internal bool IsDisposed => _disposed;

    /// <summary>Requests navigator.gpu adapter/device without synchronously blocking a JavaScript Promise.</summary>
    public static async Task<BrowserWebGpuBackend> CreateAsync(CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsBrowser())
            throw new PlatformNotSupportedException("BrowserWebGpuBackend requires a browser WebAssembly runtime with WebGPU.");
        return await CreateAsync(new BrowserWebGpuInterop(), cancellationToken).ConfigureAwait(false);
    }

    internal static async Task<BrowserWebGpuBackend> CreateAsync(IBrowserWebGpuInterop interop, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(interop);
        string json = await interop.InitializeAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        int handle = root.GetProperty("handle").GetInt32();
        string? name = root.TryGetProperty("name", out JsonElement nameElement) ? nameElement.GetString() : null;
        if (handle <= 0) throw new InvalidOperationException("WebGPU initialization returned an invalid backend handle.");
        return new BrowserWebGpuBackend(interop, handle, name ?? "WebGPU / browser");
    }

    public IGpuBackendBuffer CreateBuffer(ulong size, GpuMemoryKind kind)
    {
        ThrowIfDisposed();
        if (size == 0) throw new ArgumentOutOfRangeException(nameof(size));
        ulong physicalSize = AlignUp(size, 4);
        ulong allocationSize = AlignUp(physicalSize, BufferStride);
        lock (_sync)
        {
            for (int i = 0; i < _freeRanges.Count; i++)
            {
                ArenaRange range = _freeRanges[i];
                if (range.Size < allocationSize) continue;
                ulong offset = range.Offset;
                if (range.Size == allocationSize) _freeRanges.RemoveAt(i);
                else _freeRanges[i] = new(checked(offset + allocationSize), range.Size - allocationSize);
                var buffer = new BrowserWebGpuBuffer(this, size, physicalSize, allocationSize, offset, kind);
                _buffers.Add(buffer);
                return buffer;
            }
        }
        throw new OutOfMemoryException($"Browser WebGPU arena exhausted ({ArenaSize} bytes).");
    }

    public IGpuBackendPipeline CreateComputePipeline(ReadOnlySpan<byte> shaderBlob, string entryPoint)
    {
        ThrowIfDisposed();
        ValidateShader(shaderBlob, entryPoint);
        int handle = _interop.CreateComputePipeline(Handle, Convert.ToBase64String(shaderBlob), entryPoint);
        return new BrowserWebGpuPipeline(this, handle, true);
    }

    public IGpuBackendPipeline CreateGraphicsPipeline(ReadOnlySpan<byte> vsBlob, string vsEntry, ReadOnlySpan<byte> psBlob, string psEntry, GpuRasterDesc raster)
    {
        ThrowIfDisposed();
        ValidateShader(vsBlob, vsEntry);
        ValidateShader(psBlob, psEntry);
        ValidateColorFormat(raster.ColorFormat);
        if ((raster.DepthTest || raster.DepthWrite) && raster.DepthFormat != GpuFormat.D32Float)
            throw new NotSupportedException("Browser WebGPU supports D32Float depth targets only.");
        string rasterJson = string.Create(CultureInfo.InvariantCulture,
            $"{{\"colorFormat\":{(int)raster.ColorFormat},\"topology\":{(int)raster.Topology}," +
            $"\"depthTest\":{raster.DepthTest.ToString().ToLowerInvariant()},\"depthWrite\":{raster.DepthWrite.ToString().ToLowerInvariant()}," +
            $"\"depthFormat\":{(int)raster.DepthFormat},\"blend\":{(int)raster.Blend},\"cullMode\":{(int)raster.CullMode},\"frontFace\":{(int)raster.FrontFace}}}");
        int handle = _interop.CreateGraphicsPipeline(Handle, Convert.ToBase64String(vsBlob), vsEntry,
            Convert.ToBase64String(psBlob), psEntry, rasterJson);
        return new BrowserWebGpuPipeline(this, handle, false);
    }

    public IGpuBackendTexture CreateRenderTarget(uint width, uint height, GpuFormat format)
        => CreateTexture(width, height, format, BrowserTextureUsage.RenderTarget, ReadOnlySpan<byte>.Empty, null);

    public IGpuBackendTexture CreateDepthTarget(uint width, uint height, GpuFormat format)
    {
        if (format != GpuFormat.D32Float) throw new NotSupportedException("Browser WebGPU supports D32Float depth targets only.");
        return CreateTexture(width, height, format, BrowserTextureUsage.DepthTarget, ReadOnlySpan<byte>.Empty, null);
    }

    public IGpuBackendTexture CreateSampledTexture(uint width, uint height, GpuFormat format, ReadOnlySpan<byte> data)
    {
        if (format is not (GpuFormat.Rgba8Unorm or GpuFormat.Bgra8Unorm))
            throw new NotSupportedException("The fixed sampled-texture ABI supports RGBA8/BGRA8 filterable textures only.");
        int expected = checked((int)(width * height * 4));
        if (data.Length != expected) throw new ArgumentException($"Sampled texture data must contain exactly {expected} bytes.", nameof(data));
        uint slot = AllocateSlot(_textureSlots, "sampled texture", MaxSampledTextures);
        try { return CreateTexture(width, height, format, BrowserTextureUsage.Sampled, data, slot); }
        catch { ReleaseSlot(_textureSlots, slot); throw; }
    }

    public IGpuBackendSampler CreateSampler(GpuSamplerFilter filter, GpuSamplerAddress address = GpuSamplerAddress.Clamp)
    {
        ThrowIfDisposed();
        if (!Enum.IsDefined(filter)) throw new ArgumentOutOfRangeException(nameof(filter));
        if (!Enum.IsDefined(address)) throw new ArgumentOutOfRangeException(nameof(address));
        uint slot = AllocateSlot(_samplerSlots, "sampler", MaxSamplers);
        try { return new BrowserWebGpuSampler(this, _interop.CreateSampler(Handle, (int)filter, (int)address, checked((int)slot)), slot); }
        catch { ReleaseSlot(_samplerSlots, slot); throw; }
    }

    /// <summary>Creates a browser canvas presentation surface from a CSS selector or host-provided canvas token.</summary>
    public GpuSurface CreateCanvasSurface(string canvasToken, uint width, uint height)
        => new(this, CreateNativeCanvasSurface(canvasToken, width, height));

    /// <summary>Creates the backend-specific browser surface for advanced interop.</summary>
    public BrowserWebGpuSurface CreateNativeCanvasSurface(string canvasToken, uint width, uint height)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(canvasToken);
        ValidateDimensions(width, height);
        return new(this, _interop.CreateSurface(Handle, canvasToken, checked((int)width), checked((int)height)), width, height);
    }

    private BrowserWebGpuTexture CreateTexture(uint width, uint height, GpuFormat format, BrowserTextureUsage usage, ReadOnlySpan<byte> data, uint? slot)
    {
        ThrowIfDisposed();
        ValidateDimensions(width, height);
        if (!Enum.IsDefined(format)) throw new ArgumentOutOfRangeException(nameof(format));
        int handle = _interop.CreateTexture(Handle, checked((int)width), checked((int)height), (int)format, (int)usage,
            slot.HasValue ? checked((int)slot.Value) : -1, data.IsEmpty ? string.Empty : Convert.ToBase64String(data));
        return new(this, handle, width, height, format, slot);
    }

    internal BrowserWebGpuBuffer RequireBuffer(IGpuBackendBuffer value, string parameterName)
    {
        if (value is not BrowserWebGpuBuffer buffer || !ReferenceEquals(buffer.Owner, this))
            throw new ArgumentException("Buffer belongs to another backend.", parameterName);
        buffer.ThrowIfDisposed();
        return buffer;
    }

    internal BrowserWebGpuTexture RequireTexture(IGpuBackendTexture value, string parameterName)
    {
        if (value is not BrowserWebGpuTexture texture || !ReferenceEquals(texture.Owner, this))
            throw new ArgumentException("Texture belongs to another backend.", parameterName);
        texture.ThrowIfDisposed();
        return texture;
    }

    internal BrowserWebGpuPipeline RequirePipeline(IGpuBackendPipeline value, bool compute)
    {
        if (value is not BrowserWebGpuPipeline pipeline || !ReferenceEquals(pipeline.Owner, this))
            throw new ArgumentException("Pipeline belongs to another backend.", nameof(value));
        pipeline.ThrowIfDisposed();
        if (pipeline.IsCompute != compute) throw new ArgumentException("Pipeline has the wrong type.", nameof(value));
        return pipeline;
    }

    internal BrowserWebGpuBuffer[] SnapshotBuffers()
    {
        lock (_sync) return _buffers.Where(static b => !b.IsDisposed).ToArray();
    }

    internal void RetireBuffer(BrowserWebGpuBuffer buffer, ulong offset, ulong allocationSize)
    {
        lock (_sync)
        {
            _buffers.Remove(buffer);
            _freeRanges.Add(new(offset, allocationSize));
            _freeRanges.Sort(static (a, b) => a.Offset.CompareTo(b.Offset));
            for (int i = 0; i + 1 < _freeRanges.Count;)
            {
                ArenaRange current = _freeRanges[i], next = _freeRanges[i + 1];
                if (current.Offset + current.Size == next.Offset)
                {
                    _freeRanges[i] = new(current.Offset, current.Size + next.Size);
                    _freeRanges.RemoveAt(i + 1);
                }
                else i++;
            }
        }
    }

    internal void RetireTexture(uint? slot, int handle)
    {
        if (slot.HasValue) ReleaseSlot(_textureSlots, slot.Value);
        if (!_disposed) _interop.Release((int)BrowserHandleKind.Texture, handle);
    }

    internal void RetireSampler(uint slot, int handle)
    {
        ReleaseSlot(_samplerSlots, slot);
        if (!_disposed) _interop.Release((int)BrowserHandleKind.Sampler, handle);
    }

    internal void ReleaseHandle(BrowserHandleKind kind, int handle)
    {
        if (!_disposed) _interop.Release((int)kind, handle);
    }

    internal void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var buffer in SnapshotBuffers()) buffer.Dispose();
        _interop.DisposeBackend(Handle);
    }

    internal static ulong AlignUp(ulong value, ulong alignment) => checked((value + alignment - 1) & ~(alignment - 1));
    private static void ValidateShader(ReadOnlySpan<byte> shader, string entryPoint)
    {
        if (shader.IsEmpty) throw new ArgumentException("WGSL shader data cannot be empty.", nameof(shader));
        ArgumentException.ThrowIfNullOrWhiteSpace(entryPoint);
        _ = Encoding.UTF8.GetString(shader);
    }
    private static void ValidateColorFormat(GpuFormat format)
    {
        if (format is not (GpuFormat.Rgba8Unorm or GpuFormat.Bgra8Unorm or GpuFormat.R32Float))
            throw new NotSupportedException($"Unsupported Browser WebGPU color format: {format}.");
    }
    private static void ValidateDimensions(uint width, uint height)
    {
        if (width == 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height == 0) throw new ArgumentOutOfRangeException(nameof(height));
        _ = checked((int)width); _ = checked((int)height);
    }
    private uint AllocateSlot(bool[] slots, string name, uint limit)
    {
        lock (_sync)
        {
            for (uint i = 0; i < slots.Length; i++) if (!slots[i]) { slots[i] = true; return i; }
        }
        throw new InvalidOperationException($"Browser WebGPU {name} table is full; fixed limit is {limit}.");
    }
    private void ReleaseSlot(bool[] slots, uint slot) { lock (_sync) slots[slot] = false; }

    private sealed record InitializationResult(int Handle, string? Name);
    private readonly record struct ArenaRange(ulong Offset, ulong Size);
}

internal enum BrowserTextureUsage { RenderTarget, DepthTarget, Sampled }
internal enum BrowserHandleKind { Texture = 1, Sampler = 2, Pipeline = 3, Command = 4, Surface = 5 }
