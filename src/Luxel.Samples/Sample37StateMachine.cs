using System.Numerics;
using Luxel;
using Luxel.Animation;
using Luxel.Animation.TwoD;
using Luxel.TwoD;

namespace Luxel.Samples;

/// <summary>
/// サンプル37 (AN-M6b): StateMachine による状態遷移。
///
/// 2 つの状態 idle/jump を持つ:
///   idle: card は静止 (y=40 固定、青)
///   jump: card が y を 40→0 (上昇) してすぐ戻る、色は黄色
///
/// 0.4s 時点で Trigger("press") → idle → jump (CrossfadeSec=0.15)
/// 1.0s 時点で Trigger("done")  → jump → idle (CrossfadeSec=0.15)
///
/// 4 フレーム (t=0.0 idle / 0.45 transitioning / 0.7 jump / 1.4 back-to-idle) を観測、vk/dx 一致。
/// </summary>
public static class Sample37StateMachine
{
    public static int Run(Func<GpuDevice> createDevice)
    {
        const uint w = 256, h = 128;
        ulong fbBytes = (ulong)(w * h * 4);

        using GpuDevice device = createDevice();
        Console.WriteLine($"device: {device.Name}");

        // === Clip 構築 ===
        var idleClip = new AnimationClip("idle", new TrackBase[]
        {
            Tracks.Float("card/translationY", InterpolationKind.Linear, new Keyframe<float>[]
            {
                new(0.0f, 40f), new(1.0f, 40f),
            }),
            Tracks.Color("card/color", InterpolationKind.Linear, new Keyframe<uint>[]
            {
                new(0.0f, Color2D.Rgba(60, 130, 240, 255)),
                new(1.0f, Color2D.Rgba(60, 130, 240, 255)),
            }),
        });
        var jumpClip = new AnimationClip("jump", new TrackBase[]
        {
            Tracks.Float("card/translationY", InterpolationKind.Linear, new Keyframe<float>[]
            {
                new(0.0f, 40f),
                new(0.15f, 0f),
                new(0.30f, 40f),
                new(0.60f, 40f),
            }),
            Tracks.Color("card/color", InterpolationKind.Linear, new Keyframe<uint>[]
            {
                new(0.0f, Color2D.Rgba(255, 200, 60, 255)),
                new(0.6f, Color2D.Rgba(255, 200, 60, 255)),
            }),
        });

        // === RetainedCanvas + 1 ノード ===
        using var raster = new Rasterizer2D(device);
        using var canvas = new RetainedCanvas(raster);
        var card = canvas.AddChild(canvas.Root);
        card.Content = new Scene2D().FillRoundedRect(Color2D.White, 0, 0, 80, 50, 10);
        card.Transform = Affine2D.Translate(80, 40);
        card.Color = Color2D.Rgba(60, 130, 240, 255);
        card.Opacity = 1f;

        var target = new RetainedCanvasAnimationTarget().Bind("card", card);

        // === StateMachine 構築 ===
        var idle = new State("idle", new ClipNode(idleClip));
        var jump = new State("jump", new ClipNode(jumpClip));
        idle.AddTransition("press", jump, crossfadeSec: 0.15f);
        jump.AddTransition("done",  idle, crossfadeSec: 0.15f);

        var sm = new StateMachine(target).AddState(idle).AddState(jump).SetInitial(idle);

        var clock = new FixedFrameClock { FrameRate = 60f };
        sm.Start(clock);

        using GpuBuffer fb = device.Malloc(fbBytes, GpuMemoryKind.HostMapped);

        // 経過時間ごとの Trigger スケジュール
        bool triggeredPress = false, triggeredDone = false;
        float[] snapshotsAtSec = { 0.0f, 0.45f, 0.7f, 1.4f };
        int snapIdx = 0;
        var observed = new List<(float Y, uint Color, string StateName)>();

        for (int frame = 0; frame <= 100 && snapIdx < snapshotsAtSec.Length; frame++)
        {
            clock.Frame = frame;
            // Trigger イベントを時刻で発火
            if (!triggeredPress && clock.TimeSec >= 0.4f)
            {
                sm.Trigger("press", clock);
                triggeredPress = true;
            }
            if (!triggeredDone && clock.TimeSec >= 1.0f)
            {
                sm.Trigger("done", clock);
                triggeredDone = true;
            }
            sm.Tick(clock);

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
                var t = card.Transform;
                observed.Add((t.F, card.Color, sm.Current?.Name ?? "(none)"));
                string png = Path.Combine(AppContext.BaseDirectory, $"sm_t{snapshotsAtSec[snapIdx]:0.00}.png");
                PngWriter.WriteRgba(png, (int)w, (int)h, fb.Span<byte>((int)fbBytes));
                Console.WriteLine($"  t={snapshotsAtSec[snapIdx]:0.00}s [{sm.Current?.Name ?? "?"}{(sm.IsTransitioning ? " (transitioning)" : "")}]: y={t.F:0.0}, color=0x{card.Color:X8}");
                snapIdx++;
            }
        }

        // === 検証 ===
        bool ok = true;
        // t=0.0: idle, y=40, blue
        if (observed[0].StateName != "idle") { ok = false; Console.Error.WriteLine($"FAILED: t=0 state should be idle"); }
        if ((int)((observed[0].Color >> 16) & 0xff) < 200) { ok = false; Console.Error.WriteLine($"FAILED: t=0 blue"); }
        // t=0.7: jump, 黄色寄り
        if (observed[2].StateName != "jump") { ok = false; Console.Error.WriteLine($"FAILED: t=0.7 state should be jump"); }
        if ((int)(observed[2].Color & 0xff) < 200) { ok = false; Console.Error.WriteLine($"FAILED: t=0.7 red component should be high (yellow)"); }
        // t=1.4: idle 復帰
        if (observed[3].StateName != "idle") { ok = false; Console.Error.WriteLine($"FAILED: t=1.4 state should be idle"); }

        Console.WriteLine(ok ? "OK: AN-M6b (StateMachine + Trigger + Crossfade) 動作"
                             : "FAILED");
        return ok ? 0 : 1;
    }
}
