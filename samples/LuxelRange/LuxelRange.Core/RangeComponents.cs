using Friflo.Engine.ECS;

namespace LuxelRange.Core;

/// <summary>射的の的。命中で <see cref="Score"/> を加算。命中済みは <see cref="Hit"/> で二重計上を防ぐ。</summary>
public struct RangeTarget : IComponent
{
    /// <summary>命中時の得点。</summary>
    public int Score;
    /// <summary>命中済みか。</summary>
    public bool Hit;
    public RangeTarget(int score) { Score = score; Hit = false; }
}

/// <summary>発射された弾のマーカ (命中判定で的と区別する)。</summary>
public struct RangeBullet : IComponent
{
}
