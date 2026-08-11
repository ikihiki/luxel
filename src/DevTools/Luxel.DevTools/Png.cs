using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Luxel.DevTools;

/// <summary>RGBA8 フレームの PNG 変換 (ImageSharp)。DevTools の format=png 配信と
/// Gallery の snap 回帰が使う。</summary>
public static class Png
{
    public static byte[] Encode(int width, int height, ReadOnlySpan<byte> rgba)
    {
        using var img = Image.LoadPixelData<Rgba32>(rgba, width, height);
        var ms = new MemoryStream();
        img.SaveAsPng(ms);
        return ms.ToArray();
    }

    public static void Write(string path, int width, int height, ReadOnlySpan<byte> rgba)
        => File.WriteAllBytes(path, Encode(width, height, rgba));

    /// <summary>PNG を RGBA8 へ展開する (snap のピクセル比較用 — エンコーダが変わっても
    /// ピクセルが同じなら等価と判定できる)。</summary>
    public static (byte[] Rgba, int Width, int Height) Decode(byte[] png)
    {
        using var img = Image.Load<Rgba32>(png);
        var rgba = new byte[img.Width * img.Height * 4];
        img.CopyPixelDataTo(rgba);
        return (rgba, img.Width, img.Height);
    }
}
