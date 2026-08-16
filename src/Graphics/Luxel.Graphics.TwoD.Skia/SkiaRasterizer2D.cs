namespace Luxel.Graphics.TwoD.Skia;

/// <summary>SkiaSharpで同期描画するCPU RGBA target。</summary>
public sealed class SkiaRasterTarget2D : IRasterTarget2D
{
    private readonly byte[] _pixels;

    public SkiaRasterTarget2D(uint width, uint height)
    {
        if (width == 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height == 0) throw new ArgumentOutOfRangeException(nameof(height));
        Width = width;
        Height = height;
        RowBytes = checked((int)width * 4);
        _pixels = new byte[checked(RowBytes * (int)height)];
    }

    public uint Width { get; }
    public uint Height { get; }
    public int RowBytes { get; }
    public Memory<byte> Pixels => _pixels;
    public byte[] ToArray() => (byte[])_pixels.Clone();

    internal void SetPixels(byte[] source)
    {
        if (source.Length != _pixels.Length)
            throw new ArgumentException("Pixel buffer size does not match the target.", nameof(source));
        source.CopyTo(_pixels, 0);
    }
}

/// <summary>SkiaSharpによるCPU 2D rasterizer。</summary>
public sealed class SkiaRasterizer2D : IRasterizer2D
{
    private bool _disposed;

    public string Name => "SkiaSharp";
    public Rasterizer2DCapabilities Capabilities => Rasterizer2DCapabilities.CpuRgbaTarget;

    public IRasterScene2D CreateScene(Scene2D scene)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(scene);
        EnsureSupported(scene);
        return new ImmediateScene(this, scene);
    }

    public IRasterScene2D CreateScene(RetainedCanvas canvas)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(canvas);
        EnsureSupported(canvas.Root);
        return new RetainedScene(this, canvas);
    }

    public void Dispose() => _disposed = true;

    internal void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    internal static SkiaRasterTarget2D RequireTarget(IRasterTarget2D target)
        => target as SkiaRasterTarget2D
           ?? throw new ArgumentException("Skia scenes require SkiaRasterTarget2D.", nameof(target));

    internal static void EnsureSupported(Scene2D scene)
    {
        foreach (Scene2D.Shape shape in scene.Shapes)
            if (shape.Kind is PaintKind.Image or PaintKind.Mask)
                throw new NotSupportedException("SkiaRasterizer2D does not support bindless image or alpha-mask shapes.");
    }

    private static void EnsureSupported(UiNode node)
    {
        if (node.Content is { } scene) EnsureSupported(scene);
        foreach (UiNode child in node.Children) EnsureSupported(child);
    }

    private sealed class ImmediateScene(SkiaRasterizer2D rasterizer, Scene2D scene) : IRasterScene2D
    {
        private bool _disposed;
        public IRasterizer2D Rasterizer => rasterizer;
        public void Render(Camera2D camera, IRasterTarget2D target, bool transparent = false)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            rasterizer.ThrowIfDisposed();
            SkiaRasterTarget2D skia = RequireTarget(target);
            EnsureSupported(scene);
            skia.SetPixels(SkiaRenderer.RenderRgba(scene, camera, (int)skia.Width, (int)skia.Height, transparent));
        }
        public void Dispose() => _disposed = true;
    }

    private sealed class RetainedScene(SkiaRasterizer2D rasterizer, RetainedCanvas canvas) : IRasterScene2D
    {
        private bool _disposed;
        public IRasterizer2D Rasterizer => rasterizer;
        public void Render(Camera2D camera, IRasterTarget2D target, bool transparent = false)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            rasterizer.ThrowIfDisposed();
            SkiaRasterTarget2D skia = RequireTarget(target);
            EnsureSupported(canvas.Root);
            canvas.Flush(skia.Width, skia.Height);
            skia.SetPixels(SkiaRenderer.RenderRgba(canvas, camera, (int)skia.Width, (int)skia.Height, transparent));
            canvas.EmitRenderDiagnostics();
        }
        public void Dispose() => _disposed = true;
    }
}
