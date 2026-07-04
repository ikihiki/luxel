using System.Numerics;
using Luxel.Assets;

namespace Luxel.AssetsGpu;

/// <summary>
/// <see cref="AssetMaterial"/> の GPU ミラー。テクスチャ/サンプラは direct ref (共有可、所有しない)。
/// shader-facing 32B struct への encode は <see cref="ToShaderData"/>。
/// </summary>
public sealed class GpuMaterial
{
    public AssetMaterial? Source { get; init; }
    public string? Name { get; set; }

    public Vector4 BaseColorFactor { get; set; } = Vector4.One;
    public float MetallicFactor { get; set; } = 1.0f;
    public float RoughnessFactor { get; set; } = 1.0f;
    public Vector3 EmissiveFactor { get; set; } = Vector3.Zero;
    public float EmissiveStrength { get; set; } = 1.0f;
    public float AlphaCutoff { get; set; } = 0.5f;
    public AssetAlphaMode AlphaMode { get; set; } = AssetAlphaMode.Opaque;
    public bool DoubleSided { get; set; }

    public GpuTexture? BaseColorTexture { get; set; }
    public GpuTexture? MetallicRoughnessTexture { get; set; }
    public GpuTexture? NormalTexture { get; set; }
    public GpuTexture? OcclusionTexture { get; set; }
    public GpuTexture? EmissiveTexture { get; set; }
    public GpuSampler? Sampler { get; set; }

    /// <summary>shader 用 32B (<see cref="Luxel.Assets.MaterialGpuData"/>) を組み立てる。</summary>
    public MaterialGpuData ToShaderData()
    {
        uint texIdx = 0, sampIdx = 0, flags = 0;
        if (BaseColorTexture is not null)
        {
            texIdx = BaseColorTexture.BindlessIndex;
            sampIdx = Sampler?.BindlessIndex ?? 0;
            flags = MaterialGpuData.FlagHasTexture;
        }
        return new MaterialGpuData
        {
            BaseColor = BaseColorFactor,
            BaseColorTexIndex = texIdx,
            SamplerIndex = sampIdx,
            Flags = flags,
        };
    }
}
