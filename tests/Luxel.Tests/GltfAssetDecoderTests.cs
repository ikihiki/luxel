using System.Numerics;
using System.Text;
using Luxel.Assets.Gltf;

namespace Luxel.Tests;

public sealed class GltfAssetDecoderTests
{
    [Fact]
    public async Task MinimalJson_DecodesTypedRelationsAndMetadata()
    {
        byte[] json = Encoding.UTF8.GetBytes("""
        {
          "asset": { "version": "2.0", "generator": "unit-test" },
          "scene": 0,
          "scenes": [{ "nodes": [0] }],
          "nodes": [{ "name": "root", "translation": [1, 2, 3] }]
        }
        """);

        var index = GltfDecoder.ParseIndex(json);
        Assert.Equal(new NodeIndex(0), index.Scenes[0].Nodes[0]);
        var document = await GltfDecoder.DecodeAsync(json);
        Assert.Equal("unit-test", document.Generator);
        Assert.Equal(new Vector3(1, 2, 3), document.Nodes[0].Translation);
        Assert.Same(document.Nodes[0], document.DefaultScene!.Roots[0]);
    }

    [Fact]
    public async Task Glb_DecodesEmbeddedBinaryChunk()
    {
        byte[] positions = Floats(0, 0, 0, 1, 0, 0, 0, 1, 0);
        byte[] json = Encoding.UTF8.GetBytes("""
        {"asset":{"version":"2.0"},"nodes":[{"mesh":0}],"meshes":[{"primitives":[{"attributes":{"POSITION":0}}]}],"buffers":[{"byteLength":36}],"bufferViews":[{"buffer":0,"byteLength":36}],"accessors":[{"bufferView":0,"componentType":5126,"count":3,"type":"VEC3"}]}
        """);
        byte[] glb = MakeGlb(json, positions);

        var document = await GltfDecoder.DecodeAsync(glb);

        Assert.Equal("glb", document.SourceFormat);
        Assert.Equal(new Vector3(0, 1, 0), document.Meshes[0].Primitives[0].Attributes.Positions[2]);
    }

    [Fact]
    public async Task ExternalResolver_ReceivesRelativeReference()
    {
        byte[] positions = Floats(2, 3, 4);
        byte[] json = Encoding.UTF8.GetBytes("""
        {"asset":{"version":"2.0"},"nodes":[{"mesh":0}],"meshes":[{"primitives":[{"attributes":{"POSITION":0},"mode":0}]}],"buffers":[{"uri":"mesh.bin","byteLength":12}],"bufferViews":[{"buffer":0,"byteLength":12}],"accessors":[{"bufferView":0,"componentType":5126,"count":1,"type":"VEC3"}]}
        """);
        string? requested = null;

        var document = await GltfDecoder.DecodeAsync(json, (uri, _) =>
        {
            requested = uri;
            return ValueTask.FromResult<ReadOnlyMemory<byte>>(positions);
        });

        Assert.Equal("mesh.bin", requested);
        Assert.Equal(new Vector3(2, 3, 4), document.Meshes[0].Primitives[0].Attributes.Positions[0]);
        Assert.Equal(Luxel.Assets.AssetTopology.Points, document.Meshes[0].Primitives[0].Topology);
    }

    [Fact]
    public async Task InvalidIndex_IsRejectedBeforeConversion()
    {
        byte[] json = Encoding.UTF8.GetBytes("""
        {"asset":{"version":"2.0"},"nodes":[{"mesh":0}]}
        """);

        var error = await Assert.ThrowsAsync<InvalidDataException>(() => GltfDecoder.DecodeAsync(json));
        Assert.Contains("node.mesh", error.Message);
    }

    private static byte[] Floats(params float[] values)
    {
        var result = new byte[values.Length * sizeof(float)];
        for (int i = 0; i < values.Length; i++) BitConverter.GetBytes(values[i]).CopyTo(result, i * sizeof(float));
        return result;
    }

    private static byte[] MakeGlb(byte[] json, byte[] binary)
    {
        int jsonLength = (json.Length + 3) & ~3;
        int binaryLength = (binary.Length + 3) & ~3;
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(0x46546c67u);
        writer.Write(2u);
        writer.Write((uint)(12 + 8 + jsonLength + 8 + binaryLength));
        writer.Write((uint)jsonLength);
        writer.Write(0x4e4f534au);
        writer.Write(json);
        for (int i = json.Length; i < jsonLength; i++) writer.Write((byte)' ');
        writer.Write((uint)binaryLength);
        writer.Write(0x004e4942u);
        writer.Write(binary);
        for (int i = binary.Length; i < binaryLength; i++) writer.Write((byte)0);
        return stream.ToArray();
    }
}
