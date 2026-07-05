namespace Luxel.Animation;

/// <summary>glTF 2.0 の `interpolation` と同じ。Track ごとに 1 つ。</summary>
public enum InterpolationKind
{
    /// <summary>直前のキー値をそのまま保持 (jump)。</summary>
    Step,
    /// <summary>線形補間。Quaternion track の場合は実装側で slerp/nlerp に置き換え。</summary>
    Linear,
    /// <summary>3 次 Hermite (glTF CUBICSPLINE)。各キーフレームが (inTangent, value, outTangent) を持つ。AN-M3 では未対応 (Linear へフォールバック)。</summary>
    CubicSpline,
}
