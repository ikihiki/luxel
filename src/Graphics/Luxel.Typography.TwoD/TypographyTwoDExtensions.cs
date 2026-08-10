using System.Numerics;
using System.Runtime.CompilerServices;
using Luxel.Graphics.TwoD;

namespace Luxel.Typography.TwoD;

/// <summary>Horizontal placement inside a text drawing box.</summary>
public enum TextBoxHorizontalAlignment { Left, Center, Right }

/// <summary>Vertical placement inside a text drawing box.</summary>
public enum TextBoxVerticalAlignment { Top, Center, Bottom }

/// <summary>Fine-grained placement controls for box-based vector text drawing.</summary>
public sealed record TextDrawOptions
{
    public TextBoxHorizontalAlignment HorizontalAlignment { get; init; } = TextBoxHorizontalAlignment.Left;
    public TextBoxVerticalAlignment VerticalAlignment { get; init; } = TextBoxVerticalAlignment.Top;
    /// <summary>Logical-pixel offset applied after alignment.</summary>
    public Vector2 Offset { get; init; }
    /// <summary>Uniform visual glyph scale. The box itself is not resized.</summary>
    public float GlyphScale { get; init; } = 1f;
    /// <summary>Multiplier applied to HarfBuzz advances between glyphs.</summary>
    public float AdvanceScale { get; init; } = 1f;
}

/// <summary>Typographyのシェーピング／レイアウト結果をScene2Dへ出力するアダプタ。</summary>
public static class TypographyTwoDExtensions
{
    private sealed class GlyphCache
    {
        public Dictionary<(uint GlyphId, float PixelHeight, float Tolerance), Vector2[][]> Contours { get; } = new();
    }

    private static readonly ConditionalWeakTable<VectorFont, GlyphCache> GlyphCaches = new();
    private static readonly bool NoGlyphCache = Environment.GetEnvironmentVariable("NOGFX_NO_GLYPH_CACHE") == "1";

    /// <summary>テキストを塗りグリフパスとしてsceneへ追加する。baseline=(x,y)。</summary>
    public static float AppendText(
        this VectorFont font,
        Scene2D scene,
        string text,
        float x,
        float y,
        float pixelHeight,
        uint color)
    {
        ArgumentNullException.ThrowIfNull(font);
        ArgumentNullException.ThrowIfNull(scene);
        if (string.IsNullOrEmpty(text)) return 0;

        ShapedGlyph[] glyphs = font.ShapeRun(text, pixelHeight);
        AppendShaped(font, scene, glyphs, x, y, pixelHeight, color, advanceScale: 1);
        return MeasureAdvance(glyphs, advanceScale: 1);
    }

    /// <summary>Draws text inside a box. Alignment, offset, glyph scale, and advance scale are
    /// resolved here so callers do not need to reverse-calculate baselines or measured origins.</summary>
    public static float AppendText(
        this VectorFont font,
        Scene2D scene,
        string text,
        TextRect box,
        float pixelHeight,
        uint color,
        TextDrawOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(font);
        ArgumentNullException.ThrowIfNull(scene);
        if (string.IsNullOrEmpty(text)) return 0;
        options ??= new TextDrawOptions();
        if (!float.IsFinite(options.GlyphScale) || options.GlyphScale <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "GlyphScale must be finite and greater than zero.");
        if (!float.IsFinite(options.AdvanceScale) || options.AdvanceScale <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "AdvanceScale must be finite and greater than zero.");

        float scaledHeight = pixelHeight * options.GlyphScale;
        ShapedGlyph[] glyphs = font.ShapeRun(text, scaledHeight);
        float width = MeasureAdvance(glyphs, options.AdvanceScale);
        float originX = options.HorizontalAlignment switch
        {
            TextBoxHorizontalAlignment.Center => box.X + MathF.Max(0, box.Width - width) * 0.5f,
            TextBoxHorizontalAlignment.Right => box.X + MathF.Max(0, box.Width - width),
            _ => box.X,
        };
        float top = options.VerticalAlignment switch
        {
            TextBoxVerticalAlignment.Center => box.Y + MathF.Max(0, box.Height - scaledHeight) * 0.5f,
            TextBoxVerticalAlignment.Bottom => box.Y + MathF.Max(0, box.Height - scaledHeight),
            _ => box.Y,
        };
        originX += options.Offset.X;
        float baseline = top + font.Ascent(scaledHeight) + options.Offset.Y;
        AppendShaped(font, scene, glyphs, originX, baseline, scaledHeight, color, options.AdvanceScale);
        return width;
    }

    /// <summary>レイアウト済みテキスト全体を1色で描く。(x,y)はレイアウトボックス左上。</summary>
    public static void Draw(this TextLayout layout, Scene2D scene, float x, float y, uint color)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(scene);
        layout.VisitGlyphs(x, y, null,
            (font, glyphId, gx, gy, px) => AppendGlyph(font, scene, glyphId, gx, gy, px, color));
    }

    /// <summary>指定色のrunだけを白で描く。保持型UIの色別ノード分割で使用する。</summary>
    public static void DrawColorRuns(this TextLayout layout, Scene2D scene, float x, float y, uint runColor)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(scene);
        layout.VisitGlyphs(x, y, runColor,
            (font, glyphId, gx, gy, px) => AppendGlyph(font, scene, glyphId, gx, gy, px, Color2D.White));
    }

    private static float MeasureAdvance(ReadOnlySpan<ShapedGlyph> glyphs, float advanceScale)
    {
        float width = 0;
        foreach (ref readonly ShapedGlyph glyph in glyphs) width += glyph.XAdvance * advanceScale;
        return width;
    }

    private static void AppendShaped(
        VectorFont font,
        Scene2D scene,
        ReadOnlySpan<ShapedGlyph> glyphs,
        float x,
        float baselineY,
        float pixelHeight,
        uint color,
        float advanceScale)
    {
        float penX = x, penY = baselineY;
        foreach (ref readonly ShapedGlyph glyph in glyphs)
        {
            AppendGlyph(font, scene, glyph.GlyphId,
                penX + glyph.XOffset, penY - glyph.YOffset, pixelHeight, color);
            penX += glyph.XAdvance * advanceScale;
            penY -= glyph.YAdvance;
        }
    }

    private static void AppendGlyph(
        VectorFont font,
        Scene2D scene,
        uint glyphId,
        float x,
        float baselineY,
        float pixelHeight,
        uint color)
    {
        if (font.TryGetColorLayers(glyphId, out ColorLayer[] layers))
        {
            foreach (ColorLayer layer in layers)
            {
                if (font.GetOutline(layer.GlyphId) is VectorFont.GlyphOutline outline)
                    EmitGlyphCached(font, scene, layer.GlyphId, outline, x, baselineY, pixelHeight,
                        layer.Foreground ? color : layer.Rgba, absolute: !layer.Foreground);
            }
            return;
        }

        if (font.GetOutline(glyphId) is VectorFont.GlyphOutline glyphOutline)
            EmitGlyphCached(font, scene, glyphId, glyphOutline, x, baselineY, pixelHeight, color);
    }

    private static void EmitGlyphCached(
        VectorFont font,
        Scene2D scene,
        uint glyphId,
        VectorFont.GlyphOutline outline,
        float x,
        float y,
        float pixelHeight,
        uint color,
        bool absolute = false)
    {
        if (NoGlyphCache)
        {
            EmitGlyph(scene, outline, x, y, font.Scale(pixelHeight), color, absolute);
            return;
        }

        var key = (glyphId, pixelHeight, scene.FlattenTolerance);
        Dictionary<(uint GlyphId, float PixelHeight, float Tolerance), Vector2[][]> cache =
            GlyphCaches.GetOrCreateValue(font).Contours;
        if (!cache.TryGetValue(key, out Vector2[][]? contours))
        {
            var temporary = new Scene2D { FlattenTolerance = scene.FlattenTolerance };
            EmitGlyph(temporary, outline, 0, 0, font.Scale(pixelHeight), color, absolute);
            contours = temporary.ExportContours();
            cache[key] = contours;
        }

        if (contours.Length > 0)
            scene.BeginFill(color, FillRule.NonZero, absolute).AppendClosedContours(contours, x, y).End();
    }

    private static void EmitGlyph(
        Scene2D scene,
        VectorFont.GlyphOutline outline,
        float x,
        float y,
        float scale,
        uint color,
        bool absolute)
    {
        scene.BeginFill(color, FillRule.NonZero, absolute);
        int start = 0;
        foreach (int end in outline.ContourEnds)
        {
            EmitContour(scene, outline, start, end, x, y, scale);
            start = end + 1;
        }
        scene.End();
    }

    private static void EmitContour(
        Scene2D scene,
        VectorFont.GlyphOutline outline,
        int start,
        int end,
        float x,
        float y,
        float scale)
    {
        int count = end - start + 1;
        if (count < 2) return;

        Vector2 Point(int i) => outline.Points[start + (i % count + count) % count];
        bool IsOnCurve(int i) => outline.OnCurve[start + (i % count + count) % count];
        static Vector2 Mid(Vector2 a, Vector2 b) => (a + b) * 0.5f;

        int firstOnCurve = -1;
        for (int i = 0; i < count; i++)
        {
            if (IsOnCurve(i))
            {
                firstOnCurve = i;
                break;
            }
        }

        Vector2 startPoint = firstOnCurve >= 0 ? Point(firstOnCurve) : Mid(Point(count - 1), Point(0));
        float X(Vector2 point) => x + point.X * scale;
        float Y(Vector2 point) => y - point.Y * scale;

        scene.MoveTo(X(startPoint), Y(startPoint));
        Vector2? control = null;
        int begin = firstOnCurve >= 0 ? firstOnCurve + 1 : 0;
        for (int i = 0; i < (firstOnCurve >= 0 ? count - 1 : count); i++)
        {
            Vector2 point = Point(begin + i);
            if (IsOnCurve(begin + i))
            {
                if (control is Vector2 c) scene.QuadTo(X(c), Y(c), X(point), Y(point));
                else scene.LineTo(X(point), Y(point));
                control = null;
            }
            else
            {
                if (control is Vector2 c)
                {
                    Vector2 midpoint = Mid(c, point);
                    scene.QuadTo(X(c), Y(c), X(midpoint), Y(midpoint));
                }
                control = point;
            }
        }

        if (control is Vector2 last)
            scene.QuadTo(X(last), Y(last), X(startPoint), Y(startPoint));
        scene.Close();
    }
}
