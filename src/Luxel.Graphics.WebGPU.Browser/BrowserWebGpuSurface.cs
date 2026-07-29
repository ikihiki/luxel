using Luxel.Graphics.Abstraction;

namespace Luxel.Graphics.WebGPU.Browser;

/// <summary>Canvas-backed Browser WebGPU presentation surface.</summary>
public sealed class BrowserWebGpuSurface : IGpuBackendSurface
{
    private readonly BrowserWebGpuBackend _owner;
    private readonly int _handle;
    private uint _width;
    private uint _height;
    private bool _disposed;
    internal BrowserWebGpuSurface(BrowserWebGpuBackend owner, int handle, uint width, uint height)
    { _owner = owner; _handle = handle > 0 ? handle : throw new InvalidOperationException("JavaScript returned an invalid surface handle."); _width = width; _height = height; }

    public void Present(IGpuBackendBuffer source, uint srcStridePixels, uint width, uint height)
    {
        ThrowIfDisposed(); _owner.ThrowIfDisposed();
        BrowserWebGpuBuffer buffer = _owner.RequireBuffer(source, nameof(source));
        if (width == 0 || height == 0) return;
        if (srcStridePixels < width) throw new ArgumentOutOfRangeException(nameof(srcStridePixels));
        ulong required = checked(((ulong)(height - 1) * srcStridePixels + width) * 4);
        if (required > buffer.Size) throw new ArgumentException("Presentation buffer is too small.", nameof(source));
        if (buffer.Kind == GpuMemoryKind.HostMapped) ((BrowserWebGpuQueue)_owner.MainQueue).UploadBuffer(buffer);
        if (_width != width || _height != height) Resize(width, height);
        _owner.Interop.SurfacePresent(_handle, checked((int)buffer.Offset), checked((int)srcStridePixels), checked((int)width), checked((int)height));
    }

    public void Resize(uint width, uint height)
    {
        ThrowIfDisposed(); _owner.ThrowIfDisposed();
        _ = checked((int)width); _ = checked((int)height);
        _owner.Interop.SurfaceResize(_handle, (int)width, (int)height);
        _width = width; _height = height;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _owner.ReleaseHandle(BrowserHandleKind.Surface, _handle);
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
