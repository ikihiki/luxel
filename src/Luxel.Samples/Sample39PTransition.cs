using System.Numerics;
using Luxel;
using Luxel.Animation;
using Luxel.Animation.UI;
using Luxel.TwoD;
using Luxel.UI;
using static Luxel.UI.Decl;   // P を導入

namespace Luxel.Samples;

/// <summary>
/// サンプル39 (TR-M2): <c>P.Transition.*</c> 添付プロパティで宣言的に CSS transition 風アニメ。
///
/// Grid.Column と同じ流儀:
/// <code>
/// INodePart[] cardAParts = [
///     P.Transition.Color(0.25f, CubicBezierCurve.EaseInOut),
///     P.Transition.Scale(0.15f, CubicBezierCurve.EaseOut),
/// ];
/// var animatedColor = WidgetTransitions.Wrap&lt;uint&gt;(cardAParts, TransitionKeys.Color, raw, player, clock);
/// </code>
///
/// 2 カード、各々に異なる transition spec を宣言。hover signal の切替えで自動補間。
/// vk/dx 完全一致を確認。
/// </summary>
public static class Sample39PTransition
{
    public static int Run(Func<GpuDevice> createDevice)
    {
        const uint w = 256, h = 128;
        ulong fbBytes = (ulong)(w * h * 4);

        using GpuDevice device = createDevice();
        Console.WriteLine($"device: {device.Name}");

        // === RetainedCanvas + 2 ノード ===
        using var raster = new Rasterizer2D(device);
        using var canvas = new RetainedCanvas(raster);
        var cardA = canvas.AddChild(canvas.Root);
        cardA.Content = new Scene2D().FillRoundedRect(Color2D.White, 0, 0, 60, 40, 8);
        cardA.Transform = Affine2D.Translate(40, 40);
        cardA.Color = Color2D.Rgba(60, 130, 240, 255);

        var cardB = canvas.AddChild(canvas.Root);
        cardB.Content = new Scene2D().FillRoundedRect(Color2D.White, 0, 0, 60, 40, 8);
        cardB.Transform = Affine2D.Translate(150, 40);
        cardB.Color = Color2D.Rgba(40, 200, 120, 255);

        // === P.Transition.* 添付プロパティで spec を宣言 ===
        INodePart[] cardAParts = [
            P.Transition.Color(0.30f, CubicBezierCurve.EaseInOut),
            P.Transition.Scale(0.20f, CubicBezierCurve.EaseOut),
        ];
        INodePart[] cardBParts = [
            P.Transition.Color(0.15f, CubicBezierCurve.EaseInOut),   // B は色が速い
            P.Transition.TranslationY(0.25f, CubicBezierCurve.EaseInOut),
        ];

        var clock = new FixedFrameClock { FrameRate = 60f };
        var player = new AnimationPlayer();
        player.Update(clock);

        // === setter ラッパーをヘルパで生成 ===
        var animatedColorA = WidgetTransitions.Wrap<uint>(cardAParts, TransitionKeys.Color,
            v => cardA.Color = v, player, clock);
        var animatedScaleA = WidgetTransitions.Wrap<float>(cardAParts, TransitionKeys.Scale,
            v => cardA.Transform = new Affine2D { A = v, D = v, E = 40, F = 40 }, player, clock);

        var animatedColorB = WidgetTransitions.Wrap<uint>(cardBParts, TransitionKeys.Color,
            v => cardB.Color = v, player, clock);
        var animatedYB = WidgetTransitions.Wrap<float>(cardBParts, TransitionKeys.TranslationY,
            v => cardB.Transform = new Affine2D { A = 1f, D = 1f, E = 150, F = v }, player, clock);

        // === Signal で hover 状態を切替 ===
        var hovered = new Signal<bool>(false);
        using var subColorA = SignalTransition.Watch(hovered,
            h => animatedColorA(h ? Color2D.Rgba(230, 80, 100, 255) : Color2D.Rgba(60, 130, 240, 255)));
        using var subScaleA = SignalTransition.Watch(hovered, h => animatedScaleA(h ? 1.2f : 1.0f));
        using var subColorB = SignalTransition.Watch(hovered,
            h => animatedColorB(h ? Color2D.Rgba(255, 200, 60, 255) : Color2D.Rgba(40, 200, 120, 255)));
        using var subYB = SignalTransition.Watch(hovered, h => animatedYB(h ? 20f : 40f));

        using GpuBuffer fb = device.Malloc(fbBytes, GpuMemoryKind.HostMapped);

        float[] snapshotsAtSec = { 0.0f, 0.15f, 0.35f, 0.7f };
        int snapIdx = 0;
        bool triggered = false;

        for (int frame = 0; frame <= 60 && snapIdx < snapshotsAtSec.Length; frame++)
        {
            clock.Frame = frame;
            if (!triggered && clock.TimeSec >= 0.05f)
            {
                hovered.Value = true;
                triggered = true;
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
                string png = Path.Combine(AppContext.BaseDirectory, $"ptransition_t{snapshotsAtSec[snapIdx]:0.00}.png");
                PngWriter.WriteRgba(png, (int)w, (int)h, fb.Span<byte>((int)fbBytes));
                Console.WriteLine(
                    $"  t={snapshotsAtSec[snapIdx]:0.00}s: A(color=0x{cardA.Color:X8}, scaleA={cardA.Transform.A:0.00}) | B(color=0x{cardB.Color:X8}, yB={cardB.Transform.F:0.0})");
                snapIdx++;
            }
        }

        // === 検証 ===
        bool ok = true;
        // hover 起動 (t=0.05) 後の進行を確認
        // t=0.15: 補間中 (B は 0.15s で完了直前、A はまだ進行中)
        // t=0.35: A も B も完了 (max 0.30s 以上経過)
        // t=0.7: 全完了 (静止)

        // A: scaleA は最終的に 1.2 に到達 (t=0.35 で完了 = 0.05+0.20=0.25 経過)
        // 注: snapshotsAtSec[2] = 0.35 → 0.30 (経過時間) > 0.20 (scaleA duration) なので完了
        if (Math.Abs(cardA.Transform.A - 1.2f) > 0.01f) { ok = false; Console.Error.WriteLine($"FAILED: scaleA expected 1.2, got {cardA.Transform.A}"); }
        // B: TranslationY は最終 20
        if (Math.Abs(cardB.Transform.F - 20f) > 0.5f) { ok = false; Console.Error.WriteLine($"FAILED: yB expected 20, got {cardB.Transform.F}"); }

        Console.WriteLine(ok ? "OK: TR-M2 (P.Transition.* 添付プロパティ + WidgetTransitions ヘルパ) 動作"
                             : "FAILED");
        return ok ? 0 : 1;
    }
}
