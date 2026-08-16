using Luxel.Graphics.TwoD.Skia;
using Luxel.Graphics.TwoD;
using Luxel.Typography;
using Luxel.Typography.TwoD;

namespace Luxel.Tests;

public sealed class GlyphMaskRenderingTests
{
    [Fact]
    public void SkiaRasterizerProducesHintedR8Coverage()
    {
        using VectorFont font = VectorFont.LoadSystemJapanese();
        Assert.True(font.TryGetGlyph('A', out uint glyph));
        using var rasterizer = new SkiaGlyphMaskRasterizer();

        Assert.True(rasterizer.TryRasterize(font, glyph, 13, 0, out GlyphMaskBitmap bitmap));
        Assert.InRange(bitmap.Width, 3, 32);
        Assert.InRange(bitmap.Height, 3, 32);
        Assert.Equal(bitmap.Width * bitmap.Height, bitmap.Coverage.Length);
        Assert.Contains(bitmap.Coverage, value => value == byte.MaxValue);
        Assert.Contains(bitmap.Coverage, value => value is > 0 and < 255);
        Assert.True(bitmap.OriginY < 0);
    }

    [Fact]
    public void SkiaRasterizerKeepsHorizontalPhaseInCoverage()
    {
        using VectorFont font = VectorFont.LoadSystemJapanese();
        Assert.True(font.TryGetGlyph('i', out uint glyph));
        using var rasterizer = new SkiaGlyphMaskRasterizer();

        Assert.True(rasterizer.TryRasterize(font, glyph, 13, 0, out GlyphMaskBitmap aligned));
        Assert.True(rasterizer.TryRasterize(font, glyph, 13, 2, out GlyphMaskBitmap shifted));
        Assert.False(aligned.Coverage.AsSpan().SequenceEqual(shifted.Coverage));
    }

    [Fact]
    public void RegisteredSceneRendererIsUsedAndCanBeRemoved()
    {
        using VectorFont font = VectorFont.LoadSystemJapanese();
        var renderer = new RecordingRenderer();
        var scene = new Scene2D();

        using (GlyphMaskRendering.Register(font, renderer))
            font.AppendText(scene, "A", 0, 20, 13, Color2D.White);
        Assert.Equal(1, renderer.Calls);

        font.AppendText(new Scene2D(), "A", 0, 20, 13, Color2D.White);
        Assert.Equal(1, renderer.Calls);
    }

    private sealed class RecordingRenderer : IGlyphMaskSceneRenderer
    {
        public int Calls { get; private set; }

        public bool TryAppendGlyph(VectorFont font, Scene2D scene, uint glyphId, float x, float baselineY,
            float pixelHeight, uint color)
        {
            Calls++;
            scene.FillRect(color, x, baselineY - pixelHeight, 1, pixelHeight);
            return true;
        }
    }
}
