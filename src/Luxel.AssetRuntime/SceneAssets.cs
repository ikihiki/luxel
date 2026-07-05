using System.Numerics;
using Friflo.Engine.ECS;
using Luxel.Assets;
using Luxel.Ecs;

namespace Luxel.AssetRuntime;

/// <summary>
/// AssetDocument から build した GPU リソースの保持先。SceneBuilder.Build で生成。
/// direct-ref 版: 各 mapping は <see cref="AssetMesh"/> / <see cref="AssetMaterial"/> をキーに直接 GPU state を引ける。
/// Sample 側は entity から <see cref="AssetMeshRef.Mesh"/> 経由でルックアップする。
/// </summary>
public sealed class SceneAssets : IDisposable
{
    /// <summary>Primitive → GPU state (1 mesh に複数 primitive があるため primitive 単位)。</summary>
    public Dictionary<AssetPrimitive, ScenePrimitiveGpu> Primitives { get; } = new();

    /// <summary>Mesh の primitive 一覧を素早く引くための逆引き (Sample が iterate 用)。</summary>
    public Dictionary<AssetMesh, List<AssetPrimitive>> MeshPrimitives { get; } = new();

    /// <summary>SceneBuilder が processing した primitive の登場順リスト (fragment URI 用の stable index)。</summary>
    public List<AssetPrimitive> OrderedPrimitives { get; } = new();

    /// <summary>マテリアル → shader 用 material index (bindless material buffer の要素番号)。</summary>
    public Dictionary<AssetMaterial, int> MaterialIndex { get; } = new();
    /// <summary>material index 順の GPU state 配列。</summary>
    public List<SceneMaterialGpu> MaterialArray { get; } = new();

    /// <summary>Skin runtime state (joint entity ルックアップ用)。</summary>
    public Dictionary<AssetSkin, int> SkinIndex { get; } = new();
    public List<AssetSkin> SkinArray { get; } = new();

    /// <summary>Node → Entity のマッピング (animation / skin の joint 解決に使用)。</summary>
    public Dictionary<AssetNode, Entity> NodeEntities { get; } = new();

    /// <summary>bindless texture sampling で共有する既定サンプラ。</summary>
    public GpuSampler? DefaultSampler;

    public void Dispose()
    {
        foreach (var p in Primitives.Values) p.Dispose();
        foreach (var m in MaterialArray) m.Dispose();
        DefaultSampler?.Dispose();
    }
}

/// <summary>1 primitive ぶんの GPU state (vertex/index buffer + メタ情報)。</summary>
public sealed class ScenePrimitiveGpu : IDisposable
{
    public GpuBuffer VertexBuffer = null!;
    public GpuBuffer IndexBuffer = null!;
    public int VertexCount;
    public int IndexCount;
    public int VertexStride;
    public int MaterialIndex = -1;
    public bool HasSkinning;

    public void Dispose()
    {
        VertexBuffer?.Dispose();
        IndexBuffer?.Dispose();
    }
}

/// <summary>1 material ぶんの GPU state (base color + optional texture)。</summary>
public sealed class SceneMaterialGpu : IDisposable
{
    public Vector4 BaseColor = Vector4.One;
    public GpuTexture? BaseColorTexture;

    public void Dispose()
    {
        BaseColorTexture?.Dispose();
    }
}
