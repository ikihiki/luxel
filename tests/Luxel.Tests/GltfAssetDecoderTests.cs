using System.Numerics;
using System.Text;
using Luxel.Assets;
using Luxel.Assets.Gltf;
using Luxel.Resources;

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
    public async Task ResourceSystem_LoadsRelativeExternalBufferAndImage()
    {
        byte[] positions = Floats(2, 3, 4);
        byte[] png = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8/5+hHgAHggJ/PchI7wAAAABJRU5ErkJggg==");
        byte[] json = Encoding.UTF8.GetBytes("""
        {"asset":{"version":"2.0"},"nodes":[{"mesh":0}],"meshes":[{"primitives":[{"attributes":{"POSITION":0},"mode":0}]}],"buffers":[{"uri":"../buffers/mesh.bin","byteLength":12}],"bufferViews":[{"buffer":0,"byteLength":12}],"accessors":[{"bufferView":0,"componentType":5126,"count":1,"type":"VEC3"}],"images":[{"uri":"../textures/pixel.png"}],"textures":[{"source":0}]}
        """);
        var files = new MemoryFileSystem();
        files.Set("assets/models/scene.gltf", json);
        files.Set("assets/buffers/mesh.bin", positions);
        files.Set("assets/textures/pixel.png", png);
        using var resources = CreateResources(new FileSource(files));

        using ResourceHandle<AssetDocument> handle = resources.Load<AssetDocument>("assets/models/scene.gltf");
        await handle.Ready;

        Assert.Equal(new Vector3(2, 3, 4), handle.Value.Meshes[0].Primitives[0].Attributes.Positions[0]);
        Assert.Equal(AssetTopology.Points, handle.Value.Meshes[0].Primitives[0].Topology);
        Assert.Equal(1, handle.Value.Textures[0].Width);
        Assert.Equal(1, handle.Value.Textures[0].Height);
        Assert.Equal("../textures/pixel.png", handle.Value.Textures[0].SourceUri);
    }

    [Fact]
    public async Task ResourceSystem_AwaitsAsyncExternalSourceWithoutBlockingLoad()
    {
        byte[] positions = Floats(5, 6, 7);
        byte[] json = Encoding.UTF8.GetBytes("""
        {"asset":{"version":"2.0"},"nodes":[{"mesh":0}],"meshes":[{"primitives":[{"attributes":{"POSITION":0},"mode":0}]}],"buffers":[{"uri":"mesh.bin","byteLength":12}],"bufferViews":[{"buffer":0,"byteLength":12}],"accessors":[{"bufferView":0,"componentType":5126,"count":1,"type":"VEC3"}]}
        """);
        var source = new GatedSource("models/scene.gltf", json, "models/mesh.bin", positions);
        using var resources = CreateResources(source);

        using ResourceHandle<AssetDocument> handle = resources.Load<AssetDocument>("models/scene.gltf");
        await source.DependencyRequested.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(handle.Ready.IsCompleted);
        source.ReleaseDependency();
        await handle.Ready.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(new Vector3(5, 6, 7), handle.Value.Meshes[0].Primitives[0].Attributes.Positions[0]);
    }

    [Fact]
    public void PublicDecodeApi_DoesNotExposeExternalResolver()
    {
        var publicDecodeMethods = typeof(GltfDecoder).GetMethods()
            .Where(method => method.Name == nameof(GltfDecoder.DecodeAsync));

        Assert.All(publicDecodeMethods, method =>
            Assert.DoesNotContain(method.GetParameters(), parameter =>
                parameter.ParameterType.IsGenericType &&
                parameter.ParameterType.GetGenericTypeDefinition() == typeof(Func<,,>)));
    }

    [Fact]
    public async Task ResourceSystem_ExternalDependencyFailureIncludesReferenceAndResolvedUri()
    {
        byte[] json = Encoding.UTF8.GetBytes("""
        {"asset":{"version":"2.0"},"buffers":[{"uri":"../buffers/missing.bin","byteLength":1}]}
        """);
        var files = new MemoryFileSystem();
        files.Set("assets/models/scene.gltf", json);
        using var resources = CreateResources(new FileSource(files));

        using ResourceHandle<AssetDocument> handle = resources.Load<AssetDocument>("assets/models/scene.gltf");
        var error = await Assert.ThrowsAsync<InvalidDataException>(async () => await handle.Ready);

        Assert.Contains("../buffers/missing.bin", error.Message);
        Assert.Contains("assets/buffers/missing.bin", error.Message);
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

    private static ResourceSystem CreateResources(IResourceSource source)
    {
        return ResourceTestSystem.Create(
            sources: [source],
            configure: (builder, handles) => builder.Steps.Add<byte[], AssetDocument>(new GltfResourceStep())
                .RunOn(handles.CpuDomain).ManagedBy(handles.CpuManager).Register());
    }

    private sealed class GatedSource(
        string documentPath,
        byte[] document,
        string dependencyPath,
        byte[] dependency) : IResourceSource
    {
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IEnumerable<string> Schemes => ["file", ""];
        public TaskCompletionSource DependencyRequested { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<byte[]> ReadAsync(ResourceUri uri, LoadContext context)
        {
            if (uri.Path == documentPath) return (byte[])document.Clone();
            if (uri.Path != dependencyPath) throw new FileNotFoundException(uri.Path);

            DependencyRequested.TrySetResult();
            await _release.Task.WaitAsync(context.Token).ConfigureAwait(false);
            return (byte[])dependency.Clone();
        }

        public void ReleaseDependency() => _release.TrySetResult();
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
