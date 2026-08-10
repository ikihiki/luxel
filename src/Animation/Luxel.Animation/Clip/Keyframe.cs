namespace Luxel.Animation;

/// <summary>時刻と値の組。Track の Keyframes 配列の要素。時刻昇順で保持される。</summary>
public readonly record struct Keyframe<T>(float Time, T Value);
