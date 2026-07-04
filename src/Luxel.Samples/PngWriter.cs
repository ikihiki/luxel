using System.IO.Compression;

namespace Luxel.Samples;

/// <summary>依存無しの最小 PNG (RGBA8) ライタ。サンプルの描画結果確認 + DevTools の format=png 配信用。</summary>
public static class PngWriter
{
    public static void WriteRgba(string path, int width, int height, ReadOnlySpan<byte> rgba)
    {
        using var fs = File.Create(path);
        WriteRgba(fs, width, height, rgba);
    }

    /// <summary>PNG バイト列を返す (HTTP 配信用)。</summary>
    public static byte[] ToBytes(int width, int height, ReadOnlySpan<byte> rgba)
    {
        var ms = new MemoryStream();
        WriteRgba(ms, width, height, rgba);
        return ms.ToArray();
    }

    public static void WriteRgba(Stream fs, int width, int height, ReadOnlySpan<byte> rgba)
    {
        fs.Write([137, 80, 78, 71, 13, 10, 26, 10]); // PNG シグネチャ

        var ihdr = new byte[13];
        WriteBE(ihdr, 0, (uint)width);
        WriteBE(ihdr, 4, (uint)height);
        ihdr[8] = 8;  // bit depth
        ihdr[9] = 6;  // color type RGBA
        WriteChunk(fs, "IHDR", ihdr);

        using var ms = new MemoryStream();
        using (var z = new ZLibStream(ms, CompressionLevel.Optimal, leaveOpen: true))
        {
            var row = new byte[1 + width * 4];
            for (int y = 0; y < height; y++)
            {
                row[0] = 0; // filter: none
                rgba.Slice(y * width * 4, width * 4).CopyTo(row.AsSpan(1));
                z.Write(row, 0, row.Length);
            }
        }
        WriteChunk(fs, "IDAT", ms.ToArray());
        WriteChunk(fs, "IEND", []);
    }

    private static void WriteBE(byte[] buf, int offset, uint value)
    {
        buf[offset] = (byte)(value >> 24);
        buf[offset + 1] = (byte)(value >> 16);
        buf[offset + 2] = (byte)(value >> 8);
        buf[offset + 3] = (byte)value;
    }

    private static void WriteChunk(Stream s, string type, byte[] data)
    {
        Span<byte> len = stackalloc byte[4];
        WriteBE2(len, (uint)data.Length);
        s.Write(len);
        byte[] typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        s.Write(typeBytes);
        s.Write(data);
        uint crc = Crc32(typeBytes, data);
        Span<byte> crcb = stackalloc byte[4];
        WriteBE2(crcb, crc);
        s.Write(crcb);
    }

    private static void WriteBE2(Span<byte> buf, uint value)
    {
        buf[0] = (byte)(value >> 24);
        buf[1] = (byte)(value >> 16);
        buf[2] = (byte)(value >> 8);
        buf[3] = (byte)value;
    }

    private static uint Crc32(byte[] a, byte[] b)
    {
        uint crc = 0xFFFFFFFFu;
        crc = Update(crc, a);
        crc = Update(crc, b);
        return crc ^ 0xFFFFFFFFu;

        static uint Update(uint crc, byte[] data)
        {
            foreach (byte x in data)
            {
                crc ^= x;
                for (int i = 0; i < 8; i++)
                    crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
            return crc;
        }
    }
}
