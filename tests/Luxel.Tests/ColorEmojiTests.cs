using Luxel.TwoD;
using Luxel.Typography;
using Xunit;

namespace Luxel.Tests;

/// <summary>CE: カラー絵文字 (COLR v0 + CPAL) — レイヤ取得と AbsoluteColor シェイプでの描画。
/// Segoe UI Emoji (seguiemj.ttf) が無い環境ではスキップ相当 (early return)。</summary>
public class ColorEmojiTests
{
    private static VectorFont? TryLoadEmoji()
    {
        try { return VectorFont.LoadSystem("seguiemj.ttf"); }
        catch { return null; }
    }

    [Fact]
    public void ColrLayers_AreParsed_WithPaletteColors()
    {
        using VectorFont? f = TryLoadEmoji();
        if (f is null) return;
        Assert.True(f.TryGetGlyph(0x1F600, out uint gid));   // 😀
        Assert.True(f.TryGetColorLayers(gid, out ColorLayer[] layers));
        Assert.True(layers.Length >= 2);   // カラー絵文字は複数レイヤ
        Assert.Contains(layers, l => !l.Foreground && (l.Rgba >> 24) != 0);   // 非透明のパレット色
    }

    [Fact]
    public void PlainGlyph_HasNoColorLayers()
    {
        using VectorFont? f = TryLoadEmoji();
        if (f is null) return;
        // 絵文字フォントにも数字等の非カラーグリフがある — cmap にあればカラー無しを確認
        if (f.TryGetGlyph('0', out uint gid))
            Assert.False(f.TryGetColorLayers(gid, out _));
    }

    [Fact]
    public void AppendText_Emoji_EmitsAbsoluteColorShapes()
    {
        using VectorFont? f = TryLoadEmoji();
        if (f is null) return;
        var scene = new Scene2D();
        f.AppendText(scene, "😀", 0, 32, 32, Color2D.White);
        Assert.True(scene.Shapes.Count >= 2);                          // レイヤ毎に 1 シェイプ
        Assert.Contains(scene.Shapes, s => s.AbsoluteColor);           // パレット色レイヤ
        Assert.Contains(scene.Shapes, s => s.Color != Color2D.White);  // 実際に色が付いている

        // エンコードでもレイヤ数分のパス + 形状別スタイル (styles[i].ColorRgba = シェイプ色)
        (_, GpuPath[] paths, GpuStyle[] styles) = PathEncoder.Encode(scene);
        Assert.Equal(scene.Shapes.Count, paths.Length);
        Assert.Contains(styles, st => st.ColorRgba != Color2D.White);
    }

    [Fact]
    public void PlainText_ShapesStayNodeColored()
    {
        // 通常テキストは従来どおり非 Absolute (白描き + ノード色) — 既存 recolor 経路の不変を担保
        using var f = VectorFont.LoadSystem();
        var scene = new Scene2D();
        f.AppendText(scene, "Ab", 0, 32, 32, Color2D.White);
        Assert.DoesNotContain(scene.Shapes, s => s.AbsoluteColor);
    }
}
