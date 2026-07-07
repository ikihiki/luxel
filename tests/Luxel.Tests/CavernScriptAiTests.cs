using System.Linq;
using System.Numerics;
using LuxelCavern.Core;
using Luxel.Scripting;

namespace Luxel.Tests;

/// <summary>
/// 敵 AI の差し替え (<see cref="Walker.Ai"/>) と **.csx で書いた AI** のドッグフーディング (タスク 01/19)。
/// AI フックが既定巡回を上書きすること / Roslyn で .csx をコンパイルして得た delegate が敵を駆動すること。
/// </summary>
public class CavernScriptAiTests
{
    [Fact]
    public void AiHook_OverridesDefaultPatrol()
    {
        CavernSim sim = CavernTestLevel.CreateSim();
        Walker w = sim.Enemies[0];
        w.Ai = (self, s, dt) => self.Pos.X += 100f * dt;   // ひたすら右へ
        float x0 = w.Pos.X;
        sim.Step(1f / 60, 0f, false);
        Assert.True(w.Pos.X > x0);
    }

    [Fact]
    public void CsxAi_CompilesAndChasesPlayer()
    {
        var host = new ScriptHost(
            references:
            [
                typeof(object).Assembly, typeof(Enumerable).Assembly,
                typeof(Vector2).Assembly, typeof(Luxel.TwoD.RectF).Assembly,
                typeof(Luxel.GpuDevice).Assembly, typeof(Walker).Assembly,
            ],
            usings: ["System", "LuxelCavern.Core"],
            globalsType: typeof(object));

        // .csx: プレイヤーの方向へ歩く敵 AI
        const string code =
            "(Action<Walker, CavernSim, float>)((w, s, dt) => w.Pos.X += (s.PlayerCenter.X > w.Pos.X ? 60f : -60f) * dt)";
        ScriptResult r = host.Run(code, new object());
        Assert.True(r.Success, string.Join(" | ", r.Diagnostics.Select(d => d.Message)));
        var ai = Assert.IsAssignableFrom<Action<Walker, CavernSim, float>>(r.ReturnValue);

        CavernSim sim = CavernTestLevel.CreateSim();
        Walker w = sim.Enemies[0];
        w.Ai = ai;
        sim.PlayerPos = new Vector2(w.Pos.X + 120, sim.PlayerPos.Y);   // プレイヤーは敵の右
        float x0 = w.Pos.X;
        sim.Step(1f / 60, 0f, false);
        Assert.True(w.Pos.X > x0);   // プレイヤー方向 (右) へ追跡
    }
}
