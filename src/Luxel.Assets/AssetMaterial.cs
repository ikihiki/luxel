using System.Numerics;

namespace Luxel.Assets;

/// <summary>マテリアル記述 (シェーダ選択 + 因子/テクスチャ参照)。</summary>
public sealed class AssetMaterial
{
    public string? Name { get; set; }
    public AssetMaterialModel Model { get; set; } = AssetMaterialModel.PbrMetallicRoughness;

    public AssetAlphaMode AlphaMode { get; set; } = AssetAlphaMode.Opaque;
    public float AlphaCutoff { get; set; } = 0.5f;
    public bool DoubleSided { get; set; }

    // PBR Metallic-Roughness
    public Vector4 BaseColorFactor { get; set; } = Vector4.One;
    public AssetTextureRef? BaseColorTexture { get; set; }
    public float MetallicFactor { get; set; } = 1.0f;
    public float RoughnessFactor { get; set; } = 1.0f;
    public AssetTextureRef? MetallicRoughnessTexture { get; set; }

    // Common
    public AssetNormalTextureRef? NormalTexture { get; set; }
    public AssetOcclusionTextureRef? OcclusionTexture { get; set; }
    public Vector3 EmissiveFactor { get; set; } = Vector3.Zero;
    public AssetTextureRef? EmissiveTexture { get; set; }
    public float EmissiveStrength { get; set; } = 1.0f;

    /// <summary>Custom model 使用時のシェーダ識別子 (Renderer が独自解釈)。</summary>
    public string? CustomShaderId { get; set; }

    public Dictionary<string, object>? Extras { get; set; }
    public Dictionary<string, object>? Extensions { get; set; }
}

public enum AssetMaterialModel { PbrMetallicRoughness, PbrSpecularGlossiness, Unlit, Custom }
public enum AssetAlphaMode { Opaque, Mask, Blend }

/// <summary>マテリアルからテクスチャへの参照 (使う TexCoord セット + UV 変換オプション付き)。</summary>
public class AssetTextureRef
{
    public AssetTexture Texture { get; set; } = null!;
    public int TexCoordSet { get; set; }
    public AssetTextureTransform? Transform { get; set; }
}

public sealed class AssetNormalTextureRef : AssetTextureRef
{
    public float Scale { get; set; } = 1.0f;
}

public sealed class AssetOcclusionTextureRef : AssetTextureRef
{
    public float Strength { get; set; } = 1.0f;
}

/// <summary>UV 変換 (KHR_texture_transform 相当)。</summary>
public sealed class AssetTextureTransform
{
    public Vector2 Offset { get; set; } = Vector2.Zero;
    public float Rotation { get; set; }
    public Vector2 Scale { get; set; } = Vector2.One;
    public int? OverrideTexCoordSet { get; set; }
}
