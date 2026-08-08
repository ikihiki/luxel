using System.Numerics;

namespace Luxel.Assets;

/// <summary>頂点データ集合。複数 primitive で構成 (multi-material など)。</summary>
public sealed class AssetMesh
{
    /// <summary>名前 (glTF の name)。</summary>
    public string? Name { get; set; }
    /// <summary>この mesh を構成する primitive の一覧 (1 primitive = 1 draw call)。</summary>
    public List<AssetPrimitive> Primitives { get; } = new();
    /// <summary>mesh 全体の AABB (全 primitive の union、未計算なら null)。</summary>
    public AssetAabb? Bounds { get; set; }
    /// <summary>アプリ固有の追加データ (glTF の extras)。</summary>
    public Dictionary<string, object>? Extras { get; set; }
}

/// <summary>1 draw call 単位。Attribute + optional Indices + Material (直接参照) + optional MorphTargets。</summary>
public sealed class AssetPrimitive
{
    /// <summary>頂点属性 (SoA)。</summary>
    public AssetVertexBuffer Attributes { get; set; } = new();
    /// <summary>index buffer (null = non-indexed 描画)。</summary>
    public uint[]? Indices { get; set; }
    /// <summary>プリミティブトポロジ (既定は Triangles)。</summary>
    public AssetTopology Topology { get; set; } = AssetTopology.Triangles;
    /// <summary>使用するマテリアルへの直接参照 (null = 既定マテリアル)。</summary>
    public AssetMaterial? Material { get; set; }
    /// <summary>morph target (blend shape) の一覧 (null = なし)。</summary>
    public List<AssetMorphTarget>? MorphTargets { get; set; }
    /// <summary>この primitive の AABB (未計算なら null)。</summary>
    public AssetAabb? Bounds { get; set; }
    /// <summary>アプリ固有の追加データ (glTF の extras)。</summary>
    public Dictionary<string, object>? Extras { get; set; }
}

/// <summary>頂点属性の SoA コンテナ。使わない属性は null。</summary>
public sealed class AssetVertexBuffer
{
    /// <summary>頂点位置 (必須。この配列長が頂点数)。</summary>
    public Vector3[] Positions { get; set; } = Array.Empty<Vector3>();
    /// <summary>頂点法線 (null = なし)。</summary>
    public Vector3[]? Normals { get; set; }
    /// <summary>xyz=tangent, w=handedness (glTF 準拠)。</summary>
    public Vector4[]? Tangents { get; set; }
    /// <summary>UV セット 0。</summary>
    public Vector2[]? TexCoord0 { get; set; }
    /// <summary>UV セット 1。</summary>
    public Vector2[]? TexCoord1 { get; set; }
    /// <summary>頂点カラー (RGBA)。</summary>
    public Vector4[]? Color0 { get; set; }
    /// <summary>skinning: 各頂点あたり 4 joint (skin.Joints への index)。</summary>
    public ushort[]? Joints0 { get; set; }
    /// <summary>skinning: <see cref="Joints0"/> に対応する 4 joint ぶんの重み。</summary>
    public Vector4[]? Weights0 { get; set; }
}

/// <summary>morph target (blend shape) 1 個。</summary>
public sealed class AssetMorphTarget
{
    /// <summary>名前 (glTF の extras.targetNames 等)。</summary>
    public string? Name { get; set; }
    /// <summary>基準頂点位置からの差分 (null = 位置変形なし)。</summary>
    public Vector3[]? DeltaPositions { get; set; }
    /// <summary>基準法線からの差分 (null = 法線変形なし)。</summary>
    public Vector3[]? DeltaNormals { get; set; }
    /// <summary>基準 tangent からの差分 (null = tangent 変形なし)。</summary>
    public Vector3[]? DeltaTangents { get; set; }
}

/// <summary>プリミティブトポロジ (glTF の mode 相当)。</summary>
public enum AssetTopology
{
    /// <summary>独立三角形リスト。</summary>
    Triangles,
    /// <summary>三角形ストリップ。</summary>
    TriangleStrip,
    /// <summary>独立線分リスト。</summary>
    Lines,
    /// <summary>連続線分 (ラインストリップ)。</summary>
    LineStrip,
    /// <summary>点群。</summary>
    Points,
}
