using LuxelCavern.Core;

namespace Luxel.Tests;

/// <summary>
/// SE 検出 <see cref="CavernSfxDetector"/> の決定的テスト: 初回は無音 (基準取り) / コイン・鍵・HP の差分 /
/// 撃破リスト / クリア遷移 / ジャンプフラグ (実 sim) / <see cref="CavernSfxDetector.Reset"/> で再基準化。
/// </summary>
public class CavernSfxDetectorTests
{
    private static List<CavernSfxCue> Detect(CavernSfxDetector d, CavernSim sim)
    {
        var cues = new List<CavernSfxCue>();
        d.Detect(sim, cues);
        return cues;
    }

    [Fact]
    public void FirstDetect_IsSilent_Baseline()
    {
        var sim = CavernTestLevel.CreateSim();
        sim.Coins = 3;   // 既に貯まっていても初回は鳴らさない
        var d = new CavernSfxDetector();
        Assert.Empty(Detect(d, sim));
    }

    [Fact]
    public void CoinIncrement_EmitsCoin()
    {
        var sim = CavernTestLevel.CreateSim();
        var d = new CavernSfxDetector();
        Detect(d, sim);                 // baseline
        sim.Coins++;
        Assert.Equal(new[] { CavernSfxCue.Coin }, Detect(d, sim));
    }

    [Fact]
    public void MultipleCoinsInOneStep_EmitsOnePerCoin()
    {
        var sim = CavernTestLevel.CreateSim();
        var d = new CavernSfxDetector();
        Detect(d, sim);
        sim.Coins += 3;
        Assert.Equal(3, Detect(d, sim).Count(c => c == CavernSfxCue.Coin));
    }

    [Fact]
    public void KeyIncrement_EmitsKey()
    {
        var sim = CavernTestLevel.CreateSim();
        var d = new CavernSfxDetector();
        Detect(d, sim);
        sim.Keys++;
        Assert.Contains(CavernSfxCue.Key, Detect(d, sim));
    }

    [Fact]
    public void HpDecrease_EmitsHurt()
    {
        var sim = CavernTestLevel.CreateSim();
        var d = new CavernSfxDetector();
        Detect(d, sim);
        sim.Hp--;
        Assert.Contains(CavernSfxCue.Hurt, Detect(d, sim));
    }

    [Fact]
    public void DefeatList_EmitsDefeat()
    {
        var sim = CavernTestLevel.CreateSim();
        var d = new CavernSfxDetector();
        Detect(d, sim);
        sim.DefeatsThisStep.Add(new System.Numerics.Vector2(1, 2));
        Assert.Contains(CavernSfxCue.Defeat, Detect(d, sim));
    }

    [Fact]
    public void ClearTransition_EmitsClearOnce()
    {
        var sim = CavernTestLevel.CreateSim();
        var d = new CavernSfxDetector();
        Detect(d, sim);
        sim.Result = CavernResult.Cleared;
        Assert.Contains(CavernSfxCue.Clear, Detect(d, sim));
        Assert.DoesNotContain(CavernSfxCue.Clear, Detect(d, sim));   // 遷移エッジのみ
    }

    [Fact]
    public void JumpFlag_EmitsJump()
    {
        var sim = CavernTestLevel.CreateSim();
        const float dt = 1f / 60;
        for (int i = 0; i < 240 && !sim.OnGround; i++) sim.Step(dt, 0f, false);   // 着地まで
        Assert.True(sim.OnGround, "テスト前提: プレイヤーが接地している");

        var d = new CavernSfxDetector();
        Detect(d, sim);                       // baseline (接地状態)
        sim.Step(dt, 0f, jumpPressed: true);  // 踏み切り
        Assert.True(sim.JumpedThisStep);
        Assert.Contains(CavernSfxCue.Jump, Detect(d, sim));
    }

    [Fact]
    public void Reset_RebaselinesSoNextDetectIsSilent()
    {
        var sim = CavernTestLevel.CreateSim();
        var d = new CavernSfxDetector();
        Detect(d, sim);
        sim.Coins += 5;
        d.Reset();                       // sim 差し替え相当
        Assert.Empty(Detect(d, sim));    // 差分があっても再基準化で無音
    }
}
