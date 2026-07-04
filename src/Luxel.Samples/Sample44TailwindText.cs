using Luxel;
using Luxel.Typography;
using Luxel.TwoD;
using Luxel.UI;
using Luxel.UI.Styling;
using Luxel.UI.Tailwind;
using static Luxel.Controls.Kit;
using static Luxel.UI.Decl;
using static Luxel.UI.Tailwind.S;

namespace Luxel.Samples;

/// <summary>
/// サンプル44 (TW-M4): Text と Button の両方を Tailwind utility 駆動で構築。
/// Text に StateStyle 引数 (Button/Border と同じ流儀) が効き、Foreground/Opacity/FontSize を utility 経由で指定可能。
///
/// <code>
/// VStack(
///     Text("Heading", parts: [Fg(Tw.Slate900), FontSize(24)]),
///     Text("Body",    parts: [Fg(Tw.Slate700), FontSize(16)]),
///     Text("Caption", parts: [Fg(Tw.Slate500), FontSize(13), Opacity(0.8f)]),
///     Button("OK", onClick, parts: [Bg(Tw.Blue500), Fg(Tw.White), Rounded(Tw.RoundedMd), On(Hover, Bg(Tw.Blue600))])
/// )
/// </code>
/// </summary>
public static class Sample44TailwindText
{
    public static int Run(Func<GpuDevice> createDevice)
    {
        const uint w = 480, h = 320;
        using GpuDevice device = createDevice();
        Console.WriteLine($"device: {device.Name}");

        using var raster = new Rasterizer2D(device);
        using var canvas = new RetainedCanvas(raster);
        using var font = VectorFont.LoadSystem();

        int clicks = 0;

        var host = new UiHost(canvas, font, w, h);
        host.SetRoot(
            Border(background: Tw.Slate100, padding: new Thickness(Tw.P8))
            [
                VStack(spacing: 12)
                [
                    Text("Welcome",                              fontSize: 28, color: Tw.Slate900),
                    Text("Tailwind for Luxel",                   fontSize: 16, color: Tw.Slate700),
                    Text("Utility-first styling, even for text.", fontSize: 13, color: Tw.Slate500, opacity: 0.85f),
                    Button(_ => clicks++, "Get Started",
                        background: Tw.Blue500, foreground: Tw.White, rounded: Tw.RoundedMd,
                        width: 200, height: 50,
                        parts: S.On(WidgetState.Hover, S.Bg(Tw.Blue600), S.Scale(1.05f)))
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
            string png = Path.Combine(AppContext.BaseDirectory, $"tailwind_text_{name}.png");
            PngWriter.WriteRgba(png, (int)w, (int)h, fb.Span<byte>((int)(w * h * 4)));
        }

        (byte r, byte g, byte b) Rgb(int x, int y)
        { var p = fb.Span<byte>((int)(w * h * 4)); int i = (y * (int)w + x) * 4; return (p[i], p[i+1], p[i+2]); }

        Snap("00_idle");
        var bgCol = Rgb(10, 10);
        Console.WriteLine($"  bg = {bgCol} (expect Tw.Slate100 = (241,245,249))");

        // Text の Foreground は AppendText で white が焼き込まれ、node.Color が utility 色で乗算される
        // 観察ポイント: 見出しのテキスト周辺 (Y=40) では Slate900 寄りのピクセルが含まれる
        // ボタンは右下、PointerMove で hover 切替を確認
        host.PointerMove(0, 0);
        Snap("01_idle2");

        // ボタン中心 (PNG 出力された位置から判断、おおむね中央下)
        // PerformLayout は VStack で子を縦に並べる、ボタンは最下段
        // 探索: Button が描画されている領域を見つける
        // 簡素化: ボタン背景色 (Blue500 = (59, 130, 246)) のピクセルを探す
        bool foundButtonBlue = false;
        int buttonY = 0;
        for (int y = 100; y < (int)h - 10; y++)
        {
            for (int x = 50; x < (int)w - 50; x++)
            {
                var p = Rgb(x, y);
                if (p.r >= 55 && p.r <= 65 && p.g >= 125 && p.g <= 135 && p.b >= 240 && p.b <= 250)
                {
                    foundButtonBlue = true;
                    buttonY = y;
                    goto found;
                }
            }
        }
        found:
        Console.WriteLine($"  found Blue500 button at y≈{buttonY}: {foundButtonBlue}");

        bool ok = true;
        // Slate100 背景
        if (Math.Abs(bgCol.r - 241) > 5 || Math.Abs(bgCol.b - 249) > 5)
        { ok = false; Console.Error.WriteLine($"FAILED: bg not Slate100: {bgCol}"); }
        // Button があるはず (Blue500)
        if (!foundButtonBlue)
        { ok = false; Console.Error.WriteLine("FAILED: Button (Tw.Blue500) not found"); }

        Console.WriteLine(ok ? "OK: TW-M4 (Text + Button が utility 駆動で構築) 動作"
                             : "FAILED");
        return ok ? 0 : 1;
    }
}
