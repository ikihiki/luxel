using System.Numerics;
using BepuPhysics.Constraints;

namespace Luxel.Physics;

/// <summary>
/// <see cref="PhysicsWorld"/> の生成時設定。既定値は「1 ユニット = 1m、60Hz 固定ステップ、単スレッド」。
/// 単スレッド (ThreadCount = 0) は**決定的** — snap 回帰 (golden) と同じ思想で、
/// 同じ初期状態 + 同じステップ列は常に同じ結果になる。ThreadCount > 0 は速いが決定性を失う (opt-in)。
/// </summary>
public sealed class PhysicsSettings
{
    /// <summary>重力加速度 (m/s²)。既定は地球 (0, -9.81, 0)。</summary>
    public Vector3 Gravity = new(0, -9.81f, 0);

    /// <summary>固定タイムステップ (秒)。<see cref="PhysicsWorld.Step"/> の accumulator がこの刻みで進める。</summary>
    public float FixedDt = 1f / 60f;

    /// <summary>速度の減衰 (0..1/秒相当)。空気抵抗の近似。</summary>
    public float LinearDamping = 0.03f;
    /// <summary>角速度の減衰。</summary>
    public float AngularDamping = 0.03f;

    /// <summary>接触の摩擦係数 (全接触共通 — v1 はマテリアル per-body を持たない)。</summary>
    public float Friction = 1f;

    /// <summary>接触の反発上限 (m/s)。Bepu v2 に古典的 restitution は無く、
    /// めり込み回復速度の上限 + <see cref="ContactSpring"/> で「跳ね」を表現する。</summary>
    public float MaximumRecoveryVelocity = 2f;

    /// <summary>接触ばね (周波数 Hz, ダンピング比)。硬い接触の既定は (30, 1)。</summary>
    public SpringSettings ContactSpring = new(30, 1);

    /// <summary>ソルバのワーカースレッド数。0 = 単スレッド (決定的、既定)。</summary>
    public int ThreadCount;

    /// <summary>ソルバの速度反復数。</summary>
    public int SolverVelocityIterations = 8;
    /// <summary>ソルバのサブステップ数。</summary>
    public int SolverSubsteps = 1;
}

/// <summary>レイキャストのヒット結果。</summary>
public readonly record struct PhysicsRayHit(float T, Vector3 Normal, bool IsStatic, int HandleValue);
