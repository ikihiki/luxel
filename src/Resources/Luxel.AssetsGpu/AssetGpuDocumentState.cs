using Luxel.Assets;

namespace Luxel.AssetsGpu;

/// <summary>
/// 1つの <see cref="AssetDocument"/> に対する GPU installation state。
/// 同じ typed index はこの state 内で一度だけ upload され、別 document の同じ数値 index とは共有されない。
/// </summary>
public sealed class AssetGpuDocumentState
{
    internal AssetGpuDocumentState(AssetDocument document) => Document = document;

    public AssetDocument Document { get; }
    public bool IsInstalled { get; internal set; }

    internal Dictionary<AssetMeshIndex, GpuMesh> Meshes { get; } = new();
    internal Dictionary<AssetPrimitiveIndex, GpuPrimitive> Primitives { get; } = new();
    internal Dictionary<AssetMaterialIndex, GpuMaterial> Materials { get; } = new();
    internal Dictionary<AssetTextureIndex, GpuTexture> Textures { get; } = new();
    internal Dictionary<AssetSamplerIndex, GpuSampler> Samplers { get; } = new();
    internal Dictionary<AssetSkinIndex, GpuSkin> Skins { get; } = new();

    // Factory API remains direct-reference based, so these caches are document-scoped bridges.
    internal Dictionary<AssetMesh, GpuMesh> MeshObjects { get; } = new(ReferenceEqualityComparer.Instance);
    internal Dictionary<AssetMaterial, GpuMaterial> MaterialObjects { get; } = new(ReferenceEqualityComparer.Instance);
    internal Dictionary<AssetTexture, GpuTexture> TextureObjects { get; } = new(ReferenceEqualityComparer.Instance);
    internal Dictionary<AssetSampler, GpuSampler> SamplerObjects { get; } = new(ReferenceEqualityComparer.Instance);
    internal Dictionary<AssetSkin, GpuSkin> SkinObjects { get; } = new(ReferenceEqualityComparer.Instance);
}
