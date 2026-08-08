namespace Luxel.Assets;

/// <summary>1 アニメーション。複数チャンネルからなる (node × path で 1 チャンネル)。</summary>
public sealed class AssetAnimation
{
    /// <summary>名前 (glTF の name)。</summary>
    public string? Name { get; set; }
    /// <summary>アニメーション全体の長さ (秒、全チャンネルの最大時刻)。</summary>
    public float Duration { get; set; }
    /// <summary>チャンネル一覧 (node × path ごとに 1 つ)。</summary>
    public List<AssetAnimationChannel> Channels { get; } = new();
    /// <summary>アプリ固有の追加データ (glTF の extras)。</summary>
    public Dictionary<string, object>? Extras { get; set; }
}

/// <summary>1 チャンネル: 「この Node のこの Path をこの Sampler で駆動」。</summary>
public sealed class AssetAnimationChannel
{
    /// <summary>駆動対象の node (直接参照)。</summary>
    public AssetNode TargetNode { get; set; } = null!;
    /// <summary>駆動するプロパティ (Translation / Rotation / Scale / Weights)。</summary>
    public AssetAnimationPath Path { get; set; }
    /// <summary>キーフレームデータ (時刻 + 値 + 補間)。</summary>
    public AssetAnimationSampler Sampler { get; set; } = new();
}

/// <summary>時刻 + 値 + 補間。値の型は Path に依存 (Vector3 / Quaternion / float[])。</summary>
public sealed class AssetAnimationSampler
{
    /// <summary>キーフレーム時刻 (秒、昇順)。</summary>
    public float[] Times { get; set; } = Array.Empty<float>();
    /// <summary>Vector3[] (T/S) / Quaternion[] (R) / float[] (Weights) のいずれか。</summary>
    public object Values { get; set; } = Array.Empty<float>();
    /// <summary>キーフレーム間の補間方法。</summary>
    public AssetInterpolation Interpolation { get; set; } = AssetInterpolation.Linear;
}

/// <summary>アニメーションが駆動する node のプロパティ。</summary>
public enum AssetAnimationPath
{
    /// <summary>平行移動 (Vector3)。</summary>
    Translation,
    /// <summary>回転 (Quaternion)。</summary>
    Rotation,
    /// <summary>スケール (Vector3)。</summary>
    Scale,
    /// <summary>morph target 重み (float[])。</summary>
    Weights,
}

/// <summary>キーフレーム補間方法 (glTF の interpolation 相当)。</summary>
public enum AssetInterpolation
{
    /// <summary>線形補間 (回転は slerp)。</summary>
    Linear,
    /// <summary>ステップ (直前キーの値を保持)。</summary>
    Step,
    /// <summary>3 次スプライン (in/out tangent 付き)。</summary>
    CubicSpline,
}
