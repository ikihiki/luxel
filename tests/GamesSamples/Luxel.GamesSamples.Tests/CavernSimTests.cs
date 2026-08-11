using System.Linq;
using System.Numerics;
using LuxelCavern.Core;
using Luxel.Graphics.TwoD;

namespace Luxel.Tests;

/// <summary>
/// capstone ① 「Luxel Cavern」のプレイヤー物理 (<see cref="CavernSim"/>) の決定的テスト:
/// 落下して地面に着地 / 右移動 / 接地時のみジャンプ (二段ジャンプしない) / 壁で停止。
/// </summary>
public class CavernSimTests
{
    private const float Dt = 1f / 60;
    private static readonly float FloorTopY = (CavernLevel.Height - 5) * CavernLevel.Tile;

    private static CavernSim MakeSim(Vector2? spawn = null)
    {
        SpriteAtlas atlas = CavernLevel.BuildAtlas();
        TileSet ts = CavernLevel.BuildTileSet(atlas);
        TileMap map = CavernTestLevel.BuildMap(ts);
        return new CavernSim(map, spawn ?? CavernLevel.Spawn, new Vector2(12, 22));
    }

    [Fact]
    public void FallsAndLandsOnGround()
    {
        var sim = MakeSim();
        for (int i = 0; i < 120; i++) sim.Step(Dt, 0f, false);
        Assert.True(sim.OnGround);
        Assert.Equal(FloorTopY, sim.PlayerPos.Y + sim.PlayerSize.Y, 1);   // 下端 = 地面上端
        Assert.Equal(0f, sim.PlayerVel.Y, 1);
    }

    [Fact]
    public void MovesRight()
    {
        var sim = MakeSim();
        for (int i = 0; i < 30; i++) sim.Step(Dt, 0f, false);   // 着地
        float x0 = sim.PlayerPos.X;
        for (int i = 0; i < 30; i++) sim.Step(Dt, 1f, false);
        Assert.True(sim.PlayerPos.X > x0);
        Assert.True(sim.FacingRight);
    }

    [Fact]
    public void JumpOnlyWhenGrounded_NoDoubleJump()
    {
        var sim = MakeSim();
        for (int i = 0; i < 60; i++) sim.Step(Dt, 0f, false);
        Assert.True(sim.OnGround);

        sim.Step(Dt, 0f, jumpPressed: true);   // 接地 → ジャンプ発動
        Assert.False(sim.OnGround);
        Assert.True(sim.PlayerVel.Y < 0f);     // 上昇中
        float vyAfterJump = sim.PlayerVel.Y;

        sim.Step(Dt, 0f, jumpPressed: true);   // 空中 → 二段ジャンプしない (リセットされず重力で減速)
        Assert.True(sim.PlayerVel.Y > vyAfterJump);
    }

    [Fact]
    public void StopsAtWall()
    {
        // 壁柱は x=24 (world 384)。壁の左の地面にスポーンして右へ突進 → 壁で止まる
        var sim = MakeSim(new Vector2(20 * 16, FloorTopY - 22 - 4));
        for (int i = 0; i < 10; i++) sim.Step(Dt, 0f, false);     // 着地
        for (int i = 0; i < 120; i++) sim.Step(Dt, 1f, false);    // 右へ突進
        Assert.True(sim.PlayerPos.X + sim.PlayerSize.X <= 384f + 0.5f);   // 壁の左端で停止
    }

    [Fact]
    public void Deterministic_SameInputSameResult()
    {
        var a = MakeSim();
        var b = MakeSim();
        for (int i = 0; i < 90; i++)
        {
            bool jump = i == 40;
            a.Step(Dt, 1f, jump);
            b.Step(Dt, 1f, jump);
        }
        Assert.Equal(a.PlayerPos.X, b.PlayerPos.X, 4);
        Assert.Equal(a.PlayerPos.Y, b.PlayerPos.Y, 4);
    }

    // ---- 収集物 / 扉 ----

    [Fact]
    public void CollectsCoin()
    {
        var sim = MakeSim();
        for (int i = 0; i < 30; i++) sim.Step(Dt, 0f, false);   // 着地
        sim.Pickups.Add(new Pickup { Pos = sim.PlayerPos, Size = 10 });   // 重なる位置
        sim.Step(Dt, 0f, false);
        Assert.Equal(1, sim.Coins);
        Assert.True(sim.Pickups[0].Collected);
    }

    [Fact]
    public void ThreeKeysOpenDoor()
    {
        var sim = MakeSim();
        for (int i = 0; i < 3; i++) sim.Pickups.Add(new Pickup { Pos = sim.PlayerPos, Size = 12, IsKey = true });
        sim.Step(Dt, 0f, false);
        Assert.Equal(3, sim.Keys);
        Assert.True(sim.DoorOpen);
    }

    [Fact]
    public void ReachingOpenDoor_Clears()
    {
        var sim = MakeSim();
        for (int i = 0; i < 3; i++) sim.Pickups.Add(new Pickup { Pos = sim.PlayerPos, Size = 12, IsKey = true });
        sim.DoorPos = sim.PlayerPos;
        sim.Step(Dt, 0f, false);
        Assert.Equal(CavernResult.Cleared, sim.Result);
    }

    // ---- ハザード / HP ----

    [Fact]
    public void SpikeTile_Damages_AndRequestsShake()
    {
        var sim = MakeSim();
        sim.PlayerPos = new Vector2(31 * 16 + 2, 300);   // トゲタイル (x=31, floor 行) に重なる
        sim.PlayerVel = Vector2.Zero;
        sim.Step(Dt, 0f, false);
        Assert.Equal(2, sim.Hp);
        Assert.True(sim.Invincible);
        Assert.True(sim.ShakeRequested);
    }

    [Fact]
    public void Invincibility_PreventsRepeatDamage()
    {
        var sim = MakeSim();
        sim.PlayerPos = new Vector2(31 * 16 + 2, 300);
        sim.Step(Dt, 0f, false);
        int hp1 = sim.Hp;
        sim.PlayerPos = new Vector2(31 * 16 + 2, 300);   // 再び重ねても
        sim.Step(Dt, 0f, false);
        Assert.Equal(hp1, sim.Hp);   // 無敵中は追加ダメージなし
    }

    [Fact]
    public void EnemyContact_Damages_EnemySurvives()
    {
        var sim = MakeSim();
        for (int i = 0; i < 20; i++) sim.Step(Dt, 0f, false);   // 着地 (落下していない状態に)
        var e = new Walker { Pos = sim.PlayerPos, MinX = 0, MaxX = 1000 };
        sim.Enemies.Add(e);
        int hp0 = sim.Hp;
        sim.Step(Dt, 0f, false);
        Assert.Equal(hp0 - 1, sim.Hp);
        Assert.True(e.Alive);   // 横接触は踏みつけでない
    }

    [Fact]
    public void Stomp_DefeatsEnemy_NoDamage_Bounces()
    {
        var sim = MakeSim();
        var e = new Walker { Pos = new Vector2(200, 290), MinX = 0, MaxX = 1000 };
        sim.Enemies.Add(e);
        sim.PlayerPos = new Vector2(200, 272);   // 敵の真上
        sim.PlayerVel = new Vector2(0, 120);     // 落下中
        sim.Step(Dt, 0f, false);
        Assert.False(e.Alive);
        Assert.True(sim.PlayerVel.Y < 0f);   // 踏んで跳ねる
        Assert.Equal(3, sim.Hp);             // ダメージなし
    }

    [Fact]
    public void FallOffMap_Dies()
    {
        var sim = MakeSim();
        sim.PlayerPos = new Vector2(100, sim.KillY + 10);
        sim.Step(Dt, 0f, false);
        Assert.Equal(CavernResult.Dead, sim.Result);
        Assert.Equal(0, sim.Hp);
    }

    [Fact]
    public void CreateSim_PopulatesEntities()
    {
        CavernSim sim = CavernTestLevel.CreateSim();
        Assert.Equal(3, sim.Pickups.Count(p => p.IsKey));
        Assert.Contains(sim.Pickups, p => !p.IsKey);   // コインもある
        Assert.NotEmpty(sim.Enemies);
        Assert.NotEmpty(sim.Flyers);
        Assert.False(sim.DoorOpen);
        Assert.Equal(3, sim.Hp);
    }

    // ---- 飛行敵 + 演出イベント ----

    [Fact]
    public void Flyer_Oscillates()
    {
        var f = new Flyer { Home = new Vector2(200, 200), AmpX = 30, AmpY = 20, Freq = 1f };
        float y0 = f.Pos.Y;
        f.Time = 0.5f;
        Assert.True(MathF.Abs(f.Pos.Y - y0) > 10f);   // サイン波で浮遊
    }

    [Fact]
    public void FlyerContact_Damages()
    {
        var sim = MakeSim();
        for (int i = 0; i < 20; i++) sim.Step(Dt, 0f, false);   // 着地
        var fl = new Flyer { Home = sim.PlayerCenter, AmpX = 0, AmpY = 0 };   // プレイヤーに重ねる
        sim.Flyers.Add(fl);
        int hp0 = sim.Hp;
        sim.Step(Dt, 0f, false);
        Assert.Equal(hp0 - 1, sim.Hp);
        Assert.True(fl.Alive);
    }

    [Fact]
    public void FlyerStomp_DefeatsAndFiresEvent()
    {
        var sim = MakeSim();
        var fl = new Flyer { Home = new Vector2(440, 250), AmpX = 0, AmpY = 0 };
        sim.Flyers.Add(fl);
        Vector2 fpos = fl.Pos;
        sim.PlayerPos = new Vector2(fpos.X, fpos.Y - 22);
        sim.PlayerVel = new Vector2(0, 120);
        sim.Step(Dt, 0f, false);
        Assert.False(fl.Alive);
        Assert.True(sim.PlayerVel.Y < 0f);
        Assert.NotEmpty(sim.DefeatsThisStep);   // 撃破イベント
    }

    [Fact]
    public void LandedThisStep_FiresOnLanding_NotWhileGrounded()
    {
        var sim = MakeSim();
        bool sawLand = false;
        for (int i = 0; i < 40; i++) { sim.Step(Dt, 0f, false); if (sim.LandedThisStep) sawLand = true; }
        Assert.True(sawLand);
        Assert.False(sim.LandedThisStep);   // 着地後の接地継続では再発火しない
    }

    [Fact]
    public void PickupEvent_FiresOnCollect()
    {
        var sim = MakeSim();
        sim.Pickups.Add(new Pickup { Pos = sim.PlayerPos, Size = 10 });
        sim.Step(Dt, 0f, false);
        Assert.Single(sim.PickupsThisStep);
    }

    // ---- チェックポイント / セーブ ----

    [Fact]
    public void ReachingCheckpoint_UpdatesRespawn()
    {
        var sim = MakeSim();
        Vector2 cpPos = sim.PlayerPos;
        sim.Checkpoints.Add(new Checkpoint { Pos = cpPos });
        sim.Step(Dt, 0f, false);
        Assert.True(sim.Checkpoints[0].Reached);
        Assert.True(sim.CheckpointThisStep);
        Assert.Equal(cpPos.X, sim.LastCheckpoint.X, 1);
        Assert.Equal(cpPos.Y, sim.LastCheckpoint.Y, 1);
    }

    [Fact]
    public void SaveLoad_RestoresProgress()
    {
        var sim = CavernTestLevel.CreateSim();
        sim.Coins = 4;
        sim.Keys = 3;
        sim.Hp = 2;
        Pickup p = sim.Pickups[0]; p.Collected = true; sim.Pickups[0] = p;
        sim.Enemies[0].Alive = false;
        sim.Checkpoints[0].Reached = true;
        sim.LastCheckpoint = new Vector2(500, 200);

        string json = sim.Export().ToJson();
        CavernSave loaded = CavernSave.FromJson(json);

        var fresh = CavernTestLevel.CreateSim();
        fresh.ApplySave(loaded);

        Assert.Equal(4, fresh.Coins);
        Assert.Equal(3, fresh.Keys);
        Assert.True(fresh.DoorOpen);            // 鍵 3 → 扉が開いた状態も復元
        Assert.Equal(2, fresh.Hp);
        Assert.True(fresh.Pickups[0].Collected);
        Assert.False(fresh.Enemies[0].Alive);
        Assert.True(fresh.Checkpoints[0].Reached);
        Assert.Equal(500, fresh.PlayerPos.X, 1);   // 復活位置 = セーブ時のチェックポイント
        Assert.Equal(200, fresh.PlayerPos.Y, 1);
    }
}
