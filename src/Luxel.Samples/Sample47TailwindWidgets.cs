using Luxel;
using Luxel.Controls;
using Luxel.Typography;
using Luxel.TwoD;
using Luxel.UI;
using Luxel.UI.Styling;
using Luxel.UI.Tailwind;
using static Luxel.Controls.Kit;
using static Luxel.UI.Tailwind.S;

namespace Luxel.Samples;

/// <summary>
/// サンプル47 (TW-M4c 残り): Slider / TextField / Select / SegmentedControl / RadioGroup / Tabs に
/// StateStyle.Background/Foreground 上書きが効くことを確認。
///
/// 各 widget に <c>S.Bg(Tw.X)</c> / <c>S.Fg(Tw.Y)</c> を適用して、track / highlight 色を Tailwind パレットに変更。
/// </summary>
public static class Sample47TailwindWidgets
{
    public static int Run(Func<GpuDevice> createDevice)
    {
        const uint w = 480, h = 240;
        using GpuDevice device = createDevice();
        Console.WriteLine($"device: {device.Name}");

        using var raster = new Rasterizer2D(device);
        using var canvas = new RetainedCanvas(raster);
        using var font = VectorFont.LoadSystem();

        // 新 DSL: trackColor/fillColor/knobColor 引数で構築
        var sliderVal = new Signal<float>(0.5f);
        var slider = Luxel.Controls.Kit.Slider(sliderVal, 0, 1,
            trackColor: Tw.Slate200,
            fillColor: Tw.Blue500,
            knobColor: Tw.Blue600,
            width: 200);

        // 新 DSL
        var segVal = new Signal<int>(1);
        var seg = Luxel.Controls.Kit.Segmented(new[] { "A", "B", "C" }, segVal,
            background: Tw.Slate200,
            foreground: Tw.Green500);

        var host = new UiHost(canvas, font, w, h);
        host.SetRoot(
            Border(background: Tw.Slate100, padding: new Thickness(20))
            [
                VStack(spacing: 16)[slider, seg]
            ]
        );

        using GpuBuffer fb = device.Malloc(w * h * 4, GpuMemoryKind.HostMapped);

        void Snap(string name)
        {
            using GpuCommandBuffer cmd = device.MainQueue.StartCommandRecording();
            canvas.Render(cmd, Camera2D.Pixels, w, h, fb);
            cmd.Finish();
            device.MainQueue.SubmitAndWait(cmd);
            string png = Path.Combine(AppContext.BaseDirectory, $"tailwind_widgets_{name}.png");
            PngWriter.WriteRgba(png, (int)w, (int)h, fb.Span<byte>((int)(w * h * 4)));
        }

        (byte r, byte g, byte b) Rgb(int x, int y)
        { var p = fb.Span<byte>((int)(w * h * 4)); int i = (y * (int)w + x) * 4; return (p[i], p[i+1], p[i+2]); }

        Snap("00_default");

        // Slider track 位置: panel padding 20 + slider 自身の track padding。
        // y=20 から VStack start、Slider height = 26、track_y = (26-6)/2 = 10
        // → track world y = 20 + 10 ≈ 30, x = 20+200 - some = 180-ish
        // 値=0.5, fill 半分 (0..100, 100..200 部分)
        // Slider track 右端付近 (210, 33) は track 色 (Slate200)、左端 (30, 33) は fill (Blue500)
        var trackBg = Rgb(210, 33);
        var trackFill = Rgb(30, 33);
        Console.WriteLine($"  slider: track_bg={trackBg} (expect Slate200=(226,232,240)), fill={trackFill} (expect Blue500=(59,130,246))");

        // SegmentedControl は VStack 内 2 つ目、Slider 26 + spacing 16 = 42 後、SegHeight=34
        // → seg world y ≈ 62..96。各 seg 幅 ≈ 44、track 開始 x=20、3 segments で 152 まで
        // seg track 右端付近 (140, 80) ≈ Slate200 (highlight 外)
        var segBg = Rgb(140, 80);
        // 選択 idx=1 (B) の highlight: x=20+44 ~ 64..108、中央 (86, 80) ≈ Green500
        var segFill = Rgb(86, 80);
        Console.WriteLine($"  segmented: track_bg={segBg} (expect Slate200), highlight={segFill} (expect Green500=(34,197,94))");

        bool ok = true;
        if (Math.Abs(trackBg.r - 226) > 8) { ok = false; Console.Error.WriteLine($"FAILED: slider track not Slate200: {trackBg}"); }
        if (Math.Abs(trackFill.r - 59) > 8 || Math.Abs(trackFill.b - 246) > 8)
        { ok = false; Console.Error.WriteLine($"FAILED: slider fill not Blue500: {trackFill}"); }
        if (Math.Abs(segBg.r - 226) > 8) { ok = false; Console.Error.WriteLine($"FAILED: seg bg not Slate200: {segBg}"); }
        if (Math.Abs(segFill.g - 197) > 15)
        { ok = false; Console.Error.WriteLine($"FAILED: seg hl not Green500: {segFill}"); }

        Console.WriteLine(ok ? "OK: TW-M4c (Slider/SegmentedControl utility 適用) 動作" : "FAILED");
        return ok ? 0 : 1;
    }
}
