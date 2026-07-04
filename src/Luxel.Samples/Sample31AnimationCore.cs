using System.Numerics;
using Luxel;
using Luxel.Animation;
using Luxel.Animation.UI;
using Luxel.TwoD;
using Luxel.UI;

namespace Luxel.Samples;

/// <summary>
/// サンプル31 (AN-M1): Animation core 最小デモ。
///
/// (1) `Signal&lt;Vector2&gt;` (位置) と `Signal&lt;uint&gt;` (色) を Animatable で駆動。
/// (2) `AnimationPlayer.Tick(dt)` を毎フレーム呼び、値が時間で変化することを確認。
/// (3) 1 秒のアニメ (60 fps × 60 フレーム) のうち 4 フレーム (t=0, 0.33s, 0.66s, 完了) を PNG 出力し、
///     画素配置 + 色を検証。
/// (4) vk/dx で同一の数値を生成する (animation core は CPU 駆動なので両 backend で完全一致)。
///
/// 設計確認:
///   - Animatable&lt;T&gt; = Curve.Eval(t01) → Tween.Lerp(progress) の 2 段分解 (Flutter 流)
///   - Signal&lt;T&gt; に直接書込み (`SignalAnimationTarget.For(signal)` でラップ)
///   - frame-driven (#2 設計決定)
/// </summary>
public static class Sample31AnimationCore
{
    public static int Run(Func<GpuDevice> createDevice)
    {
        const uint w = 256, h = 128;
        ulong fbBytes = (ulong)(w * h * 4);

        using GpuDevice device = createDevice();
        Console.WriteLine($"device: {device.Name}");

        // === Signal + Animatable ===
        var position = new Signal<Vector2>(new Vector2(20, 40));
        var color    = new Signal<uint>(Color2D.Rgba(220, 80, 80, 255));

        var posAnim = new Animatable<Vector2>
        {
            Curve = CubicBezierCurve.EaseInOut,
            Tween = new Vector2Tween(new Vector2(20, 40), new Vector2(180, 40)),
            Duration = 1.0f,
        };
        var colorAnim = new Animatable<uint>
        {
            Curve = LinearCurve.Instance,
            Tween = new RgbaTween(Color2D.Rgba(220, 80, 80, 255), Color2D.Rgba(80, 130, 240, 255)),
            Duration = 1.0f,
        };

        // 固定 FPS の整数フレーム時計。frame * (1/60) を毎回計算するので累積誤差ゼロ。
        var clock = new FixedFrameClock { FrameRate = 60f };
        var player = new AnimationPlayer();
        player.Update(clock);   // _lastTime を t=0 に同期
        player.Play(posAnim,   SignalAnimationTarget.For(position), clock);
        player.Play(colorAnim, SignalAnimationTarget.For(color), clock);

        // === ラスタライザ ===
        using var raster = new Rasterizer2D(device);
        using GpuBuffer fb = device.Malloc(fbBytes, GpuMemoryKind.HostMapped);

        // 4 つのスナップショット時刻 (秒): 0.00 / 0.33 / 0.66 / 1.00
        float[] snapshotsAtSec = { 0.00f, 0.33f, 0.66f, 1.00f };
        var observed = new List<(Vector2 Pos, uint Color)>();

        // FixedFrameClock を進めて player.Update(clock) を呼ぶ。
        // 各 snapshot 時刻に対応する frame 番号を事前計算 (整数で完全一致)。
        int snapIdx = 0;
        for (int frame = 0; frame <= 60 && snapIdx < snapshotsAtSec.Length; frame++)
        {
            clock.Frame = frame;
            player.Update(clock);

            while (snapIdx < snapshotsAtSec.Length
                   && clock.TimeSec + 1e-4f >= snapshotsAtSec[snapIdx])
            {
                // 値を読み (Signal.Peek で依存追跡せず)
                var p = position.Peek();
                var c = color.Peek();
                observed.Add((p, c));

                // 矩形 1 個のシーンを描いて PNG 出力
                var scene = new Scene2D();
                scene.FillRoundedRect(c, p.X, p.Y, 50, 40, 8);
                using var encoded = raster.Encode(scene);
                fb.Span<byte>((int)fbBytes).Clear();
                using (var cmd = device.MainQueue.StartCommandRecording())
                {
                    raster.Render(cmd, encoded, Camera2D.Pixels, w, h, fb);
                    cmd.Finish();
                    device.MainQueue.SubmitAndWait(cmd);
                }
                string png = Path.Combine(AppContext.BaseDirectory, $"animation_t{snapshotsAtSec[snapIdx]:0.00}.png");
                PngWriter.WriteRgba(png, (int)w, (int)h, fb.Span<byte>((int)fbBytes));
                Console.WriteLine($"  t={snapshotsAtSec[snapIdx]:0.00}s: pos=({p.X:0.0},{p.Y:0.0}), color=0x{c:X8}, PNG={Path.GetFileName(png)}");
                snapIdx++;
            }
        }

        // 絶対時刻モデルなので余分な tick は不要。frame=60 で TimeSec=60/60=1.0 ピッタリ、
        // track の StartTime=0 + Duration=1.0 = EndTime=1.0 で確実に完了する。

        // === 検証 ===
        bool ok = true;
        // t=0: 開始値
        if (Math.Abs(observed[0].Pos.X - 20f) > 1f) { ok = false; Console.Error.WriteLine($"FAILED: start pos.X expected 20, got {observed[0].Pos.X}"); }
        // t=1.0: 完了値
        if (Math.Abs(observed[3].Pos.X - 180f) > 1f) { ok = false; Console.Error.WriteLine($"FAILED: end pos.X expected 180, got {observed[3].Pos.X}"); }
        // 単調増加
        for (int i = 1; i < 4; i++)
        {
            if (observed[i].Pos.X < observed[i - 1].Pos.X - 1f) { ok = false; Console.Error.WriteLine($"FAILED: pos.X not monotonic at i={i}"); }
        }
        // 色も補間されている (R 減少, B 増加)
        int r0 = (int)(observed[0].Color & 0xff);
        int r3 = (int)(observed[3].Color & 0xff);
        int b0 = (int)((observed[0].Color >> 16) & 0xff);
        int b3 = (int)((observed[3].Color >> 16) & 0xff);
        if (r3 >= r0 - 50) { ok = false; Console.Error.WriteLine($"FAILED: R did not decrease ({r0} -> {r3})"); }
        if (b3 <= b0 + 50) { ok = false; Console.Error.WriteLine($"FAILED: B did not increase ({b0} -> {b3})"); }
        // Player が空になっている (両アニメ完了)
        if (player.ActiveCount != 0) { ok = false; Console.Error.WriteLine($"FAILED: player ActiveCount expected 0, got {player.ActiveCount}"); }

        Console.WriteLine(ok ? "OK: AN-M1 (Curve + Tween + Animatable + Signal target + Player) 動作"
                             : "FAILED");
        return ok ? 0 : 1;
    }
}
