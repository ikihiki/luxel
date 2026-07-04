using Luxel;
using Luxel.Typography;
using Luxel.TwoD;
using Luxel.UI;
using Luxel.Controls;
using Luxel.UI.Styling;
using S = Luxel.UI.Tailwind.S;
using static Luxel.Controls.Kit;
using static Luxel.UI.Decl;

namespace Luxel.Samples;

/// <summary>
/// サンプル40: Button の Bindable 引数 + 状態レイヤ (On) で状態別スタイルを宣言的に指定。
///
/// <code>
/// Button("Hover Me", onClick,
///     background: blue, foreground: white, rounded: 10,
///     parts: [On(WidgetState.Hover, Bg(red), Scale(1.1f)),
///             On(WidgetState.Pressed, Scale(0.95f))]);
/// </code>
///
/// 関数引数で全部完結。Theme / Variant / Intent を一切経由しない。
/// PointerMove で hover signal を発火 → Reactive.Effect で recolor + transform を部分更新。
/// vk/dx ピクセル一致を確認。
/// </summary>
public static class Sample40StateStyleButton
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
        uint green = Color2D.Rgba(40, 180, 110);
        uint orange = Color2D.Rgba(240, 140, 50);
        uint white = Color2D.White;

        int clickedA = 0, clickedB = 0;

        var host = new UiHost(canvas, font, w, h);
        host.SetRoot(
            Border(background: panel, padding: new Thickness(24))
            [
                Grid(columns: [1, 1], rows: [GridLength.Star(1)])
                [
                    Button(_ => clickedA++, "Hover A",
                        background: blue, foreground: white, rounded: 10,
                        width: 180, height: 100,
                        parts: [S.On(WidgetState.Hover, S.Bg(red), S.Scale(1.10f)),
                                S.On(WidgetState.Pressed, S.Scale(0.95f)),
                                P.Grid.Column(0)]),
                    Button(_ => clickedB++, "Hover B",
                        background: green, foreground: white, rounded: 10,
                        width: 180, height: 100,
                        parts: [S.On(WidgetState.Hover, S.Bg(orange), S.Opacity(0.6f)),
                                P.Grid.Column(1)])
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
            string png = Path.Combine(AppContext.BaseDirectory, $"statestyle_{name}.png");
            PngWriter.WriteRgba(png, (int)w, (int)h, fb.Span<byte>((int)(w * h * 4)));
        }

        (byte r, byte g, byte b) Rgb(int x, int y)
        { var p = fb.Span<byte>((int)(w * h * 4)); int i = (y * (int)w + x) * 4; return (p[i], p[i+1], p[i+2]); }

        // --- 初期 (hover なし) ---
        Snap("00_idle");
        var aIdle = Rgb(100, 60);   // A 中央付近
        var bIdle = Rgb(320, 60);   // B 中央付近
        Console.WriteLine($"  idle:    A={aIdle}, B={bIdle}");

        // --- A 上に hover ---
        host.PointerMove(100, 60);
        Snap("01_hoverA");
        var aHoverA = Rgb(100, 60);
        var bHoverA = Rgb(320, 60);
        Console.WriteLine($"  hoverA:  A={aHoverA}, B={bHoverA}");

        // --- B 上に hover (A は idle へ戻る) ---
        host.PointerMove(320, 60);
        Snap("02_hoverB");
        var aHoverB = Rgb(100, 60);
        var bHoverB = Rgb(320, 60);
        Console.WriteLine($"  hoverB:  A={aHoverB}, B={bHoverB}");

        // --- 領域外 (どちらも idle) ---
        host.PointerMove(0, 0);
        Snap("03_idle2");
        var aIdle2 = Rgb(100, 60);
        var bIdle2 = Rgb(320, 60);
        Console.WriteLine($"  idle2:   A={aIdle2}, B={bIdle2}");

        // === 検証 ===
        bool ok = true;
        // A: idle = 青 (b 大), hover = 赤 (r 大)
        if (aIdle.b < 150 || aIdle.r > 120) { ok = false; Console.Error.WriteLine("FAILED: A idle is not blue"); }
        if (aHoverA.r < 180 || aHoverA.b > 130) { ok = false; Console.Error.WriteLine("FAILED: A hover is not red"); }
        // A は B hover 中は idle (青) に戻っているはず
        if (aHoverB.b < 150 || aHoverB.r > 120) { ok = false; Console.Error.WriteLine("FAILED: A is not blue when B is hovered"); }

        // B: idle = 緑 (g 大), hover = 橙 + opacity 0.6 (やや薄い)
        if (bIdle.g < 150 || bIdle.r > 100) { ok = false; Console.Error.WriteLine("FAILED: B idle is not green"); }
        if (bHoverB.r < 130 || bHoverB.g < 80) { ok = false; Console.Error.WriteLine("FAILED: B hover is not orange-ish"); }
        // B は A hover 中は idle (緑) に戻っているはず
        if (bHoverA.g < 150 || bHoverA.r > 100) { ok = false; Console.Error.WriteLine("FAILED: B is not green when A is hovered"); }

        // 戻った状態が初期と一致
        if (aIdle2 != aIdle || bIdle2 != bIdle) { ok = false; Console.Error.WriteLine("FAILED: returning to idle did not restore initial colors"); }

        Console.WriteLine(ok ? "OK: TW-M1 (StateStyle 引数で hover/pressed) 動作"
                             : "FAILED");
        return ok ? 0 : 1;
    }
}
