using System.Numerics;

namespace Luxel.Assets;

/// <summary>スキニング設定。Joint node の直接参照リスト + Inverse Bind Matrix。</summary>
public sealed class AssetSkin
{
    public string? Name { get; set; }
    /// <summary>各 joint に対応する node の直接参照。頂点の Joints0 はこのリストへの index。</summary>
    public List<AssetNode> Joints { get; } = new();
    public Matrix4x4[] InverseBindMatrices { get; set; } = Array.Empty<Matrix4x4>();
    /// <summary>optional: skeleton の root (glTF 準拠)。</summary>
    public AssetNode? SkeletonRoot { get; set; }
    public Dictionary<string, object>? Extras { get; set; }
}
