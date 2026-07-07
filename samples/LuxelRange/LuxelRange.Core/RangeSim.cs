using System.Numerics;
using BepuPhysics;
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
    /// <summary>この高さを下回った動的体は despawn する (kill plane — 場外/貫通弾の body リーク防止)。</summary>
    public const float KillY = -20f;

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
    /// <summary>ボーナス加点 (小物をゾーンへ) の得点。</summary>
    public int BonusScore { get; private set; }
    /// <summary>配置した物理小物 (ConvexHull) の数。</summary>
    public int PropCount { get; private set; }
    /// <summary>kill plane で despawn した動的体の累計。</summary>
    public int DespawnedCount { get; private set; }
    /// <summary>1 発でも撃ったか (最初の発射まで物理停止 = 初期絵が決定的)。</summary>
    public bool Started { get; private set; }

    private readonly List<Entity> _targets = new();

    /// <summary>地形メッシュの頂点 (描画と物理コライダーで共有 — 絵と当たりが一致)。</summary>
    public Vector3[] TerrainPositions { get; private set; } = Array.Empty<Vector3>();
    /// <summary>地形メッシュの法線。</summary>
    public Vector3[] TerrainNormals { get; private set; } = Array.Empty<Vector3>();
    /// <summary>地形メッシュの三角形インデックス。</summary>
    public int[] TerrainIndices { get; private set; } = Array.Empty<int>();

    public RangeSim(int ammo = 20)
    {
        AmmoLeft = ammo;
        World = new Luxel.Ecs.World();
        Physics = new PhysicsWorld(new PhysicsSettings { FixedDt = FixedDt });
        Step = new PhysicsStepSystem(World, Physics);
        BuildArena();
    }

    /// <summary>起伏メッシュ地形 + 外周壁 + 薄板ターゲット列を配置する (ハードコード = 決定的)。</summary>
    private void BuildArena()
    {
        // 起伏メッシュ地形 (静的 Mesh コライダー、タスク 05)。描画は Gallery が同じ頂点を使う。
        (TerrainPositions, TerrainNormals, TerrainIndices) = RangeTerrain.Build();
        Physics.AddStaticMesh(RigidPose.Identity, TerrainPositions, TerrainIndices, Vector3.One);

        // 外周壁 ×4 (弾/小物の場外飛び出し防止、静的箱)。ECS entity にはしない (描画不要 = 見えない壁)。
        const float hs = RangeTerrain.HalfSize, wallH = 3f, wallT = 0.5f;
        Physics.AddStatic(new RigidPose(new Vector3(0, wallH / 2, -hs)), Physics.AddShape(new BepuPhysics.Collidables.Box(2 * hs, wallH, wallT)));
        Physics.AddStatic(new RigidPose(new Vector3(0, wallH / 2, hs)), Physics.AddShape(new BepuPhysics.Collidables.Box(2 * hs, wallH, wallT)));
        Physics.AddStatic(new RigidPose(new Vector3(-hs, wallH / 2, 0)), Physics.AddShape(new BepuPhysics.Collidables.Box(wallT, wallH, 2 * hs)));
        Physics.AddStatic(new RigidPose(new Vector3(hs, wallH / 2, 0)), Physics.AddShape(new BepuPhysics.Collidables.Box(wallT, wallH, 2 * hs)));

        // 薄板ターゲット ×5 (厚さ 0.15m、z=-8 の地形上に立てる。弾は +Z 側から撃つ)
        float[] xs = [-6f, -3f, 0f, 3f, 6f];
        const float targetZ = -8f, plateH = 1.6f;
        foreach (float x in xs)
        {
            float groundY = RangeTerrain.Height(x, targetZ);
            var size = new Vector3(1.2f, plateH, 0.15f);
            Entity e = World.Store.CreateEntity(
                new LocalTransform(Matrix4x4.CreateScale(size) * Matrix4x4.CreateTranslation(x, groundY + plateH / 2, targetZ)),
                new Color3D(new Vector4(0.90f, 0.45f, 0.30f, 1f)),
                new MeshRef(MeshRef.Cube),
                Collider.Box(size.X, size.Y, size.Z),
                new StaticBody(),
                new RangeTarget(100));
            _targets.Add(e);
        }

        // 物理小物 (動的 ConvexHull = 単位箱、撃って動かせる)。地形上に配置。描画は単位キューブ。
        Vector3[] cube =
        [
            new(-0.5f, -0.5f, -0.5f), new(0.5f, -0.5f, -0.5f), new(-0.5f, 0.5f, -0.5f), new(0.5f, 0.5f, -0.5f),
            new(-0.5f, -0.5f, 0.5f), new(0.5f, -0.5f, 0.5f), new(-0.5f, 0.5f, 0.5f), new(0.5f, 0.5f, 0.5f),
        ];
        float[] propXs = [-3f, 0f, 3f];
        foreach (float x in propXs)
        {
            float groundY = RangeTerrain.Height(x, -1f);
            World.Store.CreateEntity(
                new LocalTransform(Matrix4x4.CreateTranslation(x, groundY + 0.6f, -1f)),
                new Color3D(new Vector4(0.55f, 0.75f, 0.85f, 1f)),
                new MeshRef(MeshRef.Cube),
                HullCollider.Dynamic(cube, mass: 1f),
                new RangeProp());
            PropCount++;
        }

        // ボーナスゾーン (トリガー、z=-5 の帯)。小物をここへ吹き飛ばすと +200。
        // 物理はトリガー collidable (通過検知・力なし)。描画は地形上の薄い床マーカ (装飾、コライダー無し)。
        var zoneCenter = new Vector3(0, 1.5f, -5f);
        var zoneSize = new Vector3(11f, 3f, 2f);
        World.Store.CreateEntity(
            new LocalTransform(Matrix4x4.CreateTranslation(zoneCenter)),
            Collider.Box(zoneSize.X, zoneSize.Y, zoneSize.Z),
            new Trigger(),
            new RangeBonusZone());
        float markerY = RangeTerrain.Height(0, -5f);
        World.Store.CreateEntity(
            new LocalTransform(Matrix4x4.CreateScale(11f, 0.08f, 2f) * Matrix4x4.CreateTranslation(0, markerY + 0.1f, -5f)),
            new Color3D(new Vector4(0.95f, 0.85f, 0.35f, 0.9f)),
            new MeshRef(MeshRef.Cube));
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

    /// <summary>物理を固定 1 ステップ進め、pose を書き戻し、命中を処理し、場外落下体を despawn する。</summary>
    public void StepOnce()
    {
        if (!Started) return;
        Step.StepFixedOnce();
        ProcessHits();
        DespawnFallen();
    }

    /// <summary>KillY を下回った弾/小物を body ごと削除する (リーク防止)。収集 → 削除の 2 段。</summary>
    private void DespawnFallen()
    {
        var dead = new List<(Entity E, BepuPhysics.BodyHandle H)>();
        World.Query<RangeBullet, RigidBody, LocalTransform>().ForEachEntity(
            (ref RangeBullet _, ref RigidBody b, ref LocalTransform t, Entity e) =>
            { if (b.Attached && t.Matrix.Translation.Y < KillY) dead.Add((e, b.Handle)); });
        World.Query<RangeProp, HullCollider, LocalTransform>().ForEachEntity(
            (ref RangeProp _, ref HullCollider h, ref LocalTransform t, Entity e) =>
            { if (h.Attached && t.Matrix.Translation.Y < KillY) dead.Add((e, h.Handle)); });
        foreach ((Entity e, BepuPhysics.BodyHandle h) in dead)
        {
            Physics.RemoveBody(h);
            e.DeleteEntity();
            DespawnedCount++;
        }
    }

    /// <summary>今ステップの ContactBegin から「弾 × 未命中の的」「小物 × ボーナスゾーン」を拾ってスコア加算。</summary>
    private void ProcessHits()
    {
        foreach (ContactEvent ev in Step.ContactEvents)
        {
            if (ev.Phase != ContactPhase.Begin) continue;
            if (TryResolveHit(ev.A, ev.B) || TryResolveHit(ev.B, ev.A)) continue;
            if (TryResolveBonus(ev.A, ev.B) || TryResolveBonus(ev.B, ev.A)) { }
        }
    }

    /// <summary><paramref name="prop"/> が未加点の小物で <paramref name="zone"/> がボーナスゾーンなら +200。</summary>
    private bool TryResolveBonus(Entity prop, Entity zone)
    {
        if (!prop.HasComponent<RangeProp>() || !zone.HasComponent<RangeBonusZone>()) return false;
        RangeProp p = prop.GetComponent<RangeProp>();
        if (p.Scored) return false;
        p.Scored = true;
        prop.AddComponent(p);   // 上書き
        BonusScore += 200;
        return true;
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
