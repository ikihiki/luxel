using System.Numerics;
using Luxel.Assets;
using Luxel.Resources;

namespace Luxel.AssetsGpu;

/// <summary>
/// <see cref="AssetSkin"/> の GPU ミラー。joint 行列は毎フレーム CPU から書き換わるため <see cref="RenderBuffer{T}"/>。
/// SkinningSystem が joint node の GlobalTransform × InverseBindMatrix を書いて MarkDirty + FlushImmediate。
/// </summary>
public sealed class GpuSkin : IDisposable
{
    public AssetSkin? Source { get; init; }
    public string? Name { get; set; }
    /// <summary>joint 数 (Source.Joints.Count と同じ、cache 用)。</summary>
    public int JointCount { get; init; }
    /// <summary>joint 行列配列 (per-frame update)。</summary>
    public RenderBuffer<Matrix4x4> JointMatrices { get; init; } = null!;

    public void Dispose()
    {
        JointMatrices?.Dispose();
    }
}
