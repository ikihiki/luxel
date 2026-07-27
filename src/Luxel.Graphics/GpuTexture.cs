using Luxel.Graphics.Abstraction;

namespace Luxel.Graphics;

/// <summary>テクスチャ / レンダーターゲット。</summary>
public sealed class GpuTexture : IDisposable
{
    private readonly IGpuBackendTexture _texture;
    private bool _disposed;

    internal GpuTexture(IGpuBackendTexture texture) => _texture = texture;

    public uint Width => _texture.Width;
    public uint Height => _texture.Height;
    public GpuFormat Format => _texture.Format;

    /// <summary>サンプリング用 bindless インデックス。</summary>
    public uint BindlessIndex => _texture.BindlessIndex;

    internal IGpuBackendTexture Backend => _texture;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _texture.Dispose();
    }
}
