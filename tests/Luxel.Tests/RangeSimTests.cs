using System.Numerics;
using Friflo.Engine.ECS;
using LuxelRange.Core;
using Xunit;

namespace Luxel.Tests;

/// <summary>「Luxel Range」コアシミュレーション (GPU 不要・決定的): CCD 弾 → 薄板ターゲット命中 → スコア。</summary>
public class RangeSimTests
{
    [Fact]
    public void Fire_HitsThinTarget_ScoresOnce()
    {
        using var sim = new RangeSim(ammo: 20);
        Assert.Equal(5, sim.TargetCount);

        // 中央の的 (x=0, z=-3) へ +Z 側から撃つ
        Assert.True(sim.Fire(new Vector3(0, 1.3f, 5f), new Vector3(0, 0, -1)));
        Assert.Equal(19, sim.AmmoLeft);

        for (int i = 0; i < 40; i++) sim.StepOnce();   // 8m / 100m/s ≈ 0.08s = ~10 step、余裕をみて 40

        Assert.Equal(100, sim.Score);
        Assert.Equal(1, sim.TargetsHit);

        // さらにステップしても二重計上しない (Hit フラグ)
        int before = sim.Score;
        for (int i = 0; i < 40; i++) sim.StepOnce();
        Assert.Equal(before, sim.Score);
    }

    [Fact]
    public void Fire_Miss_NoScore()
    {
        using var sim = new RangeSim(ammo: 20);
        // 的の無い上方向へ撃つ → 命中なし
        sim.Fire(new Vector3(0, 1.3f, 5f), new Vector3(0, 1, 0));
        for (int i = 0; i < 60; i++) sim.StepOnce();
        Assert.Equal(0, sim.Score);
        Assert.Equal(0, sim.TargetsHit);
    }

    [Fact]
    public void Ammo_DepletesAndBlocksFire()
    {
        using var sim = new RangeSim(ammo: 3);
        Assert.True(sim.Fire(new Vector3(0, 1.3f, 5f), -Vector3.UnitZ));
        Assert.True(sim.Fire(new Vector3(1, 1.3f, 5f), -Vector3.UnitZ));
        Assert.True(sim.Fire(new Vector3(-1, 1.3f, 5f), -Vector3.UnitZ));
        Assert.False(sim.Fire(new Vector3(2, 1.3f, 5f), -Vector3.UnitZ));   // 残弾 0
        Assert.Equal(0, sim.AmmoLeft);
    }

    /// <summary>地形メッシュの winding が上面衝突向き — 上から落ちた (通常速度の) 物体が地形上で静定する。
    /// 描画と同じ頂点を物理コライダーに使うので、これが通れば「絵 = 当たり」の整合が担保される (タスク 05)。</summary>
    [Fact]
    public void Terrain_WindingUpward_ObjectRestsOnSurface()
    {
        (Vector3[] pos, _, int[] idx) = RangeTerrain.Build();
        Assert.Equal((RangeTerrain.N + 1) * (RangeTerrain.N + 1), pos.Length);
        Assert.Equal(RangeTerrain.N * RangeTerrain.N * 6, idx.Length);

        using var physics = new Luxel.Physics.PhysicsWorld();
        physics.AddStaticMesh(BepuPhysics.RigidPose.Identity, pos, idx, Vector3.One);
        BepuPhysics.BodyHandle ball = physics.AddDynamic(
            new BepuPhysics.RigidPose(new Vector3(2, 5, 2)), new BepuPhysics.Collidables.Sphere(0.3f));
        for (int i = 0; i < 300; i++) physics.StepOnce();

        float y = physics.GetPose(ball).Position.Y;
        float ground = RangeTerrain.Height(2, 2);
        Assert.True(y > ground - 0.2f, $"球 y={y} が地形 {ground} を貫通した (winding が下向き?)");
        Assert.True(y < ground + 1.2f, $"球 y={y} が地形上で静定していない");
    }

    /// <summary>kill plane: 地形をすり抜けて (または場外へ) 落ちた弾は body ごと despawn される (リーク防止)。
    /// スライス 2 の「高速弾は Mesh CCD を貫通しうる」制約の回収経路。</summary>
    [Fact]
    public void KillPlane_DespawnsFallenBullet()
    {
        using var sim = new RangeSim();
        sim.Fire(new Vector3(5, 8, 5), new Vector3(0, -1, 0));   // 真下 → 地形貫通で落下
        Assert.Equal(1, CountBullets(sim));

        for (int i = 0; i < 400; i++) sim.StepOnce();

        Assert.Equal(0, CountBullets(sim));       // KillY を下回って despawn
        Assert.True(sim.DespawnedCount >= 1);
    }

    [Fact]
    public void Props_RestOnTerrain_NotDespawned()
    {
        using var sim = new RangeSim();
        Assert.Equal(3, sim.PropCount);
        sim.Fire(new Vector3(0, 1.3f, 5f), new Vector3(0, 1, 0));   // 空撃ちで物理開始 (的には当てない)
        for (int i = 0; i < 240; i++) sim.StepOnce();
        Assert.Equal(3, CountProps(sim));   // 小物は地形上に留まる (落下 despawn しない)
        Assert.Equal(0, sim.DespawnedCount);
    }

    private static int CountBullets(RangeSim sim)
    {
        int n = 0;
        sim.World.Query<RangeBullet>().ForEachEntity((ref RangeBullet _, Entity _) => n++);
        return n;
    }

    private static int CountProps(RangeSim sim)
    {
        int n = 0;
        sim.World.Query<RangeProp>().ForEachEntity((ref RangeProp _, Entity _) => n++);
        return n;
    }

    [Fact]
    public void NotStarted_PhysicsFrozen()
    {
        using var sim = new RangeSim();
        Assert.False(sim.Started);
        for (int i = 0; i < 10; i++) sim.StepOnce();   // 発射前は物理停止 (初期絵が決定的)
        Assert.Equal(0, sim.Score);
    }
}
