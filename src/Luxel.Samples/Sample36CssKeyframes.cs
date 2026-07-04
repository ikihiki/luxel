using System.Numerics;
using Luxel;
using Luxel.Animation;
using Luxel.Animation.TwoD;
using Luxel.TwoD;

namespace Luxel.Samples;

/// <summary>
/// サンプル36 (AN-M6a): CSS `@keyframes` テキストから AnimationClip を生成し、RetainedCanvas で再生。
///
/// CSS:
/// <code>
/// @keyframes slideAndFade {
///   0%   { opacity: 0; transform: translateX(-120px); background-color: rgba(60,130,240,255); }
///   50%  { opacity: 1; transform: translateX(0px);   }
///   100% { opacity: 1; transform: translateX(80px);  background-color: rgba(230,80,100,255); }
/// }
/// </code>
///
/// パース → opacity + translationX + color の 3 Track → RetainedCanvas の 1 ノードに適用。
/// 4 フレーム t=0/0.3/0.6/1.0 で PNG 出力、vk/dx 一致。
/// </summary>
public static class Sample36CssKeyframes
{
    private const string Css = @"
        @keyframes slideAndFade {
          0%   { opacity: 0; transform: translateX(-120px); background-color: rgba(60,130,240,255); }
          50%  { opacity: 1; transform: translateX(0px);    }
          100% { opacity: 1; transform: translateX(80px);   background-color: rgba(230,80,100,255); }
        }";

    public static int Run(Func<GpuDevice> createDevice)
    {
        const uint w = 256, h = 128;
        ulong fbBytes = (ulong)(w * h * 4);

        using GpuDevice device = createDevice();
        Console.WriteLine($"device: {device.Name}");

        // === CSS → AnimationClip ===
        var warnings = new List<string>();
        AnimationClip clip = CssKeyframesImporter.Parse(Css, targetPrefix: "card", durationSec: 1.0f, warnings: warnings);
        foreach (var wn in warnings) Console.Error.WriteLine($"  WARN: {wn}");
        Console.WriteLine($"  parsed clip='{clip.Name}', tracks={clip.Tracks.Length}, duration={clip.Duration}s");
        foreach (var tr in clip.Tracks)
            Console.WriteLine($"    track {tr.TargetPath} ({tr.KeyframeCount} keyframes)");

        // === RetainedCanvas + ノード ===
        using var raster = new Rasterizer2D(device);
        using var canvas = new RetainedCanvas(raster);
        var card = canvas.AddChild(canvas.Root);
        card.Content = new Scene2D().FillRoundedRect(Color2D.White, 0, 0, 80, 50, 10);
        card.Transform = Affine2D.Translate(0, 40);
        card.Color = Color2D.Rgba(60, 130, 240, 255);
        card.Opacity = 0f;

        // === Target + Player ===
        var target = new RetainedCanvasAnimationTarget().Bind("card", card);
        var clock = new FixedFrameClock { FrameRate = 60f };
        var player = new AnimationPlayer();
        player.Update(clock);
        Animate.Clip(clip, target).Play(player, clock);

        // === Render ===
        using GpuBuffer fb = device.Malloc(fbBytes, GpuMemoryKind.HostMapped);
        float[] snapshotsAtSec = { 0.0f, 0.3f, 0.6f, 1.0f };

        int snapIdx = 0;
        for (int frame = 0; frame <= 70 && snapIdx < snapshotsAtSec.Length; frame++)
        {
            clock.Frame = frame;
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
                string png = Path.Combine(AppContext.BaseDirectory, $"css_t{snapshotsAtSec[snapIdx]:0.0}.png");
                PngWriter.WriteRgba(png, (int)w, (int)h, fb.Span<byte>((int)fbBytes));
                var t = card.Transform;
                Console.WriteLine($"  t={snapshotsAtSec[snapIdx]:0.0}s: x={t.E:0.0}, opacity={card.Opacity:0.00}, color=0x{card.Color:X8}");
                snapIdx++;
            }
        }

        // === 検証 ===
        bool ok = clip.Tracks.Length == 3;
        if (!ok) Console.Error.WriteLine($"FAILED: track count expected 3, got {clip.Tracks.Length}");

        Console.WriteLine(ok ? "OK: AN-M6a (CSS @keyframes → AnimationClip + RetainedCanvas 再生) 動作"
                             : "FAILED");
        return ok ? 0 : 1;
    }
}
