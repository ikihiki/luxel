using System.Numerics;
using LuxelCavern.Core;
using Luxel.TwoD;

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
        TileMap map = CavernLevel.Build(ts);
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
}
