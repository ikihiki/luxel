using Friflo.Engine.ECS;

namespace Luxel.Assets;

/// <summary>
/// この entity がレンダリングする mesh への直接参照。
/// SceneBuilder が AssetNode.Mesh を entity に落とすときにセットする。
/// </summary>
public struct AssetMeshRef : IComponent
{
    /// <summary>参照先の mesh。</summary>
    public AssetMesh Mesh;
    /// <summary>mesh を指定して作成。</summary>
    public AssetMeshRef(AssetMesh mesh) { Mesh = mesh; }
}

/// <summary>マテリアル直接参照 (primitive 単位で複数持つ mesh の場合は、shard された child entity で持つ)。</summary>
public struct AssetMaterialRef : IComponent
{
    /// <summary>参照先のマテリアル。</summary>
    public AssetMaterial Material;
    /// <summary>マテリアルを指定して作成。</summary>
    public AssetMaterialRef(AssetMaterial m) { Material = m; }
}

/// <summary>Skin 直接参照 (joint matrix を毎フレーム計算)。</summary>
public struct AssetSkinRef : IComponent
{
    /// <summary>参照先のスキン。</summary>
    public AssetSkin Skin;
    /// <summary>スキンを指定して作成。</summary>
    public AssetSkinRef(AssetSkin s) { Skin = s; }
}

/// <summary>AssetNode.Name (debug 表示用)。</summary>
public struct AssetNodeName : IComponent
{
    /// <summary>node 名。</summary>
    public string Value;
    /// <summary>名前を指定して作成。</summary>
    public AssetNodeName(string v) { Value = v; }
}
