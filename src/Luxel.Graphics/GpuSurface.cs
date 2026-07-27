using Luxel.Graphics.Abstraction;

namespace Luxel.Graphics;

/// <summary>ウィンドウへのスワップチェーン提示面。RGBA8 framebuffer をバックバッファへコピーして表示する。</summary>
public sealed class GpuSurface : IDisposable
{
    private readonly IGpuBackendSurface _surface;
    private bool _disposed;

    internal GpuSurface(IGpuBackendSurface surface) => _surface = surface;

    /// <summary>framebuffer の (0,0)-(width,height) を提示する。<paramref name="srcStridePixels"/> は行のピクセル数。</summary>
    public void Present(GpuBuffer framebuffer, uint srcStridePixels, uint width, uint height)
        => _surface.Present(framebuffer.Backend, srcStridePixels, width, height);

    public void Resize(uint width, uint height) => _surface.Resize(width, height);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _surface.Dispose();
    }
}
