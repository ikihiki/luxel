using Luxel.Typography;
using Luxel.Typography.TwoD;
using SkiaSharp;

namespace Luxel.Graphics.TwoD.Skia;

/// <summary>Skia のフォントヒンティングを使って、GPU用のグレースケールグリフマスクを生成する。</summary>
public sealed class SkiaGlyphMaskRasterizer : IGlyphMaskRasterizer
{
    private readonly Dictionary<VectorFont, TypefaceOwner> _typefaces = new(ReferenceEqualityComparer.Instance);
    private bool _disposed;

    public bool TryRasterize(
        VectorFont font,
        uint glyphId,
        float physicalPixelHeight,
        byte horizontalPhase,
        out GlyphMaskBitmap bitmap)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(font);
        if (glyphId > ushort.MaxValue || horizontalPhase > 3 || !(physicalPixelHeight > 0))
        {
            bitmap = default;
            return false;
        }

        if (!_typefaces.TryGetValue(font, out TypefaceOwner? owner))
        {
            owner = new TypefaceOwner(font);
            _typefaces.Add(font, owner);
        }
        SKTypeface typeface = owner.Typeface;
        using var skFont = new SKFont(typeface, physicalPixelHeight)
        {
            Edging = SKFontEdging.Antialias,
            Hinting = SKFontHinting.Full,
            Subpixel = true,
            ForceAutoHinting = false,
        };

        skFont.GetFontMetrics(out SKFontMetrics metrics);
        float metricHeight = metrics.Descent - metrics.Ascent;
        if (metricHeight > 0)
            skFont.Size *= physicalPixelHeight / metricHeight;

        ushort glyph = (ushort)glyphId;
        Span<ushort> glyphs = stackalloc ushort[1] { glyph };
        Span<float> widths = stackalloc float[1];
        Span<SKRect> bounds = stackalloc SKRect[1];
        using var paint = new SKPaint
        {
            Color = SKColors.White,
            IsAntialias = true,
            BlendMode = SKBlendMode.Src,
        };
        skFont.GetGlyphWidths(glyphs, widths, bounds, paint);
        SKRect bound = bounds[0];
        if (bound.IsEmpty)
        {
            bitmap = new GlyphMaskBitmap(0, 0, 0, 0, []);
            return true;
        }

        const int padding = 1;
        int left = (int)MathF.Floor(bound.Left) - padding;
        int top = (int)MathF.Floor(bound.Top) - padding;
        int right = (int)MathF.Ceiling(bound.Right + horizontalPhase / 4f) + padding;
        int bottom = (int)MathF.Ceiling(bound.Bottom) + padding;
        int width = right - left;
        int height = bottom - top;
        if (width <= 0 || height <= 0 || width > 512 || height > 512)
        {
            bitmap = default;
            return false;
        }

        var info = new SKImageInfo(width, height, SKColorType.Alpha8, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        if (surface is null)
        {
            bitmap = default;
            return false;
        }
        SKCanvas canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        using var builder = new SKTextBlobBuilder();
        Span<SKPoint> positions = stackalloc SKPoint[1]
        {
            new(-left + horizontalPhase / 4f, -top),
        };
        builder.AddPositionedRun(glyphs, skFont, positions);
        using SKTextBlob? blob = builder.Build();
        if (blob is null)
        {
            bitmap = default;
            return false;
        }
        canvas.DrawText(blob, 0, 0, paint);
        canvas.Flush();

        using SKImage image = surface.Snapshot();
        using SKPixmap pixmap = image.PeekPixels();
        byte[] coverage = new byte[checked(width * height)];
        unsafe
        {
            byte* source = (byte*)pixmap.GetPixels().ToPointer();
            for (int row = 0; row < height; row++)
                new ReadOnlySpan<byte>(source + row * pixmap.RowBytes, width)
                    .CopyTo(coverage.AsSpan(row * width, width));
        }

        bitmap = new GlyphMaskBitmap(width, height, left, top, coverage);
        return true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (TypefaceOwner owner in _typefaces.Values) owner.Dispose();
        _typefaces.Clear();
    }

    private sealed class TypefaceOwner : IDisposable
    {
        private readonly SKData _data;

        public TypefaceOwner(VectorFont font)
        {
            _data = SKData.CreateCopy(font.FontData.Span);
            Typeface = SKTypeface.FromData(_data, font.FontIndex)
                ?? throw new NotSupportedException("Skia could not open the supplied font face.");
        }

        public SKTypeface Typeface { get; }

        public void Dispose()
        {
            Typeface.Dispose();
            _data.Dispose();
        }
    }
}
