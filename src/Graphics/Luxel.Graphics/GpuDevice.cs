using Luxel.Graphics.Abstraction;

namespace Luxel.Graphics;

/// <summary>
/// ライブラリの入口。バックエンド非依存の薄い公開 API を提供し、内部の
/// <see cref="IGpuBackend"/> に委譲する。
/// 生成は各バックエンドのファクトリ (例: <c>VulkanBackend.Create()</c>) を渡す:
/// <code>using var device = new GpuDevice(VulkanBackend.Create());</code>
/// </summary>
public sealed class GpuDevice : IDisposable
{
    private readonly IGpuBackend _backend;
    private bool _disposed;

    public GpuDevice(IGpuBackend backend)
    {
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        MainQueue = new GpuQueue(_backend.MainQueue);
    }

    /// <summary>
    /// Gets the owned backend implementation for explicit low-level integration.
    /// The backend remains owned by this device and must not be disposed separately.
    /// </summary>
    public IGpuBackend Backend => _backend;

    /// <summary>バックエンドとデバイスの名前。</summary>
    public string Name => _backend.Name;

    /// <summary>このデバイスのバックエンド種別。</summary>
    public GpuBackendKind BackendKind => _backend.Kind;

    /// <summary>主キュー。</summary>
    public GpuQueue MainQueue { get; }

    /// <summary><c>gpuMalloc</c>。GPU メモリを確保し、CPU ポインタと GPU アドレスを持つバッファを返す。</summary>
    public GpuBuffer Malloc(ulong sizeInBytes, GpuMemoryKind kind = GpuMemoryKind.HostMapped)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return new GpuBuffer(_backend, _backend.CreateBuffer(sizeInBytes, kind));
    }

    /// <summary>compute パイプラインを生成する。<paramref name="code"/> から本バックエンドに合う形式を選ぶ。</summary>
    public GpuPipeline CreateComputePipeline(GpuShaderCode code, string entryPoint = "main")
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ReadOnlySpan<byte> blob = code.ForBackend(BackendKind);
        return new GpuPipeline(_backend.CreateComputePipeline(blob, entryPoint));
    }

    /// <summary>Creates a logical graphics pipeline. Native state variants are resolved and cached by the backend.</summary>
    public GpuPipeline CreateGraphicsPipeline(GpuShaderCode code, GpuGraphicsPipelineDesc description)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        description.Validate();
        byte[] vs = code.VertexBlob(BackendKind);
        byte[] ps = code.PixelBlob(BackendKind);
        return new GpuPipeline(_backend.CreateGraphicsPipeline(vs, ps, description));
    }

    /// <summary>レンダーターゲット (カラー) テクスチャを生成する。</summary>
    public GpuTexture CreateRenderTarget(uint width, uint height, GpuFormat format = GpuFormat.Rgba8Unorm)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (width == 0 || height == 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (!GpuFormatInfo.IsColor(format)) throw new ArgumentException($"{format} is not a color attachment format.", nameof(format));
        return new GpuTexture(_backend.CreateRenderTarget(width, height, format));
    }

    /// <summary>深度ターゲットを生成する。</summary>
    public GpuTexture CreateDepthTarget(uint width, uint height, GpuFormat format = GpuFormat.D32Float)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (width == 0 || height == 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (!GpuFormatInfo.IsDepthStencilAttachment(format)) throw new ArgumentException($"{format} is not a depth-stencil attachment format.", nameof(format));
        return new GpuTexture(_backend.CreateDepthTarget(width, height, format));
    }

    /// <summary>サンプリング可能なテクスチャを生成し、ピクセルをアップロードする。</summary>
    public GpuTexture CreateTexture(uint width, uint height, ReadOnlySpan<byte> data,
        GpuFormat format = GpuFormat.Rgba8Unorm)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (width == 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height == 0) throw new ArgumentOutOfRangeException(nameof(height));
        if (!GpuFormatInfo.IsSampledUpload(format))
            throw new ArgumentException($"{format} is not a sampled upload format.", nameof(format));
        uint bytesPerPixel = GpuFormatInfo.BytesPerPixel(format);
        ulong expected = checked((ulong)width * height * bytesPerPixel);
        if ((ulong)data.Length != expected)
            throw new ArgumentException($"Texture data must contain exactly {expected} bytes, but contained {data.Length}.", nameof(data));
        return new GpuTexture(_backend.CreateSampledTexture(width, height, format, data));
    }

    /// <summary>サンプラを生成する。</summary>
    public GpuSampler CreateSampler(GpuSamplerFilter filter = GpuSamplerFilter.Linear, GpuSamplerAddress address = GpuSamplerAddress.Clamp)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return new GpuSampler(_backend.CreateSampler(filter, address));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _backend.Dispose();
    }
}
