using Luxel.Graphics.TwoD;

namespace Luxel.Controls;

/// <summary>CPU RGBA8 pixels backed by a host-mapped GPU buffer and exposed as an image scene.</summary>
internal sealed class RgbaImagePresenter : IDisposable
{
    private GpuDevice? _device;
    private GpuBuffer? _buffer;

    internal int Width { get; private set; }
    internal int Height { get; private set; }

    /// <summary>Uploads pixels, replacing the buffer only when its device or dimensions change.</summary>
    internal bool Update(GpuDevice device, int width, int height, ReadOnlySpan<byte> rgba)
    {
        int byteCount = checked(width * height * 4);
        bool replaced = _buffer is null || !ReferenceEquals(device, _device) || width != Width || height != Height;
        if (replaced)
        {
            _buffer?.Dispose();
            _buffer = device.Malloc((ulong)byteCount, GpuMemoryKind.HostMapped);
            _device = device;
            Width = width;
            Height = height;
        }

        rgba[..byteCount].CopyTo(_buffer!.Span<byte>(byteCount));
        return replaced;
    }

    internal Scene2D CreateScene(float displayWidth, float displayHeight)
        => _buffer is null
            ? new Scene2D()
            : new Scene2D().ImageRect(
                _buffer.BindlessIndex, (uint)Width, (uint)Width, (uint)Height,
                0, 0, displayWidth, displayHeight);

    public void Dispose()
    {
        _buffer?.Dispose();
        _buffer = null;
        _device = null;
        Width = 0;
        Height = 0;
    }
}
