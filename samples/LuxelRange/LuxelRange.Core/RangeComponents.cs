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

/// <summary>動的な物理小物 (ConvexHull の箱)。撃って動かせる。場外落下で despawn。
/// ボーナスゾーン通過で加点 (<see cref="Scored"/> で一度だけ)。</summary>
public struct RangeProp : IComponent
{
    /// <summary>ボーナス加点済みか。</summary>
    public bool Scored;
}

/// <summary>ボーナスゾーン (トリガー) のマーカ。小物を通過させると加点。</summary>
public struct RangeBonusZone : IComponent
{
}
