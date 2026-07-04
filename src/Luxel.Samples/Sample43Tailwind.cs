using Luxel;
using Luxel.Typography;
using Luxel.TwoD;
using Luxel.UI;
using Luxel.UI.Styling;
using Luxel.UI.Tailwind;
using static Luxel.Controls.Kit;
using static Luxel.UI.Decl;
using static Luxel.UI.Tailwind.S;        // Bg / Fg / Rounded / Scale / On / ...

namespace Luxel.Samples;

/// <summary>
/// サンプル43 (TW-M5): <b>Luxel.UI.Tailwind</b> 別アセンブリの utility (S.*) + token (Tw.*) で
/// Button を宣言。Tailwind の <c>class="bg-blue-500 hover:bg-red-500 hover:scale-105"</c> に対応:
///
/// <code>
/// using static Luxel.UI.Tailwind.S;
///
/// Button("OK", onClick, parts: [
///     Bg(Tw.Blue500), Fg(Tw.White), Rounded(Tw.RoundedMd), W(180), H(80),
///     On(WidgetState.Hover,   Bg(Tw.Red500), Scale(1.10f)),
///     On(WidgetState.Pressed, Scale(0.95f)),
/// ]);
/// </code>
///
/// 関数引数 (style/hover/...) を一切使わず、utility だけで完結。
/// </summary>
public static class Sample43Tailwind
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
            Border(background: Tw.Slate100, padding: new Thickness(Tw.P6))
            [
                Grid(columns: [1], rows: [GridLength.Star(1)])
                [
                    // utility (S.*) と token (Tw.*) だけで Button を構築
                    Button(_ => clicked++, "Tailwind",
                        background: Tw.Blue500, foreground: Tw.White, rounded: Tw.RoundedLg,
                        width: 180, height: 80,
                        parts: [S.On(WidgetState.Hover, S.Bg(Tw.Red500), S.Scale(1.10f)),
                                S.On(WidgetState.Pressed, S.Scale(0.92f))])
                ]
            ]
        );

        using GpuBuffer fb = device.Malloc(w * h * 4, GpuMemoryKind.HostMapped);

        void Snap(string name)
        {
            using GpuCommandBuffer cmd = device.MainQueue.StartCommandRecording();
            canvas.Render(cmd, Camera2D.Pixels, w, h, fb);
            cmd.Finish();
            device.MainQueue.SubmitAndWait(cmd);
            string png = Path.Combine(AppContext.BaseDirectory, $"tailwind_{name}.png");
            PngWriter.WriteRgba(png, (int)w, (int)h, fb.Span<byte>((int)(w * h * 4)));
        }

        (byte r, byte g, byte b) Rgb(int x, int y)
        { var p = fb.Span<byte>((int)(w * h * 4)); int i = (y * (int)w + x) * 4; return (p[i], p[i+1], p[i+2]); }

        // === 初期 idle ===
        Snap("00_idle");
        var btnIdle = Rgb(100, 60);
        var bgIdle  = Rgb(10, 10);
        Console.WriteLine($"  idle:  btn={btnIdle}, bg={bgIdle}");

        // === hover (ボタン中心) ===
        host.PointerMove(100, 60);
        Snap("01_hover");
        var btnHover = Rgb(100, 60);
        Console.WriteLine($"  hover: btn={btnHover}");

        // === 領域外 (戻し) ===
        host.PointerMove(0, 0);
        Snap("02_idle_again");
        var btnIdle2 = Rgb(100, 60);
        Console.WriteLine($"  idle2: btn={btnIdle2}");

        // === 検証 ===
        bool ok = true;
        // Tw.Blue500 = (59, 130, 246)
        if (Math.Abs(btnIdle.r - 59) > 5 || Math.Abs(btnIdle.b - 246) > 5)
        { ok = false; Console.Error.WriteLine($"FAILED: idle btn not Tw.Blue500: {btnIdle}"); }
        // Tw.Red500 = (239, 68, 68)
        if (Math.Abs(btnHover.r - 239) > 5 || Math.Abs(btnHover.g - 68) > 5)
        { ok = false; Console.Error.WriteLine($"FAILED: hover btn not Tw.Red500: {btnHover}"); }
        // Tw.Slate100 = (241, 245, 249)
        if (Math.Abs(bgIdle.r - 241) > 5 || Math.Abs(bgIdle.b - 249) > 5)
        { ok = false; Console.Error.WriteLine($"FAILED: bg not Tw.Slate100: {bgIdle}"); }
        // 戻し idempotent
        if (btnIdle2 != btnIdle)
        { ok = false; Console.Error.WriteLine($"FAILED: idle not restored: {btnIdle} != {btnIdle2}"); }

        Console.WriteLine(ok ? "OK: TW-M5 (Luxel.UI.Tailwind utility + token + state variant) 動作"
                             : "FAILED");
        return ok ? 0 : 1;
    }
}
