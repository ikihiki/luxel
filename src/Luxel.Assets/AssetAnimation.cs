namespace Luxel.Assets;

/// <summary>1 アニメーション。複数チャンネルからなる (node × path で 1 チャンネル)。</summary>
public sealed class AssetAnimation
{
    public string? Name { get; set; }
    public float Duration { get; set; }
    public List<AssetAnimationChannel> Channels { get; } = new();
    public Dictionary<string, object>? Extras { get; set; }
}

/// <summary>1 チャンネル: 「この Node のこの Path をこの Sampler で駆動」。</summary>
public sealed class AssetAnimationChannel
{
    public AssetNode TargetNode { get; set; } = null!;
    public AssetAnimationPath Path { get; set; }
    public AssetAnimationSampler Sampler { get; set; } = new();
}

/// <summary>時刻 + 値 + 補間。値の型は Path に依存 (Vector3 / Quaternion / float[])。</summary>
public sealed class AssetAnimationSampler
{
    public float[] Times { get; set; } = Array.Empty<float>();
    /// <summary>Vector3[] (T/S) / Quaternion[] (R) / float[] (Weights) のいずれか。</summary>
    public object Values { get; set; } = Array.Empty<float>();
    public AssetInterpolation Interpolation { get; set; } = AssetInterpolation.Linear;
}

public enum AssetAnimationPath { Translation, Rotation, Scale, Weights }
public enum AssetInterpolation { Linear, Step, CubicSpline }
