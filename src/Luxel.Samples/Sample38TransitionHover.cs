using System.Numerics;
using Luxel;
using Luxel.Animation;
using Luxel.Animation.UI;
using Luxel.TwoD;
using Luxel.UI;

namespace Luxel.Samples;

/// <summary>
/// サンプル38 (TR-M1): CSS transition 風の implicit な値補間。
///
/// `hovered` Signal をフレーム毎に切替え (frame 6 で true, frame 36 で false, frame 60 まで)、
/// `Transition.Watch(hovered, ...)` 経由で:
///   - card color: Blue (hovered=false) ↔ Red (hovered=true) を 0.25s で補間
///   - card scaleX: 1.0 ↔ 1.15 を 0.15s で補間 (color とは別 duration)
/// を自動アニメ。`Signal.Value = ...` を呼ぶだけで補間が起動するのが CSS transition との等価関係。
///
/// 4 フレーム (t=0/0.18/0.4/0.85) で値・PNG を取得し、補間の進行と smooth interrupt を観測。
/// </summary>
public static class Sample38TransitionHover
{
    public static int Run(Func<GpuDevice> createDevice)
    {
        const uint w = 256, h = 128;
        ulong fbBytes = (ulong)(w * h * 4);

        using GpuDevice device = createDevice();
        Console.WriteLine($"device: {device.Name}");

        // === RetainedCanvas + 1 ノード ===
        using var raster = new Rasterizer2D(device);
        using var canvas = new RetainedCanvas(raster);
        var card = canvas.AddChild(canvas.Root);
        card.Content = new Scene2D().FillRoundedRect(Color2D.White, 0, 0, 80, 50, 10);
        card.Transform = Affine2D.Translate(80, 40);
        card.Color = Color2D.Rgba(60, 130, 240, 255);

        // === Signal + Transition セットアップ ===
        var hovered = new Signal<bool>(false);
        var clock = new FixedFrameClock { FrameRate = 60f };
        var player = new AnimationPlayer();
        player.Update(clock);

        // 色: hovered で Red、そうでなければ Blue
        var animatedColor = Transition.Animate<uint>(
            v => card.Color = v, player, clock,
            duration: 0.25f, curve: CubicBezierCurve.EaseInOut);
        // scaleX: hovered で 1.15、そうでなければ 1.0 (別 duration を試す)
        var animatedScale = Transition.Animate<float>(
            v => card.Transform = new Affine2D { A = v, D = 1f, E = 80, F = 40 },
            player, clock,
            duration: 0.15f, curve: CubicBezierCurve.EaseOut);

        using var subColor = SignalTransition.Watch(hovered,
            h => animatedColor(h ? Color2D.Rgba(230, 80, 100, 255) : Color2D.Rgba(60, 130, 240, 255)));
        using var subScale = SignalTransition.Watch(hovered,
            h => animatedScale(h ? 1.15f : 1.0f));

        using GpuBuffer fb = device.Malloc(fbBytes, GpuMemoryKind.HostMapped);

        // hover トリガ: frame 6 (0.1s) で hovered=true、frame 36 (0.6s) で hovered=false
        float[] snapshotsAtSec = { 0.0f, 0.18f, 0.4f, 0.85f };
        int snapIdx = 0;
        bool triggeredOn = false, triggeredOff = false;
        var observed = new List<(uint Color, float ScaleX)>();

        for (int frame = 0; frame <= 60 && snapIdx < snapshotsAtSec.Length; frame++)
        {
            clock.Frame = frame;
            // Trigger 時刻に hovered を切替え (Signal の値変更で ReactiveEffect → Transition.Animate が起動)
            if (!triggeredOn && clock.TimeSec >= 0.1f)
            {
                hovered.Value = true;
                triggeredOn = true;
            }
            if (!triggeredOff && clock.TimeSec >= 0.6f)
            {
                hovered.Value = false;
                triggeredOff = true;
            }
            player.Update(clock);

            while (snapIdx < snapshotsAtSec.Length
                   && clock.TimeSec + 1e-4f >= snapshotsAtSec[snapIdx])
            {
                fb.Span<byte>((int)fbBytes).Clear();
                using (var cmd = device.MainQueue.StartCommandRecording())
                {
                    canvas.Render(cmd, Camera2D.Pixels, w, h, fb);
                    cmd.Finish();
                    device.MainQueue.SubmitAndWait(cmd);
                }
                observed.Add((card.Color, card.Transform.A));
                string png = Path.Combine(AppContext.BaseDirectory, $"transition_t{snapshotsAtSec[snapIdx]:0.00}.png");
                PngWriter.WriteRgba(png, (int)w, (int)h, fb.Span<byte>((int)fbBytes));
                Console.WriteLine(
                    $"  t={snapshotsAtSec[snapIdx]:0.00}s hovered={hovered.Peek()}: color=0x{card.Color:X8}, scaleX={card.Transform.A:0.000}");
                snapIdx++;
            }
        }

        // === 検証 ===
        bool ok = true;
        // t=0: 初期値 (Blue, scale 1.0)
        if ((observed[0].Color & 0xff) > 100) { ok = false; Console.Error.WriteLine($"FAILED: t=0 R should be low (blue), got {observed[0].Color & 0xff}"); }
        if (Math.Abs(observed[0].ScaleX - 1f) > 0.01f) { ok = false; Console.Error.WriteLine($"FAILED: t=0 scale should be 1.0, got {observed[0].ScaleX}"); }
        // t=0.18: hover on 後、color は赤側に近づく、scale は 1.15 に近づく (scale は 0.15s なのでほぼ完了)
        if ((observed[1].Color & 0xff) <= (observed[0].Color & 0xff)) { ok = false; Console.Error.WriteLine($"FAILED: t=0.18 R should increase (toward red)"); }
        if (observed[1].ScaleX <= observed[0].ScaleX) { ok = false; Console.Error.WriteLine($"FAILED: t=0.18 scale should increase"); }
        // t=0.4: hover on 完了 (color/scale ともピーク)
        if (observed[2].ScaleX < 1.14f || observed[2].ScaleX > 1.16f) { ok = false; Console.Error.WriteLine($"FAILED: t=0.4 scale should be ~1.15, got {observed[2].ScaleX}"); }
        // t=0.85: hover off 完了
        if (Math.Abs(observed[3].ScaleX - 1f) > 0.02f) { ok = false; Console.Error.WriteLine($"FAILED: t=0.85 scale should return to ~1.0, got {observed[3].ScaleX}"); }

        Console.WriteLine(ok ? "OK: TR-M1 (Transition.Animate + Watch, smooth interrupt) 動作"
                             : "FAILED");
        return ok ? 0 : 1;
    }
}
