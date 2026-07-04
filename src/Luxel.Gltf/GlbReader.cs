using System.Text;
using System.Text.Json;

namespace Luxel.Gltf;

/// <summary>
/// .glb (binary glTF) chunk reader。https://github.com/KhronosGroup/glTF/blob/main/specification/2.0/README.md#binary-gltf-layout
/// Header (12B) + Chunk0(JSON) + Chunk1(BIN)。
/// </summary>
internal static class GlbReader
{
    public static async Task<(GltfJson json, byte[]? binary)> ReadAsync(string path)
    {
        var bytes = await File.ReadAllBytesAsync(path).ConfigureAwait(false);
        return ReadFromBytes(bytes);
    }

    public static (GltfJson json, byte[]? binary) ReadFromBytes(byte[] bytes)
    {
        if (bytes.Length < 12) throw new InvalidDataException("glb too small");
        if (BitConverter.ToUInt32(bytes, 0) != 0x46546C67)
            throw new InvalidDataException("not a glb (magic missing)");
        // version = bytes[4..8], length = bytes[8..12]
        int total = BitConverter.ToInt32(bytes, 8);
        if (total > bytes.Length) throw new InvalidDataException("glb length mismatch");

        // chunk 0: JSON
        int chunk0Len = BitConverter.ToInt32(bytes, 12);
        uint chunk0Type = BitConverter.ToUInt32(bytes, 16);
        if (chunk0Type != 0x4E4F534A) throw new InvalidDataException("chunk 0 must be JSON");
        string jsonText = Encoding.UTF8.GetString(bytes, 20, chunk0Len);
        var json = JsonSerializer.Deserialize<GltfJson>(jsonText, JsonOpts)
                   ?? throw new InvalidDataException("invalid glb JSON");

        // chunk 1 (optional): BIN
        byte[]? binary = null;
        int offset = 20 + chunk0Len;
        if (offset < total)
        {
            int chunk1Len = BitConverter.ToInt32(bytes, offset);
            uint chunk1Type = BitConverter.ToUInt32(bytes, offset + 4);
            if (chunk1Type == 0x004E4942)  // "BIN\0"
            {
                binary = new byte[chunk1Len];
                Buffer.BlockCopy(bytes, offset + 8, binary, 0, chunk1Len);
            }
        }
        return (json, binary);
    }

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };
}
