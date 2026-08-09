using System.Numerics;

namespace Luxel.Assets.Gltf;

public readonly record struct NodeIndex(int Value);
public readonly record struct MeshIndex(int Value);
public readonly record struct PrimitiveIndex(MeshIndex Mesh, int Value);
public readonly record struct MaterialIndex(int Value);
public readonly record struct TextureIndex(int Value);
public readonly record struct ImageIndex(int Value);
public readonly record struct BufferIndex(int Value);
public readonly record struct BufferViewIndex(int Value);
public readonly record struct AccessorIndex(int Value);
public readonly record struct SkinIndex(int Value);
public readonly record struct AnimationIndex(int Value);
public readonly record struct SamplerIndex(int Value);
public readonly record struct SceneIndex(int Value);
public readonly record struct AnimationSamplerIndex(AnimationIndex Animation, int Value);

/// <summary>glTF の index relation を型付き handle のまま保持する中間 DTO。</summary>
public sealed class GltfIndexDocument
{
    public string Version { get; init; } = "";
    public string? Generator { get; init; }
    public SceneIndex? DefaultScene { get; init; }
    public IReadOnlyList<GltfScene> Scenes { get; init; } = [];
    public IReadOnlyList<GltfNode> Nodes { get; init; } = [];
    public IReadOnlyList<GltfMesh> Meshes { get; init; } = [];
    public IReadOnlyList<GltfMaterial> Materials { get; init; } = [];
    public IReadOnlyList<GltfTexture> Textures { get; init; } = [];
    public IReadOnlyList<GltfImage> Images { get; init; } = [];
    public IReadOnlyList<GltfSampler> Samplers { get; init; } = [];
    public IReadOnlyList<GltfBuffer> Buffers { get; init; } = [];
    public IReadOnlyList<GltfBufferView> BufferViews { get; init; } = [];
    public IReadOnlyList<GltfAccessor> Accessors { get; init; } = [];
    public IReadOnlyList<GltfSkin> Skins { get; init; } = [];
    public IReadOnlyList<GltfAnimation> Animations { get; init; } = [];
}

public sealed record GltfScene(string? Name, IReadOnlyList<NodeIndex> Nodes);
public sealed record GltfNode(string? Name, IReadOnlyList<NodeIndex> Children, MeshIndex? Mesh, SkinIndex? Skin,
    float[]? Translation, float[]? Rotation, float[]? Scale, float[]? Matrix, float[]? Weights);
public sealed record GltfMesh(string? Name, IReadOnlyList<GltfPrimitive> Primitives, float[]? Weights);
public sealed record GltfPrimitive(PrimitiveIndex Index, IReadOnlyDictionary<string, AccessorIndex> Attributes,
    AccessorIndex? Indices, MaterialIndex? Material, int Mode,
    IReadOnlyList<IReadOnlyDictionary<string, AccessorIndex>> Targets);
public sealed record GltfMaterial(string? Name, Vector4 BaseColorFactor, GltfTextureInfo? BaseColorTexture,
    float MetallicFactor, float RoughnessFactor, string? AlphaMode, float AlphaCutoff, bool DoubleSided);
public sealed record GltfTextureInfo(TextureIndex Texture, int TexCoord);
public sealed record GltfTexture(string? Name, ImageIndex? Source, SamplerIndex? Sampler);
public sealed record GltfImage(string? Name, string? Uri, BufferViewIndex? BufferView, string? MimeType);
public sealed record GltfSampler(string? Name, int? MagFilter, int? MinFilter, int WrapS, int WrapT);
public sealed record GltfBuffer(string? Uri, int ByteLength);
public sealed record GltfBufferView(BufferIndex Buffer, int ByteOffset, int ByteLength, int? ByteStride);
public sealed record GltfAccessor(BufferViewIndex? BufferView, int ByteOffset, int ComponentType,
    bool Normalized, int Count, string Type);
public sealed record GltfSkin(string? Name, AccessorIndex? InverseBindMatrices,
    IReadOnlyList<NodeIndex> Joints, NodeIndex? Skeleton);
public sealed record GltfAnimation(string? Name, IReadOnlyList<GltfAnimationChannel> Channels,
    IReadOnlyList<GltfAnimationSampler> Samplers);
public sealed record GltfAnimationChannel(AnimationSamplerIndex Sampler, NodeIndex? Target, string Path);
public sealed record GltfAnimationSampler(AccessorIndex Input, AccessorIndex Output, string Interpolation);
