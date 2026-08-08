using System.Numerics;

namespace Luxel.Assets;

/// <summary>
/// Scene graph の node。TRS + 直接参照 child list + 各種 attachment (Mesh / Skin / Camera / Light) を持つ。
/// Parent は保持しない (Children が authoritative、traversal で導出。cycle 回避)。
/// </summary>
public sealed class AssetNode
{
    /// <summary>名前 (glTF の name)。</summary>
    public string? Name { get; set; }

    // TRS (通常はこちら)
    /// <summary>ローカル平行移動。</summary>
    public Vector3 Translation { get; set; } = Vector3.Zero;
    /// <summary>ローカル回転 (quaternion)。</summary>
    public Quaternion Rotation { get; set; } = Quaternion.Identity;
    /// <summary>ローカルスケール。</summary>
    public Vector3 Scale { get; set; } = Vector3.One;

    /// <summary>指定されている場合は TRS を無視して LocalMatrix に直接使う。</summary>
    public Matrix4x4? OverrideMatrix { get; set; }

    /// <summary>子 node の authoritative list (直接参照)。</summary>
    public List<AssetNode> Children { get; } = new();

    // Attachments (どれも null 可)
    /// <summary>この node に attach された mesh。</summary>
    public AssetMesh? Mesh { get; set; }
    /// <summary>この node の mesh に適用するスキン。</summary>
    public AssetSkin? Skin { get; set; }
    /// <summary>この node に attach されたカメラ。</summary>
    public AssetCamera? Camera { get; set; }
    /// <summary>この node に attach された光源。</summary>
    public AssetLight? Light { get; set; }
    /// <summary>この node が持つ mesh の morph target 重み (Mesh.Primitives[*].MorphTargets の数と一致)。</summary>
    public float[]? Weights { get; set; }

    /// <summary>アプリ固有の追加データ (glTF の extras)。</summary>
    public Dictionary<string, object>? Extras { get; set; }
    /// <summary>node レベルの拡張データ (glTF の extensions)。</summary>
    public Dictionary<string, object>? Extensions { get; set; }

    /// <summary>ローカル変換行列 (S→R→T 合成、<see cref="OverrideMatrix"/> があればそちらを優先)。</summary>
    public Matrix4x4 LocalMatrix =>
        OverrideMatrix ??
        (Matrix4x4.CreateScale(Scale)
       * Matrix4x4.CreateFromQuaternion(Rotation)
       * Matrix4x4.CreateTranslation(Translation));
}
