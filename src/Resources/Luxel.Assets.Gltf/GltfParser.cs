using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using System.Text.Json;

namespace Luxel.Assets.Gltf;

internal static class GltfParser
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    public static (GltfIndexDocument Document, byte[]? Binary, string SourceFormat) Parse(ReadOnlySpan<byte> bytes)
    {
        WireRoot root;
        byte[]? binary = null;
        string sourceFormat;
        if (bytes.Length >= 4 && BinaryPrimitives.ReadUInt32LittleEndian(bytes) == 0x46546c67)
        {
            (root, binary) = ParseGlb(bytes);
            sourceFormat = "glb";
        }
        else
        {
            root = JsonSerializer.Deserialize<WireRoot>(bytes, Options)
                ?? throw new InvalidDataException("invalid glTF JSON");
            sourceFormat = "gltf";
        }

        var document = ToIndexDocument(root);
        GltfValidator.Validate(document);
        return (document, binary, sourceFormat);
    }

    private static (WireRoot Root, byte[]? Binary) ParseGlb(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 20) throw new InvalidDataException("GLB is too small.");
        uint version = BinaryPrimitives.ReadUInt32LittleEndian(bytes[4..]);
        uint declaredLength = BinaryPrimitives.ReadUInt32LittleEndian(bytes[8..]);
        if (version != 2) throw new InvalidDataException($"Unsupported GLB version {version}.");
        if (declaredLength != bytes.Length) throw new InvalidDataException("GLB length does not match its header.");

        int offset = 12;
        WireRoot? root = null;
        byte[]? binary = null;
        while (offset < bytes.Length)
        {
            if (bytes.Length - offset < 8) throw new InvalidDataException("Truncated GLB chunk header.");
            int length = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes[(offset + 0)..]));
            uint type = BinaryPrimitives.ReadUInt32LittleEndian(bytes[(offset + 4)..]);
            offset += 8;
            if (length < 0 || length > bytes.Length - offset) throw new InvalidDataException("Truncated GLB chunk.");
            var chunk = bytes.Slice(offset, length);
            if (type == 0x4e4f534a && root is null)
                root = JsonSerializer.Deserialize<WireRoot>(chunk, Options);
            else if (type == 0x004e4942 && binary is null)
                binary = chunk.ToArray();
            offset += length;
        }
        return (root ?? throw new InvalidDataException("GLB has no JSON chunk."), binary);
    }

    private static GltfIndexDocument ToIndexDocument(WireRoot root)
    {
        var meshes = (root.meshes ?? []).Select((mesh, meshIndex) => new GltfMesh(mesh.name,
            (mesh.primitives ?? []).Select((primitive, primitiveIndex) => new GltfPrimitive(
                new PrimitiveIndex(new MeshIndex(meshIndex), primitiveIndex),
                (primitive.attributes ?? []).ToDictionary(pair => pair.Key, pair => new AccessorIndex(pair.Value)),
                primitive.indices is int indices ? new AccessorIndex(indices) : null,
                primitive.material is int material ? new MaterialIndex(material) : null,
                primitive.mode ?? 4,
                (primitive.targets ?? []).Select(target => (IReadOnlyDictionary<string, AccessorIndex>)target
                    .ToDictionary(pair => pair.Key, pair => new AccessorIndex(pair.Value))).ToArray())).ToArray(),
            mesh.weights)).ToArray();

        return new GltfIndexDocument
        {
            Version = root.asset?.version ?? "",
            Generator = root.asset?.generator,
            DefaultScene = root.scene is int scene ? new SceneIndex(scene) : null,
            Scenes = (root.scenes ?? []).Select(scene => new GltfScene(scene.name,
                (scene.nodes ?? []).Select(value => new NodeIndex(value)).ToArray())).ToArray(),
            Nodes = (root.nodes ?? []).Select(node => new GltfNode(node.name,
                (node.children ?? []).Select(value => new NodeIndex(value)).ToArray(),
                node.mesh is int mesh ? new MeshIndex(mesh) : null,
                node.skin is int skin ? new SkinIndex(skin) : null,
                node.translation, node.rotation, node.scale, node.matrix, node.weights)).ToArray(),
            Meshes = meshes,
            Materials = (root.materials ?? []).Select(material =>
            {
                var pbr = material.pbrMetallicRoughness;
                var factor = pbr?.baseColorFactor is { Length: >= 4 } f ? new Vector4(f[0], f[1], f[2], f[3]) : Vector4.One;
                var texture = pbr?.baseColorTexture is { } info
                    ? new GltfTextureInfo(new TextureIndex(info.index), info.texCoord ?? 0) : null;
                return new GltfMaterial(material.name, factor, texture, pbr?.metallicFactor ?? 1,
                    pbr?.roughnessFactor ?? 1, material.alphaMode, material.alphaCutoff ?? 0.5f, material.doubleSided);
            }).ToArray(),
            Textures = (root.textures ?? []).Select(texture => new GltfTexture(texture.name,
                texture.source is int source ? new ImageIndex(source) : null,
                texture.sampler is int sampler ? new SamplerIndex(sampler) : null)).ToArray(),
            Images = (root.images ?? []).Select(image => new GltfImage(image.name, image.uri,
                image.bufferView is int view ? new BufferViewIndex(view) : null, image.mimeType)).ToArray(),
            Samplers = (root.samplers ?? []).Select(sampler => new GltfSampler(sampler.name, sampler.magFilter,
                sampler.minFilter, sampler.wrapS ?? 10497, sampler.wrapT ?? 10497)).ToArray(),
            Buffers = (root.buffers ?? []).Select(buffer => new GltfBuffer(buffer.uri, buffer.byteLength)).ToArray(),
            BufferViews = (root.bufferViews ?? []).Select(view => new GltfBufferView(new BufferIndex(view.buffer),
                view.byteOffset ?? 0, view.byteLength, view.byteStride)).ToArray(),
            Accessors = (root.accessors ?? []).Select(accessor => new GltfAccessor(
                accessor.bufferView is int view ? new BufferViewIndex(view) : null, accessor.byteOffset ?? 0,
                accessor.componentType, accessor.normalized, accessor.count, accessor.type ?? "")).ToArray(),
            Skins = (root.skins ?? []).Select(skin => new GltfSkin(skin.name,
                skin.inverseBindMatrices is int matrices ? new AccessorIndex(matrices) : null,
                (skin.joints ?? []).Select(value => new NodeIndex(value)).ToArray(),
                skin.skeleton is int skeleton ? new NodeIndex(skeleton) : null)).ToArray(),
            Animations = (root.animations ?? []).Select((animation, animationIndex) => new GltfAnimation(animation.name,
                (animation.channels ?? []).Select(channel => new GltfAnimationChannel(
                    new AnimationSamplerIndex(new AnimationIndex(animationIndex), channel.sampler),
                    channel.target?.node is int node ? new NodeIndex(node) : null, channel.target?.path ?? "")).ToArray(),
                (animation.samplers ?? []).Select(sampler => new GltfAnimationSampler(new AccessorIndex(sampler.input),
                    new AccessorIndex(sampler.output), sampler.interpolation ?? "LINEAR")).ToArray())).ToArray(),
        };
    }

    private sealed class WireRoot
    {
        public WireAsset? asset { get; set; } public int? scene { get; set; }
        public WireScene[]? scenes { get; set; } public WireNode[]? nodes { get; set; }
        public WireMesh[]? meshes { get; set; } public WireMaterial[]? materials { get; set; }
        public WireTexture[]? textures { get; set; } public WireImage[]? images { get; set; }
        public WireSampler[]? samplers { get; set; } public WireAccessor[]? accessors { get; set; }
        public WireBufferView[]? bufferViews { get; set; } public WireBuffer[]? buffers { get; set; }
        public WireAnimation[]? animations { get; set; } public WireSkin[]? skins { get; set; }
    }
    private sealed class WireAsset { public string? version { get; set; } public string? generator { get; set; } }
    private sealed class WireScene { public string? name { get; set; } public int[]? nodes { get; set; } }
    private sealed class WireNode { public string? name { get; set; } public int[]? children { get; set; } public int? mesh { get; set; } public int? skin { get; set; } public float[]? translation { get; set; } public float[]? rotation { get; set; } public float[]? scale { get; set; } public float[]? matrix { get; set; } public float[]? weights { get; set; } }
    private sealed class WireMesh { public string? name { get; set; } public WirePrimitive[]? primitives { get; set; } public float[]? weights { get; set; } }
    private sealed class WirePrimitive { public Dictionary<string, int>? attributes { get; set; } public int? indices { get; set; } public int? material { get; set; } public int? mode { get; set; } public Dictionary<string, int>[]? targets { get; set; } }
    private sealed class WireMaterial { public string? name { get; set; } public WirePbr? pbrMetallicRoughness { get; set; } public string? alphaMode { get; set; } public float? alphaCutoff { get; set; } public bool doubleSided { get; set; } }
    private sealed class WirePbr { public float[]? baseColorFactor { get; set; } public WireTextureInfo? baseColorTexture { get; set; } public float? metallicFactor { get; set; } public float? roughnessFactor { get; set; } }
    private sealed class WireTextureInfo { public int index { get; set; } public int? texCoord { get; set; } }
    private sealed class WireTexture { public string? name { get; set; } public int? source { get; set; } public int? sampler { get; set; } }
    private sealed class WireImage { public string? name { get; set; } public string? uri { get; set; } public int? bufferView { get; set; } public string? mimeType { get; set; } }
    private sealed class WireSampler { public string? name { get; set; } public int? magFilter { get; set; } public int? minFilter { get; set; } public int? wrapS { get; set; } public int? wrapT { get; set; } }
    private sealed class WireAccessor { public int? bufferView { get; set; } public int? byteOffset { get; set; } public int componentType { get; set; } public bool normalized { get; set; } public int count { get; set; } public string? type { get; set; } }
    private sealed class WireBufferView { public int buffer { get; set; } public int? byteOffset { get; set; } public int byteLength { get; set; } public int? byteStride { get; set; } }
    private sealed class WireBuffer { public string? uri { get; set; } public int byteLength { get; set; } }
    private sealed class WireSkin { public string? name { get; set; } public int? inverseBindMatrices { get; set; } public int[]? joints { get; set; } public int? skeleton { get; set; } }
    private sealed class WireAnimation { public string? name { get; set; } public WireAnimationChannel[]? channels { get; set; } public WireAnimationSampler[]? samplers { get; set; } }
    private sealed class WireAnimationChannel { public int sampler { get; set; } public WireAnimationTarget? target { get; set; } }
    private sealed class WireAnimationTarget { public int? node { get; set; } public string? path { get; set; } }
    private sealed class WireAnimationSampler { public int input { get; set; } public int output { get; set; } public string? interpolation { get; set; } }
}
