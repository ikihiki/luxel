using System.IO.Compression;

namespace Luxel.Gltf;

/// <summary>
/// 依存なしの最小 PNG デコーダ (8-bit Gray/GA/RGB/RGBA)。glTF の埋め込みテクスチャ用。
/// 未サポート: 16-bit, パレット, インターレース。
/// </summary>
internal static class PngDecoder
{
    private static readonly byte[] Signature = { 137, 80, 78, 71, 13, 10, 26, 10 };

    public static (int Width, int Height, byte[] Rgba) DecodeRgba(byte[] png)
    {
        if (png.Length < 8 || !png.AsSpan(0, 8).SequenceEqual(Signature))
            throw new InvalidDataException("not a PNG");

        int w = 0, h = 0, bitDepth = 0, colorType = 0;
        byte[]? palette = null;   // PLTE (RGB * N)
        byte[]? paletteAlpha = null;  // tRNS (alpha for palette entries)
        using var idataMs = new MemoryStream();
        int offset = 8;
        while (offset + 8 <= png.Length)
        {
            int length = ReadBE(png, offset);
            string type = System.Text.Encoding.ASCII.GetString(png, offset + 4, 4);
            int dataStart = offset + 8;
            if (type == "IHDR")
            {
                w = ReadBE(png, dataStart);
                h = ReadBE(png, dataStart + 4);
                bitDepth = png[dataStart + 8];
                colorType = png[dataStart + 9];
                if (png[dataStart + 12] != 0) throw new NotSupportedException("interlaced PNG");
            }
            else if (type == "PLTE")
            {
                palette = png.AsSpan(dataStart, length).ToArray();
            }
            else if (type == "tRNS")
            {
                paletteAlpha = png.AsSpan(dataStart, length).ToArray();
            }
            else if (type == "IDAT")
                idataMs.Write(png, dataStart, length);
            else if (type == "IEND")
                break;
            offset = dataStart + length + 4;
        }
        if (bitDepth != 8) throw new NotSupportedException($"PNG bitDepth {bitDepth}");
        int bpp = colorType switch { 0 => 1, 2 => 3, 3 => 1, 4 => 2, 6 => 4, _ => throw new NotSupportedException($"PNG colorType {colorType}") };
        if (colorType == 3 && palette is null) throw new InvalidDataException("PNG colorType 3 but no PLTE");

        idataMs.Position = 0;
        using var zlib = new ZLibStream(idataMs, CompressionMode.Decompress);
        int rowSize = w * bpp;
        byte[] raw = new byte[h * (rowSize + 1)];
        int total = 0;
        while (total < raw.Length)
        {
            int r = zlib.Read(raw, total, raw.Length - total);
            if (r == 0) break;
            total += r;
        }

        byte[] pixels = new byte[h * rowSize];
        for (int y = 0; y < h; y++)
        {
            int rowStart = y * (rowSize + 1);
            byte filter = raw[rowStart];
            for (int x = 0; x < rowSize; x++)
            {
                byte a = x >= bpp ? pixels[y * rowSize + x - bpp] : (byte)0;
                byte b = y > 0 ? pixels[(y - 1) * rowSize + x] : (byte)0;
                byte c = (x >= bpp && y > 0) ? pixels[(y - 1) * rowSize + x - bpp] : (byte)0;
                byte cur = raw[rowStart + 1 + x];
                byte outVal = filter switch
                {
                    0 => cur,
                    1 => (byte)(cur + a),
                    2 => (byte)(cur + b),
                    3 => (byte)(cur + (byte)((a + b) / 2)),
                    4 => (byte)(cur + Paeth(a, b, c)),
                    _ => throw new NotSupportedException($"PNG filter {filter}"),
                };
                pixels[y * rowSize + x] = outVal;
            }
        }

        byte[] rgba = new byte[w * h * 4];
        for (int i = 0; i < w * h; i++)
        {
            switch (colorType)
            {
                case 0:  // Gray
                    rgba[i * 4 + 0] = rgba[i * 4 + 1] = rgba[i * 4 + 2] = pixels[i];
                    rgba[i * 4 + 3] = 255;
                    break;
                case 2:  // RGB
                    rgba[i * 4 + 0] = pixels[i * 3 + 0];
                    rgba[i * 4 + 1] = pixels[i * 3 + 1];
                    rgba[i * 4 + 2] = pixels[i * 3 + 2];
                    rgba[i * 4 + 3] = 255;
                    break;
                case 4:  // Gray+Alpha
                    rgba[i * 4 + 0] = rgba[i * 4 + 1] = rgba[i * 4 + 2] = pixels[i * 2 + 0];
                    rgba[i * 4 + 3] = pixels[i * 2 + 1];
                    break;
                case 6:  // RGBA
                    for (int k = 0; k < 4; k++) rgba[i * 4 + k] = pixels[i * 4 + k];
                    break;
                case 3:  // Palette
                    {
                        int idx = pixels[i];
                        rgba[i * 4 + 0] = palette![idx * 3 + 0];
                        rgba[i * 4 + 1] = palette![idx * 3 + 1];
                        rgba[i * 4 + 2] = palette![idx * 3 + 2];
                        rgba[i * 4 + 3] = (paletteAlpha is not null && idx < paletteAlpha.Length) ? paletteAlpha[idx] : (byte)255;
                        break;
                    }
            }
        }
        return (w, h, rgba);
    }

    private static int ReadBE(byte[] b, int off) =>
        (b[off] << 24) | (b[off + 1] << 16) | (b[off + 2] << 8) | b[off + 3];

    private static byte Paeth(byte a, byte b, byte c)
    {
        int p = a + b - c;
        int pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }
}
