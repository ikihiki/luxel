using Luxel;
using Luxel.Typography;
using Luxel.TwoD;
using Luxel.UI;
using Luxel.UI.Styling;
using Luxel.UI.Tailwind;
using static Luxel.Controls.Kit;

namespace Luxel.Samples;

/// <summary>
/// サンプル50 (TW-N1): 新 DSL の実証
///
/// <para><b>全てのプロパティを factory 引数で受ける</b>、状態別は <c>parts: p =&gt; p.On(state, ...)</c> ラムダで宣言。</para>
///
/// <code>
/// Button("OK", onClick,
///     background: Tw.Blue500, foreground: Tw.White,
///     rounded: 8, width: 180, height: 60,
///     parts: p => p
///         .On(WidgetState.Hover,   background: Tw.Red500, scale: 1.1f)
///         .On(WidgetState.Pressed, scale: 0.95f));
/// </code>
/// </summary>
public static class Sample50NewDsl
{
    public static int Run(Func<GpuDevice> createDevice)
    {
        const uint w = 480, h = 200;
        using GpuDevice device = createDevice();
        Console.WriteLine($"device: {device.Name}");

        using var raster = new Rasterizer2D(device);
        using var canvas = new RetainedCanvas(raster);
        using var font = VectorFont.LoadSystem();

        int clicked = 0;
        var host = new UiHost(canvas, font, w, h);
        host.SetRoot(
            Border(background: Tw.Slate100, padding: new Thickness(24))
            [
                Button(_ => clicked++, "Hover Me",
                    background: Tw.Blue500,
                    foreground: Tw.White,
                    rounded: 10,
                    width: 200, height: 100,
                    parts: [S.On(WidgetState.Hover, S.Bg(Tw.Red500), S.Scale(1.10f)),
                            S.On(WidgetState.Pressed, S.Scale(0.95f))])
            ]
        );

        using GpuBuffer fb = device.Malloc(w * h * 4, GpuMemoryKind.HostMapped);

        void Snap(string name)
        {
            using GpuCommandBuffer cmd = device.MainQueue.StartCommandRecording();
            canvas.Render(cmd, Camera2D.Pixels, w, h, fb);
            cmd.Finish();
            device.MainQueue.SubmitAndWait(cmd);
            string png = Path.Combine(AppContext.BaseDirectory, $"newdsl_{name}.png");
            PngWriter.WriteRgba(png, (int)w, (int)h, fb.Span<byte>((int)(w * h * 4)));
        }

        (byte r, byte g, byte b) Rgb(int x, int y)
        { var p = fb.Span<byte>((int)(w * h * 4)); int i = (y * (int)w + x) * 4; return (p[i], p[i+1], p[i+2]); }

        Snap("00_idle");
        var idle = Rgb(120, 60);
        Console.WriteLine($"  idle:  btn={idle} (expect Tw.Blue500=(59,130,246))");

        host.PointerMove(120, 60);
        Snap("01_hover");
        var hover = Rgb(120, 60);
        Console.WriteLine($"  hover: btn={hover} (expect Tw.Red500=(239,68,68) + scale 1.10)");

        host.PointerMove(0, 0);
        Snap("02_idle_back");
        var idle2 = Rgb(120, 60);

        bool ok = true;
        if (Math.Abs(idle.r - 59) > 5 || Math.Abs(idle.b - 246) > 5)
        { ok = false; Console.Error.WriteLine($"FAILED: idle not Blue500: {idle}"); }
        if (Math.Abs(hover.r - 239) > 5 || Math.Abs(hover.g - 68) > 5)
        { ok = false; Console.Error.WriteLine($"FAILED: hover not Red500: {hover}"); }
        if (idle2 != idle)
        { ok = false; Console.Error.WriteLine($"FAILED: idle not restored"); }

        Console.WriteLine(ok ? "OK: TW-N1 (新 DSL: factory 引数 + parts ラムダ) 動作" : "FAILED");
        return ok ? 0 : 1;
    }
}
