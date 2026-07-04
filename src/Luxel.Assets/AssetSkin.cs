using System.Numerics;

namespace Luxel.Assets;

/// <summary>スキニング設定。Joint node の直接参照リスト + Inverse Bind Matrix。</summary>
public sealed class AssetSkin
{
    /// <summary>名前 (glTF の name)。</summary>
    public string? Name { get; set; }
    /// <summary>各 joint に対応する node の直接参照。頂点の Joints0 はこのリストへの index。</summary>
    public List<AssetNode> Joints { get; } = new();
    /// <summary>各 joint の inverse bind matrix (<see cref="Joints"/> と同順・同数)。</summary>
    public Matrix4x4[] InverseBindMatrices { get; set; } = Array.Empty<Matrix4x4>();
    /// <summary>optional: skeleton の root (glTF 準拠)。</summary>
    public AssetNode? SkeletonRoot { get; set; }
    /// <summary>アプリ固有の追加データ (glTF の extras)。</summary>
    public Dictionary<string, object>? Extras { get; set; }
}
