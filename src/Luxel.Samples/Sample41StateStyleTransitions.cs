using Luxel;
using Luxel.Animation;
using Luxel.Animation.UI;
using Luxel.Typography;
using Luxel.TwoD;
using Luxel.UI;
using Luxel.UI.Styling;
using static Luxel.UI.Tailwind.S;
using static Luxel.Controls.Kit;
using static Luxel.UI.Decl;

namespace Luxel.Samples;

/// <summary>
/// サンプル41: 状態レイヤ (On) + <see cref="TransitionFactory"/> パーツで状態変化が自動補間される。
/// Tailwind / CSS の "transition" モデル + 関数引数完結 API。
///
/// <code>
/// var btn = Button(_ => {}, "Hover Me",
///     background: blue, foreground: white,
///     parts: [On(WidgetState.Hover, Bg(red), Scale(1.15f)),
///             fx.Background(0.30f, CubicBezierCurve.EaseInOut),
///             fx.Scale(0.20f, CubicBezierCurve.EaseOut)]);
/// </code>
///
/// hover 起動 (PointerMove) で blue→red, scale 1.0→1.15 が独立に補間。
/// 複数 frame で snapshot → 中間値で観測、vk/dx 完全一致。
/// </summary>
public static class Sample41StateStyleTransitions
{
    public static int Run(Func<GpuDevice> createDevice)
    {
        const uint w = 480, h = 200;
        using GpuDevice device = createDevice();
        Console.WriteLine($"device: {device.Name}");

        using var raster = new Rasterizer2D(device);
        using var canvas = new RetainedCanvas(raster);
        using var font = VectorFont.LoadSystem();

        uint panel = Color2D.Rgba(245, 246, 250);
        uint blue  = Color2D.Rgba(60, 120, 210);
        uint red   = Color2D.Rgba(230, 80, 100);
        uint white = Color2D.White;

        var clock = new FixedFrameClock { FrameRate = 60f };
        var player = new AnimationPlayer();

        // 事前 setup された TransitionFactory: メソッド呼出しで IConfigPart を生成して parts: に渡す
        var fx = new TransitionFactory(player, clock);

        var btn = Button(_ => {}, "Hover Me",
            background: blue, foreground: white, rounded: 10,
            width: 200, height: 100,
            parts: [
                On(WidgetState.Hover, Bg(red), Scale(1.15f)),
                fx.Background(0.30f, CubicBezierCurve.EaseInOut),
                fx.Scale     (0.20f, CubicBezierCurve.EaseOut),
            ]);

        var host = new UiHost(canvas, font, w, h);
        host.SetRoot(
            Border(background: panel, padding: new Thickness(24))
            [
                Grid(columns: [1], rows: [GridLength.Star(1)])[btn]
            ]
        );

        using GpuBuffer fb = device.Malloc(w * h * 4, GpuMemoryKind.HostMapped);

        void Snap(string name)
        {
            using GpuCommandBuffer cmd = device.MainQueue.StartCommandRecording();
            canvas.Render(cmd, Camera2D.Pixels, w, h, fb);
            cmd.Finish();
            device.MainQueue.SubmitAndWait(cmd);
            string png = Path.Combine(AppContext.BaseDirectory, $"transitions_{name}.png");
            PngWriter.WriteRgba(png, (int)w, (int)h, fb.Span<byte>((int)(w * h * 4)));
        }

        (byte r, byte g, byte b) Rgb(int x, int y)
        { var p = fb.Span<byte>((int)(w * h * 4)); int i = (y * (int)w + x) * 4; return (p[i], p[i+1], p[i+2]); }

        // === 初期: idle ===
        player.Update(clock);
        Snap("00_idle");
        var idle = Rgb(120, 60);
        Console.WriteLine($"  t=0.00 (idle):  {idle}");

        // === hover 起動 → 補間開始 ===
        host.PointerMove(120, 60);   // Reactive.Effect 同期発火 → player にエントリ追加

        // === 補間中の snapshot ===
        var snaps = new List<(float t, (byte r, byte g, byte b) rgb)>();
        int[] snapFrames = { 6, 12, 18, 24 };   // t = 0.10, 0.20, 0.30, 0.40 (60fps)
        int snapIdx = 0;
        for (int f = 1; f <= 30; f++)
        {
            clock.Frame = f;
            player.Update(clock);
            if (snapIdx < snapFrames.Length && f == snapFrames[snapIdx])
            {
                Snap($"{(snapIdx + 1):D2}_t{clock.TimeSec:0.00}");
                var rgb = Rgb(120, 60);
                snaps.Add((clock.TimeSec, rgb));
                Console.WriteLine($"  t={clock.TimeSec:0.00}:        {rgb}");
                snapIdx++;
            }
        }

        // === 検証 ===
        bool ok = true;
        // idle = blue
        if (idle.b < 180 || idle.r > 120) { ok = false; Console.Error.WriteLine($"FAILED: idle not blue: {idle}"); }
        // t=0.30 以降 = red
        var final = snaps[2];   // t=0.30
        if (final.rgb.r < 180 || final.rgb.b > 130) { ok = false; Console.Error.WriteLine($"FAILED: t=0.30 not red: {final.rgb}"); }
        // t=0.10 = 中間 (blue でも red でも完全に一致しない)
        var mid = snaps[0];
        if (mid.rgb == idle) { ok = false; Console.Error.WriteLine("FAILED: t=0.10 should differ from idle (transition not started)"); }
        if (mid.rgb.r > 180) { ok = false; Console.Error.WriteLine($"FAILED: t=0.10 already red (no easing): {mid.rgb}"); }
        // 単調変化: R は時間と共に増加 (blue r=60 → red r=230)
        for (int i = 1; i < 3; i++)
        {
            if (snaps[i].rgb.r < snaps[i - 1].rgb.r)
            { ok = false; Console.Error.WriteLine($"FAILED: R should monotonically increase, got {snaps[i-1].rgb.r}→{snaps[i].rgb.r} at i={i}"); }
        }

        Console.WriteLine(ok ? "OK: TW-M2 (StateStyle + TransitionSet 自動補間) 動作"
                             : "FAILED");
        return ok ? 0 : 1;
    }
}
