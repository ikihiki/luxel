using System.Numerics;

namespace Luxel.Assets;

/// <summary>マテリアル記述 (シェーダ選択 + 因子/テクスチャ参照)。</summary>
public sealed class AssetMaterial
{
    /// <summary>名前 (glTF の name)。</summary>
    public string? Name { get; set; }
    /// <summary>使用するシェーディングモデル (既定は PBR Metallic-Roughness)。</summary>
    public AssetMaterialModel Model { get; set; } = AssetMaterialModel.PbrMetallicRoughness;

    /// <summary>アルファの扱い (Opaque / Mask / Blend)。</summary>
    public AssetAlphaMode AlphaMode { get; set; } = AssetAlphaMode.Opaque;
    /// <summary>Mask モード時のアルファ閾値 (これ未満のピクセルを破棄)。</summary>
    public float AlphaCutoff { get; set; } = 0.5f;
    /// <summary>両面描画 (true = backface culling 無効)。</summary>
    public bool DoubleSided { get; set; }

    // PBR Metallic-Roughness
    /// <summary>ベースカラー因子 (RGBA)。テクスチャがある場合は乗算。</summary>
    public Vector4 BaseColorFactor { get; set; } = Vector4.One;
    /// <summary>ベースカラーテクスチャ (null = 因子のみ)。</summary>
    public AssetTextureRef? BaseColorTexture { get; set; }
    /// <summary>metallic 因子 (0..1)。</summary>
    public float MetallicFactor { get; set; } = 1.0f;
    /// <summary>roughness 因子 (0..1)。</summary>
    public float RoughnessFactor { get; set; } = 1.0f;
    /// <summary>metallic-roughness テクスチャ (B=metallic, G=roughness)。</summary>
    public AssetTextureRef? MetallicRoughnessTexture { get; set; }

    // Common
    /// <summary>ノーマルマップ (スケール付き参照)。</summary>
    public AssetNormalTextureRef? NormalTexture { get; set; }
    /// <summary>オクルージョンマップ (強度付き参照)。</summary>
    public AssetOcclusionTextureRef? OcclusionTexture { get; set; }
    /// <summary>エミッシブ因子 (RGB)。</summary>
    public Vector3 EmissiveFactor { get; set; } = Vector3.Zero;
    /// <summary>エミッシブテクスチャ。</summary>
    public AssetTextureRef? EmissiveTexture { get; set; }
    /// <summary>エミッシブ強度倍率 (KHR_materials_emissive_strength 相当)。</summary>
    public float EmissiveStrength { get; set; } = 1.0f;

    /// <summary>Custom model 使用時のシェーダ識別子 (Renderer が独自解釈)。</summary>
    public string? CustomShaderId { get; set; }

    /// <summary>アプリ固有の追加データ (glTF の extras)。</summary>
    public Dictionary<string, object>? Extras { get; set; }
    /// <summary>マテリアルレベルの拡張データ (glTF の extensions)。</summary>
    public Dictionary<string, object>? Extensions { get; set; }
}

/// <summary>マテリアルのシェーディングモデル。</summary>
public enum AssetMaterialModel
{
    /// <summary>PBR metallic-roughness (glTF 標準)。</summary>
    PbrMetallicRoughness,
    /// <summary>PBR specular-glossiness (KHR_materials_pbrSpecularGlossiness 相当)。</summary>
    PbrSpecularGlossiness,
    /// <summary>ライティングなし (KHR_materials_unlit 相当)。</summary>
    Unlit,
    /// <summary>独自シェーダ (<c>CustomShaderId</c> で識別)。</summary>
    Custom,
}

/// <summary>アルファの扱い (glTF の alphaMode 相当)。</summary>
public enum AssetAlphaMode
{
    /// <summary>不透明 (アルファ無視)。</summary>
    Opaque,
    /// <summary>アルファテスト (<c>AlphaCutoff</c> 未満を破棄)。</summary>
    Mask,
    /// <summary>アルファブレンド。</summary>
    Blend,
}

/// <summary>マテリアルからテクスチャへの参照 (使う TexCoord セット + UV 変換オプション付き)。</summary>
public class AssetTextureRef
{
    /// <summary>参照先テクスチャ (直接参照)。</summary>
    public AssetTexture Texture { get; set; } = null!;
    /// <summary>使用する UV セット (0 = TexCoord0, 1 = TexCoord1)。</summary>
    public int TexCoordSet { get; set; }
    /// <summary>UV 変換 (null = 変換なし)。</summary>
    public AssetTextureTransform? Transform { get; set; }
}

/// <summary>ノーマルマップ参照 (スケール付き)。</summary>
public sealed class AssetNormalTextureRef : AssetTextureRef
{
    /// <summary>法線の XY 成分に掛けるスケール (glTF の normalTexture.scale)。</summary>
    public float Scale { get; set; } = 1.0f;
}

/// <summary>オクルージョンマップ参照 (強度付き)。</summary>
public sealed class AssetOcclusionTextureRef : AssetTextureRef
{
    /// <summary>オクルージョンの適用強度 (0..1、glTF の occlusionTexture.strength)。</summary>
    public float Strength { get; set; } = 1.0f;
}

/// <summary>UV 変換 (KHR_texture_transform 相当)。</summary>
public sealed class AssetTextureTransform
{
    /// <summary>UV オフセット。</summary>
    public Vector2 Offset { get; set; } = Vector2.Zero;
    /// <summary>UV 回転 (ラジアン)。</summary>
    public float Rotation { get; set; }
    /// <summary>UV スケール。</summary>
    public Vector2 Scale { get; set; } = Vector2.One;
    /// <summary>UV セットの上書き (null = 参照元の TexCoordSet を使用)。</summary>
    public int? OverrideTexCoordSet { get; set; }
}
