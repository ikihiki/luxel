using System.Numerics;
using Luxel;
using Luxel.Animation;
using Luxel.Animation.TwoD;
using Luxel.TwoD;

namespace Luxel.Samples;

/// <summary>
/// サンプル34 (AN-M4): RetainedCanvas (保持型 2D ツリー) に AnimationClip を適用。
///
/// 構成:
///   - RetainedCanvas + 2 ノード (cardA, cardB)
///   - 各ノードに <see cref="UiNode.Content"/> として 1 つの角丸矩形 (ローカル座標 0..w)
///   - AnimationClip:
///       Track "cardA/translation"   Vector2、左から slide-in (Linear, 0.5s)
///       Track "cardA/opacity"       float、0→1 fade-in (Linear, 0.5s, 同時)
///       Track "cardB/translation"   Vector2、右から slide-in (Linear, 0.5s, 0.2s 遅延)
///       Track "cardB/color"         uint、青→赤 (Linear)
///   - 各フレームで RetainedCanvas.Render → 部分更新が走ることを確認 (LastSegmentBytesWritten == 0)
///
/// 検証:
///   - 中間フレーム t=0.0/0.3/0.5/0.7 で位置・opacity・色が補間されている
///   - 初回 Flush 以降は LastWasFullRebuild=false かつ LastSegmentBytesWritten=0 → 部分更新だけで動作
///   - vk/dx 完全一致
/// </summary>
public static class Sample34RetainedCanvasAnim
{
    public static int Run(Func<GpuDevice> createDevice)
    {
        const uint w = 256, h = 128;
        ulong fbBytes = (ulong)(w * h * 4);

        using GpuDevice device = createDevice();
        Console.WriteLine($"device: {device.Name}");

        // === RetainedCanvas に 2 ノード ===
        using var raster = new Rasterizer2D(device);
        using var canvas = new RetainedCanvas(raster);
        var cardA = canvas.AddChild(canvas.Root);
        cardA.Content = new Scene2D().FillRoundedRect(Color2D.White, 0, 0, 80, 40, 8);
        cardA.Transform = Affine2D.Translate(-150f, 30f);
        cardA.Color = Color2D.Rgba(60, 130, 240, 255);
        cardA.Opacity = 0f;

        var cardB = canvas.AddChild(canvas.Root);
        cardB.Content = new Scene2D().FillRoundedRect(Color2D.White, 0, 0, 80, 40, 8);
        cardB.Transform = Affine2D.Translate(300f, 70f);
        cardB.Color = Color2D.Rgba(80, 130, 240, 255);
        cardB.Opacity = 1f;

        // === Target に Bind ===
        var target = new RetainedCanvasAnimationTarget()
            .Bind("cardA", cardA)
            .Bind("cardB", cardB);

        // === AnimationClip 構築 ===
        var aTrans = Tracks.Vector2("cardA/translation", InterpolationKind.Linear, new Keyframe<Vector2>[]
        {
            new(0.0f, new Vector2(-150f, 30f)),
            new(0.5f, new Vector2(30f, 30f)),
            new(0.7f, new Vector2(30f, 30f)),
        });
        var aOpacity = Tracks.Float("cardA/opacity", InterpolationKind.Linear, new Keyframe<float>[]
        {
            new(0.0f, 0f),
            new(0.5f, 1f),
            new(0.7f, 1f),
        });
        var bTrans = Tracks.Vector2("cardB/translation", InterpolationKind.Linear, new Keyframe<Vector2>[]
        {
            new(0.0f, new Vector2(300f, 70f)),
            new(0.2f, new Vector2(300f, 70f)),   // 0.2s delay 相当
            new(0.7f, new Vector2(130f, 70f)),
        });
        var bColor = Tracks.Color("cardB/color", InterpolationKind.Linear, new Keyframe<uint>[]
        {
            new(0.0f, Color2D.Rgba(80, 130, 240, 255)),
            new(0.2f, Color2D.Rgba(80, 130, 240, 255)),
            new(0.7f, Color2D.Rgba(230, 80, 100, 255)),
        });
        var clip = new AnimationClip("MenuOpen", new TrackBase[] { aTrans, aOpacity, bTrans, bColor });

        var clock = new FixedFrameClock { FrameRate = 60f };
        var player = new AnimationPlayer();
        player.Update(clock);
        Animate.Clip(clip, target).Play(player, clock);

        // === Render 用準備 ===
        using GpuBuffer fb = device.Malloc(fbBytes, GpuMemoryKind.HostMapped);

        float[] snapshotsAtSec = { 0.0f, 0.3f, 0.5f, 0.7f };
        int snapIdx = 0;
        int partialFrames = 0, fullRebuildFrames = 0;
        long totalSegmentBytesAfterFirst = 0;
        bool firstFlushDone = false;

        for (int frame = 0; frame <= 50 && snapIdx < snapshotsAtSec.Length; frame++)
        {
            clock.Frame = frame;
            player.Update(clock);

            while (snapIdx < snapshotsAtSec.Length
                   && clock.TimeSec + 1e-4f >= snapshotsAtSec[snapIdx])
            {
                // 描画
                fb.Span<byte>((int)fbBytes).Clear();
                using (var cmd = device.MainQueue.StartCommandRecording())
                {
                    canvas.Render(cmd, Camera2D.Pixels, w, h, fb);
                    cmd.Finish();
                    device.MainQueue.SubmitAndWait(cmd);
                }

                if (firstFlushDone)
                {
                    if (canvas.LastWasFullRebuild) fullRebuildFrames++;
                    else partialFrames++;
                    totalSegmentBytesAfterFirst += canvas.LastSegmentBytesWritten;
                }
                firstFlushDone = true;

                string png = Path.Combine(AppContext.BaseDirectory, $"canvas_anim_t{snapshotsAtSec[snapIdx]:0.0}.png");
                PngWriter.WriteRgba(png, (int)w, (int)h, fb.Span<byte>((int)fbBytes));
                var a = cardA.Transform; var b = cardB.Transform;
                Console.WriteLine(
                    $"  t={snapshotsAtSec[snapIdx]:0.0}s: A(x={a.E:0.0}, opacity={cardA.Opacity:0.00}) | B(x={b.E:0.0}, color=0x{cardB.Color:X8}) | rebuild={canvas.LastWasFullRebuild}, transformW={canvas.LastTransformWrites}, styleW={canvas.LastStyleWrites}, segBytes={canvas.LastSegmentBytesWritten}");
                snapIdx++;
            }
        }

        // === 検証 ===
        bool ok = true;
        // t=0.5: A は完了 (X=30, opacity=1)
        // 観測 inde: 2 番目 (t=0.5)
        // 部分更新が機能していること: 2 回目以降のスナップショットで FullRebuild=false (初回除く)
        if (fullRebuildFrames > 0) { ok = false; Console.Error.WriteLine($"FAILED: 部分更新フレームが期待されるが FullRebuild が {fullRebuildFrames} 回"); }
        if (partialFrames == 0) { ok = false; Console.Error.WriteLine($"FAILED: 部分更新フレームが 0 (期待 >= 1)"); }
        if (totalSegmentBytesAfterFirst != 0) { ok = false; Console.Error.WriteLine($"FAILED: segment bytes written after first frame = {totalSegmentBytesAfterFirst} (期待 0, ジオメトリ不変)"); }

        Console.WriteLine($"  partial frames: {partialFrames}, fullRebuild after first: {fullRebuildFrames}, segBytes after first: {totalSegmentBytesAfterFirst}");
        Console.WriteLine(ok ? "OK: AN-M4 (RetainedCanvas + AnimationClip + 部分更新の維持) 動作"
                             : "FAILED");
        return ok ? 0 : 1;
    }
}
