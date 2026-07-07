using System.Numerics;
using LuxelCavern.Core;

namespace Luxel.Tests;

/// <summary>
/// 「Luxel Cavern」の状態機械 <see cref="GameFlow"/> の決定的テスト:
/// 新規開始で Playing / ポーズ切替 / ポーズ中は進まない / 死亡で GameOver / クリアで Clear / つづきから復元。
/// </summary>
public class GameFlowTests
{
    private const float Dt = 1f / 60;

    [Fact]
    public void StartNew_EntersPlaying()
    {
        var flow = CavernTestLevel.NewFlow();
        flow.StartNew();
        Assert.Equal(GameState.Playing, flow.State);
        Assert.NotNull(flow.Sim);
    }

    [Fact]
    public void TogglePause_PlayingToPausedToPlaying()
    {
        var flow = CavernTestLevel.NewFlow();
        flow.StartNew();
        flow.TogglePause();
        Assert.Equal(GameState.Paused, flow.State);
        flow.TogglePause();
        Assert.Equal(GameState.Playing, flow.State);
    }

    [Fact]
    public void Step_WhilePaused_DoesNotAdvance()
    {
        var flow = CavernTestLevel.NewFlow();
        flow.StartNew();
        flow.TogglePause();
        Vector2 before = flow.Sim!.PlayerPos;
        flow.Step(Dt, 1f, false);
        Assert.Equal(before, flow.Sim.PlayerPos);   // 進まない
    }

    [Fact]
    public void Step_OnDeath_GoesGameOver()
    {
        var flow = CavernTestLevel.NewFlow();
        flow.StartNew();
        flow.Sim!.PlayerPos = new Vector2(100, flow.Sim.KillY + 10);   // マップ下へ落下
        flow.Step(Dt, 0f, false);
        Assert.Equal(GameState.GameOver, flow.State);
    }

    [Fact]
    public void Step_OnClear_GoesClear()
    {
        var flow = CavernTestLevel.NewFlow();
        flow.StartNew();
        flow.Sim!.Keys = 3;
        // ApplySave 経由で「鍵3 → 開扉 + 位置=扉」を作る (DoorOpen は内部設定なので)
        CavernSave save = flow.Sim.Export();
        save.PlayerX = flow.Sim.DoorPos.X;
        save.PlayerY = flow.Sim.DoorPos.Y;
        flow.Continue(save);
        flow.Step(Dt, 0f, false);
        Assert.Equal(GameState.Clear, flow.State);
    }

    [Fact]
    public void Continue_RestoresProgressAndPlays()
    {
        var src = CavernTestLevel.CreateSim();
        src.Coins = 5;
        src.Keys = 1;
        var flow = CavernTestLevel.NewFlow();
        flow.Continue(src.Export());
        Assert.Equal(GameState.Playing, flow.State);
        Assert.Equal(5, flow.Sim!.Coins);
        Assert.Equal(1, flow.Sim.Keys);
    }
}
