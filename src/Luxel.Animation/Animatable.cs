namespace Luxel.Animation;

/// <summary>時間 (秒) → 値 T の純粋関数。stateless。</summary>
public interface IAnimatable<T>
{
    /// <summary>アニメ全体の長さ (秒)。Loop 等の判定に使う。</summary>
    float Duration { get; }

    /// <summary>時刻 timeSec での値を返す。範囲外 (t&lt;0 や t&gt;Duration) でも適切にクランプ。</summary>
    T Evaluate(float timeSec);
}

/// <summary>
/// 基本の Animatable: Curve (時間→progress) + Tween (progress→値) を合成。
/// Flutter Animatable と同じ「2 段分解」モデル。
/// </summary>
public sealed class Animatable<T> : IAnimatable<T>
{
    public ICurve Curve { get; init; } = LinearCurve.Instance;
    public required ITween<T> Tween { get; init; }
    public float Duration { get; init; } = 1f;

    public T Evaluate(float timeSec)
    {
        if (Duration <= 0f) return Tween.Lerp(1f);
        float t01 = Math.Clamp(timeSec / Duration, 0f, 1f);
        return Tween.Lerp(Curve.Eval(t01));
    }
}
