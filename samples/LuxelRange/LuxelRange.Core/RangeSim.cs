using System.Numerics;
using Friflo.Engine.ECS;
using Luxel.Ecs;
using Luxel.Physics;

namespace LuxelRange.Core;

/// <summary>
/// 「Luxel Range」(3D 射的) の純ロジックシミュレーション (GPU/実窓に非依存)。
/// アリーナ (静的床) + 薄板ターゲット (静的箱) を並べ、<see cref="Fire"/> で CCD 弾を撃つ。命中は
/// <see cref="PhysicsStepSystem.ContactEvents"/> (弾 × 的の ContactBegin) を購読してスコア加算。
///
/// <para><b>決定性</b>: Bepu 単スレッド + 固定 1/120 ステップ。同じ発射列は常に同じスコアになる (数値 assert 可)。</para>
///
/// <para>本スライス (縦切り 1) は 03 (CCD) + 04 (接触イベント) の統合検証。メッシュアリーナ (05) / 動く的 (09) /
/// パーティクル・音・UI・Title/Result は後続スライス。</para>
/// </summary>
public sealed class RangeSim : IDisposable
{
    /// <summary>固定タイムステップ (1/120s — 高速弾の解像度)。</summary>
    public const float FixedDt = 1f / 120f;

    /// <summary>弾の半径。</summary>
    public const float BulletRadius = 0.1f;
    /// <summary>弾の初速 (m/s)。</summary>
    public const float BulletSpeed = 100f;

    public Luxel.Ecs.World World { get; }
    public PhysicsWorld Physics { get; }
    public PhysicsStepSystem Step { get; }

    /// <summary>現在のスコア。</summary>
    public int Score { get; private set; }
    /// <summary>残弾。</summary>
    public int AmmoLeft { get; private set; }
    /// <summary>命中した的の数。</summary>
    public int TargetsHit { get; private set; }
    /// <summary>的の総数。</summary>
    public int TargetCount => _targets.Count;
    /// <summary>1 発でも撃ったか (最初の発射まで物理停止 = 初期絵が決定的)。</summary>
    public bool Started { get; private set; }

    private readonly List<Entity> _targets = new();

    public RangeSim(int ammo = 20)
    {
        AmmoLeft = ammo;
        World = new Luxel.Ecs.World();
        Physics = new PhysicsWorld(new PhysicsSettings { FixedDt = FixedDt });
        Step = new PhysicsStepSystem(World, Physics);
        BuildArena();
    }

    /// <summary>床 + 薄板ターゲット列を配置する (ハードコード = 決定的)。</summary>
    private void BuildArena()
    {
        // 床 (静的、上面 y=0)
        World.Store.CreateEntity(
            new LocalTransform(Matrix4x4.CreateScale(20f, 1f, 20f) * Matrix4x4.CreateTranslation(0, -0.5f, 0)),
            new Color3D(new Vector4(0.32f, 0.34f, 0.40f, 1f)),
            new MeshRef(MeshRef.Cube),
            Collider.Box(20f, 1f, 20f),
            new StaticBody());

        // 薄板ターゲット ×5 (厚さ 0.15m、z=-3 に立てる。弾は +Z 側から撃つ)
        float[] xs = [-4f, -2f, 0f, 2f, 4f];
        foreach (float x in xs)
        {
            var size = new Vector3(1.2f, 1.6f, 0.15f);
            Entity e = World.Store.CreateEntity(
                new LocalTransform(Matrix4x4.CreateScale(size) * Matrix4x4.CreateTranslation(x, 1.3f, -3f)),
                new Color3D(new Vector4(0.90f, 0.45f, 0.30f, 1f)),
                new MeshRef(MeshRef.Cube),
                Collider.Box(size.X, size.Y, size.Z),
                new StaticBody(),
                new RangeTarget(100));
            _targets.Add(e);
        }
    }

    /// <summary>原点から方向へ CCD 弾を発射する (残弾があれば)。命中判定は次の <see cref="StepOnce"/> 群で。</summary>
    public bool Fire(Vector3 origin, Vector3 direction)
    {
        if (AmmoLeft <= 0) return false;
        AmmoLeft--;
        Started = true;
        Vector3 dir = Vector3.Normalize(direction);
        World.Store.CreateEntity(
            new LocalTransform(Matrix4x4.CreateScale(BulletRadius * 2) * Matrix4x4.CreateTranslation(origin)),
            new Color3D(new Vector4(0.96f, 0.97f, 1f, 1f)),
            new MeshRef(MeshRef.Cube),
            Collider.Sphere(BulletRadius),
            RigidBody.Dynamic(mass: 0.2f, initialVelocity: dir * BulletSpeed, ccd: true),
            new RangeBullet());
        return true;
    }

    /// <summary>物理を固定 1 ステップ進め、pose を書き戻し、命中を処理する。最初の発射まで物理は止まる。</summary>
    public void StepOnce()
    {
        if (!Started) return;
        Step.StepFixedOnce();
        ProcessHits();
    }

    /// <summary>今ステップの ContactBegin から「弾 × 未命中の的」を拾ってスコア加算。</summary>
    private void ProcessHits()
    {
        foreach (ContactEvent ev in Step.ContactEvents)
        {
            if (ev.Phase != ContactPhase.Begin) continue;
            if (TryResolveHit(ev.A, ev.B) || TryResolveHit(ev.B, ev.A)) { }
        }
    }

    /// <summary><paramref name="bullet"/> が弾で <paramref name="target"/> が未命中の的なら加点して true。</summary>
    private bool TryResolveHit(Entity bullet, Entity target)
    {
        if (!bullet.HasComponent<RangeBullet>() || !target.HasComponent<RangeTarget>()) return false;
        RangeTarget t = target.GetComponent<RangeTarget>();
        if (t.Hit) return false;
        t.Hit = true;
        target.AddComponent(t);   // 上書き
        Score += t.Score;
        TargetsHit++;
        return true;
    }

    public void Dispose() => Physics.Dispose();
}
