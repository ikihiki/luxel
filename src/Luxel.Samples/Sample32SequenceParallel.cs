using System.Numerics;
using Luxel;
using Luxel.Animation;
using Luxel.Animation.UI;
using Luxel.TwoD;
using Luxel.UI;

namespace Luxel.Samples;

/// <summary>
/// サンプル32 (AN-M2): コード DSL の Sequence/Parallel コンビネーション。
///
/// シナリオ:
///   - Card A: 左から slide-in、opacity 0→1 を同時 (Parallel) で 0.4s 進める
///   - 0.2s 遅延で Card B: 右から slide-in + fade-in (同じく Parallel) を 0.4s
///   - 全体は Sequence(parallelA, parallelB) で記述
///
/// 中間フレーム t=0/0.2/0.4/0.6 で PNG 出力、両カードの位置・透明度を検証。vk/dx 完全一致。
///
/// fluent DSL の使用例:
///   Animate.Sequence(
///     Animate.Parallel(
///       Animate.Tween(setX_A, -150f, 30f, 0.4f).WithCurve(CubicBezierCurve.EaseOut),
///       Animate.Tween(setOpacity_A, 0f, 1f, 0.4f)),
///     Animate.Parallel(
///       Animate.Tween(setX_B, 300f, 130f, 0.4f).WithCurve(CubicBezierCurve.EaseOut),
///       Animate.Tween(setOpacity_B, 0f, 1f, 0.4f))
///   ).Play(player, clock);
/// </summary>
public static class Sample32SequenceParallel
{
    public static int Run(Func<GpuDevice> createDevice)
    {
        const uint w = 256, h = 128;
        ulong fbBytes = (ulong)(w * h * 4);

        using GpuDevice device = createDevice();
        Console.WriteLine($"device: {device.Name}");

        // 2 つの Card 状態
        var xA = new Signal<float>(-150f);
        var opacityA = new Signal<float>(0f);
        var xB = new Signal<float>(300f);
        var opacityB = new Signal<float>(0f);

        var clock = new FixedFrameClock { FrameRate = 60f };
        var player = new AnimationPlayer();
        player.Update(clock);

        // === DSL でコンビネーション ===
        Animate.Sequence(
            Animate.Parallel(
                Animate.Tween(SignalAnimationTarget.For(xA), -150f, 30f, 0.4f)
                       .WithCurve(CubicBezierCurve.EaseOut),
                Animate.Tween(SignalAnimationTarget.For(opacityA), 0f, 1f, 0.4f)
            ),
            Animate.Parallel(
                Animate.Tween(SignalAnimationTarget.For(xB), 300f, 130f, 0.4f)
                       .WithCurve(CubicBezierCurve.EaseOut),
                Animate.Tween(SignalAnimationTarget.For(opacityB), 0f, 1f, 0.4f)
            )
        ).Play(player, clock);

        Console.WriteLine($"  active tracks after Schedule: {player.ActiveCount}");

        using var raster = new Rasterizer2D(device);
        using GpuBuffer fb = device.Malloc(fbBytes, GpuMemoryKind.HostMapped);

        float[] snapshotsAtSec = { 0.00f, 0.20f, 0.40f, 0.60f, 0.80f };
        var observed = new List<(float XA, float OA, float XB, float OB)>();

        int snapIdx = 0;
        for (int frame = 0; frame <= 60 && snapIdx < snapshotsAtSec.Length; frame++)
        {
            clock.Frame = frame;
            player.Update(clock);

            while (snapIdx < snapshotsAtSec.Length
                   && clock.TimeSec + 1e-4f >= snapshotsAtSec[snapIdx])
            {
                float xa = xA.Peek(), oa = opacityA.Peek();
                float xb = xB.Peek(), ob = opacityB.Peek();
                observed.Add((xa, oa, xb, ob));

                var scene = new Scene2D();
                // Card A (青系) と Card B (赤系) を opacity を乗せた色で描画
                uint colorA = MakeColor(80, 130, 240, oa);
                uint colorB = MakeColor(230, 80, 100, ob);
                scene.FillRoundedRect(colorA, xa, 24, 80, 36, 8);
                scene.FillRoundedRect(colorB, xb, 70, 80, 36, 8);
                using var encoded = raster.Encode(scene);
                fb.Span<byte>((int)fbBytes).Clear();
                using (var cmd = device.MainQueue.StartCommandRecording())
                {
                    raster.Render(cmd, encoded, Camera2D.Pixels, w, h, fb);
                    cmd.Finish();
                    device.MainQueue.SubmitAndWait(cmd);
                }
                string png = Path.Combine(AppContext.BaseDirectory, $"sequence_t{snapshotsAtSec[snapIdx]:0.00}.png");
                PngWriter.WriteRgba(png, (int)w, (int)h, fb.Span<byte>((int)fbBytes));
                Console.WriteLine($"  t={snapshotsAtSec[snapIdx]:0.00}s: A(x={xa:0.0}, opacity={oa:0.00}) | B(x={xb:0.0}, opacity={ob:0.00})");
                snapIdx++;
            }
        }

        // === 検証 ===
        bool ok = true;
        // t=0: A の開始値、B はまだ動いていない
        if (Math.Abs(observed[0].XA - (-150f)) > 1f) { ok = false; Console.Error.WriteLine($"FAILED: t=0 A.x"); }
        if (Math.Abs(observed[0].OA - 0f) > 0.01f) { ok = false; Console.Error.WriteLine($"FAILED: t=0 A.opacity"); }
        if (Math.Abs(observed[0].XB - 300f) > 1f) { ok = false; Console.Error.WriteLine($"FAILED: t=0 B.x"); }

        // t=0.4: A は完了 (X=30, opacity=1)、B は開始 (まだ動いていない)
        if (Math.Abs(observed[2].XA - 30f) > 1f) { ok = false; Console.Error.WriteLine($"FAILED: t=0.4 A.x expected 30, got {observed[2].XA}"); }
        if (Math.Abs(observed[2].OA - 1f) > 0.01f) { ok = false; Console.Error.WriteLine($"FAILED: t=0.4 A.opacity"); }
        if (Math.Abs(observed[2].XB - 300f) > 1f) { ok = false; Console.Error.WriteLine($"FAILED: t=0.4 B.x not at start, got {observed[2].XB}"); }

        // t=0.8: A も B も完了
        if (Math.Abs(observed[4].XB - 130f) > 1f) { ok = false; Console.Error.WriteLine($"FAILED: t=0.8 B.x expected 130, got {observed[4].XB}"); }
        if (Math.Abs(observed[4].OB - 1f) > 0.01f) { ok = false; Console.Error.WriteLine($"FAILED: t=0.8 B.opacity"); }

        // Player が空 (全 Sequence 完了)
        if (player.ActiveCount != 0) { ok = false; Console.Error.WriteLine($"FAILED: player ActiveCount expected 0, got {player.ActiveCount}"); }

        Console.WriteLine(ok ? "OK: AN-M2 (Animate.Sequence + Animate.Parallel + 絶対時刻 Clock) 動作"
                             : "FAILED");
        return ok ? 0 : 1;
    }

    private static uint MakeColor(byte r, byte g, byte b, float opacity)
    {
        byte a = (byte)Math.Clamp(opacity * 255f + 0.5f, 0f, 255f);
        return Color2D.Rgba(r, g, b, a);
    }
}
