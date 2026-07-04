using System.Numerics;

namespace Luxel.Assets;

/// <summary>頂点データ集合。複数 primitive で構成 (multi-material など)。</summary>
public sealed class AssetMesh
{
    public string? Name { get; set; }
    public List<AssetPrimitive> Primitives { get; } = new();
    public AssetAabb? Bounds { get; set; }
    public Dictionary<string, object>? Extras { get; set; }
}

/// <summary>1 draw call 単位。Attribute + optional Indices + Material (直接参照) + optional MorphTargets。</summary>
public sealed class AssetPrimitive
{
    public AssetVertexBuffer Attributes { get; set; } = new();
    public uint[]? Indices { get; set; }
    public AssetTopology Topology { get; set; } = AssetTopology.Triangles;
    public AssetMaterial? Material { get; set; }
    public List<AssetMorphTarget>? MorphTargets { get; set; }
    public AssetAabb? Bounds { get; set; }
    public Dictionary<string, object>? Extras { get; set; }
}

/// <summary>頂点属性の SoA コンテナ。使わない属性は null。</summary>
public sealed class AssetVertexBuffer
{
    public Vector3[] Positions { get; set; } = Array.Empty<Vector3>();
    public Vector3[]? Normals { get; set; }
    /// <summary>xyz=tangent, w=handedness (glTF 準拠)。</summary>
    public Vector4[]? Tangents { get; set; }
    public Vector2[]? TexCoord0 { get; set; }
    public Vector2[]? TexCoord1 { get; set; }
    public Vector4[]? Color0 { get; set; }
    /// <summary>skinning: 各頂点あたり 4 joint (skin.Joints への index)。</summary>
    public ushort[]? Joints0 { get; set; }
    public Vector4[]? Weights0 { get; set; }
}

/// <summary>morph target (blend shape) 1 個。</summary>
public sealed class AssetMorphTarget
{
    public string? Name { get; set; }
    public Vector3[]? DeltaPositions { get; set; }
    public Vector3[]? DeltaNormals { get; set; }
    public Vector3[]? DeltaTangents { get; set; }
}

public enum AssetTopology { Triangles, TriangleStrip, Lines, LineStrip, Points }
