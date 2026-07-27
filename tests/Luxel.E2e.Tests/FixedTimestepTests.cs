using System.Numerics;
using Luxel.Ecs;
using Luxel.Framework;
using Xunit;

namespace Luxel.Gallery.E2eTests;

/// <summary>FixedUpdate の蓄積器 (<see cref="FixedTimestep"/>) と描画補間 (<see cref="TransformInterpolationSystem"/>) の
/// 決定的な単体テスト (GPU 不要)。<see cref="FixedTimestep"/> は Luxel.Framework (net10.0-windows) にあるため、
/// windows ターゲットのこのテストプロジェクトに置く。</summary>
public class FixedTimestepTests
{
    [Fact]
    public void FixedTimestep_IsOwnedByFrameworkAssembly()
        => Assert.Equal("Luxel.Framework", typeof(FixedTimestep).Assembly.GetName().Name);

    [Fact]
    public void Advance_ExactMultiple_RunsExactSteps()
    {
        var acc = new FixedTimestep(1.0 / 60);
        // 1/60 ちょうど → 1 ステップ、余り 0
        Assert.Equal(1, acc.Advance(1.0 / 60));
        Assert.Equal(0f, acc.Alpha, precision: 5);
    }

    [Fact]
    public void Advance_AccumulatesRemainder_AcrossFrames()
    {
        var acc = new FixedTimestep(1.0 / 60);
        // dt = 0.035s = 1/60(0.01667)×2 + 余り 0.001667 → 2 ステップ
        int steps = acc.Advance(0.035);
        Assert.Equal(2, steps);
        // 余り = 0.035 - 2/60 = 0.0016667 → alpha = 余り/FixedDt = 0.1
        Assert.Equal(0.1f, acc.Alpha, precision: 3);

        // 次フレームで残り 0.001667 + 0.035 = 0.036667 → 2 ステップ + 余り
        int steps2 = acc.Advance(0.035);
        Assert.Equal(2, steps2);
        Assert.Equal(4, acc.TotalSteps);
    }

    [Fact]
    public void Advance_SubStepDt_RunsZeroThenOne()
    {
        var acc = new FixedTimestep(1.0 / 60);
        // 1/120 ずつ → 1 回目は 0 ステップ (溜まりきらない)、2 回目で 1 ステップ
        Assert.Equal(0, acc.Advance(1.0 / 120));
        Assert.Equal(0.5f, acc.Alpha, precision: 3);   // 半分溜まった
        Assert.Equal(1, acc.Advance(1.0 / 120));
        Assert.Equal(0f, acc.Alpha, precision: 3);
    }

    [Fact]
    public void Advance_HugeDt_ClampsToMaxSteps_AndDropsExcess()
    {
        var acc = new FixedTimestep(1.0 / 60, maxStepsPerFrame: 4);
        // 巨大 dt = 1s → 本来 60 ステップだが上限 4 でクランプ、残りは捨てる
        int steps = acc.Advance(1.0);
        Assert.Equal(4, steps);
        // 余剰を捨てたので alpha は [0,1) に収まる
        Assert.InRange(acc.Alpha, 0f, 1f);
        Assert.True(acc.DroppedSteps > 0);
        // 次フレームは平常運転 (処理落ちを引きずらない)
        Assert.Equal(1, acc.Advance(1.0 / 60));
    }

    [Fact]
    public void Advance_IsDeterministic_SameDtSequence_SameSteps()
    {
        double[] dts = { 0.017, 0.016, 0.020, 0.008, 0.033, 0.016, 0.016 };
        var a = new FixedTimestep(1.0 / 60);
        var b = new FixedTimestep(1.0 / 60);
        foreach (double dt in dts)
        {
            Assert.Equal(a.Advance(dt), b.Advance(dt));
            Assert.Equal(a.Alpha, b.Alpha, precision: 6);
        }
        Assert.Equal(a.TotalSteps, b.TotalSteps);
    }

    [Fact]
    public void Advance_NoDoubleErrorDrift_Over600Frames()
    {
        // 1/60 を 600 回 → ちょうど 600 ステップ (float 蓄積だと誤差で 599/601 になる前例)
        var acc = new FixedTimestep(1.0 / 60);
        long total = 0;
        for (int i = 0; i < 600; i++) total += acc.Advance(1.0 / 60);
        Assert.Equal(600, total);
    }

    [Fact]
    public void Reset_ClearsAccumulator_KeepsTotals()
    {
        var acc = new FixedTimestep(1.0 / 60);
        acc.Advance(1.0 / 120);   // 半分溜める
        Assert.True(acc.Alpha > 0);
        acc.Reset();
        Assert.Equal(0f, acc.Alpha);
        // Reset 後は溜まりが無いので 1/120 では 0 ステップ
        Assert.Equal(0, acc.Advance(1.0 / 120));
    }

    [Theory]
    [InlineData(0.016, 1.0, 0.016)]
    [InlineData(0.016, 0.5, 0.008)]
    [InlineData(0.016, 2.0, 0.032)]
    [InlineData(0.016, 0.0, 0.0001)]
    [InlineData(1.0, 1.0, 0.1)]
    public void ScaleDt_AppliesTimeScale_AndClamps(double raw, double scale, double expected)
        => Assert.Equal(expected, FixedTimestep.ScaleDt(raw, scale), precision: 5);

    [Fact]
    public void TimeScale_Half_Doubles_FramesPerFixedStep()
    {
        var fixedStep = new FixedTimestep(fixedDt: 1.0 / 60, maxStepsPerFrame: 8);
        int totalSteps = 0;
        for (int i = 0; i < 120; i++)
        {
            float dt = FixedTimestep.ScaleDt(1.0 / 60, 0.5);
            totalSteps += fixedStep.Advance(dt);
        }
        Assert.InRange(totalSteps, 58, 62);
        Assert.Equal(0, fixedStep.DroppedSteps);
    }

    // ==================== TransformInterpolationSystem ====================

    [Fact]
    public void Interpolation_LerpsPositionByAlpha()
    {
        var world = new World();
        var it = new InterpolatedTransform(Vector3.Zero, Quaternion.Identity, Vector3.One);
        it.Push(new Vector3(10, 0, 0), Quaternion.Identity, Vector3.One);   // prev=0, curr=10
        var e = world.CreateEntity(it);
        e.AddComponent(new LocalTransform(Matrix4x4.Identity));

        TransformInterpolationSystem.Run(world, 0.5f);

        Vector3 pos = e.GetComponent<LocalTransform>().Matrix.Translation;
        Assert.Equal(5f, pos.X, precision: 4);   // 中間
    }

    [Fact]
    public void Interpolation_AddsLocalTransform_WhenMissing()
    {
        var world = new World();
        var it = new InterpolatedTransform(new Vector3(2, 0, 0), Quaternion.Identity, Vector3.One);
        var e = world.CreateEntity(it);   // LocalTransform 無し

        TransformInterpolationSystem.Run(world, 1f);

        Assert.True(e.HasComponent<LocalTransform>());
        Assert.Equal(2f, e.GetComponent<LocalTransform>().Matrix.Translation.X, precision: 4);
    }

    [Fact]
    public void Interpolation_AlphaZeroAndOne_HitPrevAndCurr()
    {
        var it = new InterpolatedTransform(Vector3.Zero, Quaternion.Identity, Vector3.One);
        it.Push(new Vector3(4, 0, 0), Quaternion.Identity, Vector3.One);
        Assert.Equal(0f, it.Sample(0f).Translation.X, precision: 4);   // prev
        Assert.Equal(4f, it.Sample(1f).Translation.X, precision: 4);   // curr
    }

    [Fact]
    public void Teleport_RemovesInterpolationJump()
    {
        var it = new InterpolatedTransform(Vector3.Zero, Quaternion.Identity, Vector3.One);
        it.Push(new Vector3(4, 0, 0), Quaternion.Identity, Vector3.One);
        it.Teleport(new Vector3(100, 0, 0), Quaternion.Identity, Vector3.One);
        // どの alpha でも 100 (prev==curr) — テレポート後に補間で戻らない
        Assert.Equal(100f, it.Sample(0f).Translation.X, precision: 4);
        Assert.Equal(100f, it.Sample(0.5f).Translation.X, precision: 4);
    }
}
