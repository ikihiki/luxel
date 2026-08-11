using Luxel.Controls;
using Luxel.Typography;
using Luxel.UI;
using Xunit;
using static Luxel.Controls.Kit;

namespace Luxel.Tests;

/// <summary>AP-M1: エラー境界 — ユーザーコードの例外でアプリ (呼び出し元) を落とさない。</summary>
public class ErrorBoundaryTests
{
    private static LayoutContext Ctx() => new() { Font = VectorFont.LoadSystem() };

    [Fact]
    public void Effect_ThrowDoesNotPropagate_AndEffectStaysAlive()
    {
        var sig = new Signal<int>(0);
        bool boom = true;
        int ok = 0;
        Reactive.Effect(() =>
        {
            int v = sig.Value;
            if (boom && v > 0) throw new InvalidOperationException("boom");
            if (v > 0) ok++;
        });

        sig.Value = 1;          // throw — setter (呼び出し元) へ伝播しない
        boom = false;
        sig.Value = 2;          // effect は生きている — 再試行が走る
        Assert.Equal(1, ok);
    }

    private sealed class BrokenBuild : CompositeControl
    {
        public bool Broken = true;
        protected override Widget Build()
            => Broken ? throw new InvalidOperationException("live code failed") : Text("recovered");
        public void Fix() { Broken = false; Rebuild(); }
    }

    [Fact]
    public void Build_ThrowFallsBackToErrorWidget_AndRecoversOnRebuild()
    {
        var c = new BrokenBuild();
        c.Layout(Constraints.LooseW(300, 200), Ctx());   // throw しても落ちない

        Assert.NotNull(c.Root);
        Assert.Contains("live code failed", c.Root!.DebugDetail);   // ErrorWidget (赤枠 + メッセージ)

        c.Fix();
        c.Layout(Constraints.LooseW(300, 200), Ctx());
        Assert.DoesNotContain("live code failed", c.Root!.DebugDetail ?? "");   // 本来の Build に復帰
    }
}
