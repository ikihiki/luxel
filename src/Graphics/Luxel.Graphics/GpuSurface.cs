using Luxel.Graphics.Abstraction;

namespace Luxel.Graphics;

/// <summary>ウィンドウへのスワップチェーン提示面。RGBA8 framebuffer をバックバッファへコピーして表示する。</summary>
public sealed class GpuSurface : IDisposable
{
    private readonly IGpuBackend _owner;
    private readonly IGpuBackendSurface _surface;
    private bool _disposed;

    /// <summary>
    /// Wraps a backend-specific presentation surface and assumes ownership of it.
    /// The owner is retained as an identity token so buffers from another backend instance are rejected.
    /// </summary>
    public GpuSurface(IGpuBackend owner, IGpuBackendSurface surface)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        _surface = surface ?? throw new ArgumentNullException(nameof(surface));
    }

    /// <summary>framebuffer の (0,0)-(width,height) を提示する。<paramref name="srcStridePixels"/> は行のピクセル数。</summary>
    public void Present(GpuBuffer framebuffer, uint srcStridePixels, uint width, uint height)
    {
        ArgumentNullException.ThrowIfNull(framebuffer);
        if (!ReferenceEquals(framebuffer.Owner, _owner))
            throw new ArgumentException("The presentation buffer belongs to another GPU backend instance.", nameof(framebuffer));
        _surface.Present(framebuffer.Backend, srcStridePixels, width, height);
    }

    public void Resize(uint width, uint height) => _surface.Resize(width, height);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _surface.Dispose();
    }
}
