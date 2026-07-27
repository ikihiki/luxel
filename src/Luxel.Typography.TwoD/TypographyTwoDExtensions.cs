using System.Numerics;
using System.Text;
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

    /// <summary>単一Runeの実グリフ輪郭を指定矩形へ非等方scale/translateして追加する。
    /// Powerline separatorなど、セル境界へ隙間なく接続する記号向け。複数Rune、カラー/空輪郭はfalse。</summary>
    public static bool TryAppendSingleGlyphWarped(
        this VectorFont font,
        Scene2D scene,
        string text,
        float targetX,
        float targetY,
        float targetWidth,
        float targetHeight,
        float pixelHeight,
        uint color)
    {
        ArgumentNullException.ThrowIfNull(font);
        ArgumentNullException.ThrowIfNull(scene);
        if (targetWidth <= 0 || targetHeight <= 0 || pixelHeight <= 0) return false;

        var runes = text.EnumerateRunes();
        if (!runes.MoveNext()) return false;
        Rune rune = runes.Current;
        if (runes.MoveNext() || !font.TryGetGlyph(rune.Value, out uint glyphId)) return false;
        if (font.TryGetColorLayers(glyphId, out _)) return false;
        if (font.GetOutline(glyphId) is not VectorFont.GlyphOutline outline) return false;

        Vector2[][] contours = GetGlyphContours(font, glyphId, outline, pixelHeight, scene.FlattenTolerance);
        if (!TryBounds(contours, out Vector2 min, out Vector2 max)) return false;
        float sourceWidth = max.X - min.X, sourceHeight = max.Y - min.Y;
        if (sourceWidth <= float.Epsilon || sourceHeight <= float.Epsilon) return false;

        float sx = targetWidth / sourceWidth, sy = targetHeight / sourceHeight;
        var transform = new Matrix3x2(sx, 0, 0, sy, targetX - min.X * sx, targetY - min.Y * sy);
        scene.BeginFill(color).AppendClosedContours(contours, transform).End();
        return true;
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

        Vector2[][] contours = GetGlyphContours(font, glyphId, outline, pixelHeight, scene.FlattenTolerance);
        if (contours.Length > 0)
            scene.BeginFill(color, FillRule.NonZero, absolute).AppendClosedContours(contours, x, y).End();
    }

    private static Vector2[][] GetGlyphContours(
        VectorFont font,
        uint glyphId,
        VectorFont.GlyphOutline outline,
        float pixelHeight,
        float tolerance)
    {
        var key = (glyphId, pixelHeight, tolerance);
        Dictionary<(uint GlyphId, float PixelHeight, float Tolerance), Vector2[][]> cache =
            GlyphCaches.GetOrCreateValue(font).Contours;
        if (cache.TryGetValue(key, out Vector2[][]? contours)) return contours;

        var temporary = new Scene2D { FlattenTolerance = tolerance };
        EmitGlyph(temporary, outline, 0, 0, font.Scale(pixelHeight), Color2D.White, absolute: false);
        contours = temporary.ExportContours();
        cache[key] = contours;
        return contours;
    }

    private static bool TryBounds(Vector2[][] contours, out Vector2 min, out Vector2 max)
    {
        min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
        max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
        bool any = false;
        foreach (Vector2[] contour in contours)
        foreach (Vector2 point in contour)
        {
            min = Vector2.Min(min, point); max = Vector2.Max(max, point); any = true;
        }
        return any;
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
