using System.Numerics;
using System.Runtime.CompilerServices;
using Luxel.Graphics.TwoD;

namespace Luxel.Typography.TwoD;

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

        float penX = x;
        float penY = y;
        foreach (ShapedGlyph glyph in font.ShapeRun(text, pixelHeight))
        {
            AppendGlyph(font, scene, glyph.GlyphId,
                penX + glyph.XOffset, penY - glyph.YOffset, pixelHeight, color);
            penX += glyph.XAdvance;
            penY -= glyph.YAdvance;
        }
        return penX - x;
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
