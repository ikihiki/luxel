using Luxel;
using Luxel.Typography;
using Luxel.TwoD;
using Luxel.UI;
using Luxel.UI.Styling;
using Luxel.UI.Tailwind;
using static Luxel.Controls.Kit;
using static Luxel.UI.Tailwind.S;

namespace Luxel.Samples;

/// <summary>
/// サンプル46 (TW-M4c): <c>Luxel.Controls.Switch</c> に StateStyle 引数 + Tailwind utility が波及。
/// <c>On(WidgetState.Checked, ...)</c> で「on 時のスタイル」を宣言。
/// </summary>
public static class Sample46TailwindSwitch
{
    public static int Run(Func<GpuDevice> createDevice)
    {
        const uint w = 480, h = 120;
        using GpuDevice device = createDevice();
        Console.WriteLine($"device: {device.Name}");

        using var raster = new Rasterizer2D(device);
        using var canvas = new RetainedCanvas(raster);
        using var font = VectorFont.LoadSystem();

        var isOn = new Signal<bool>(false);

        // 新 DSL
        var sw = Luxel.Controls.Kit.Switch(isOn,
            background: Tw.Slate300,
            parts: S.On(WidgetState.Checked, S.Bg(Tw.Green500)));

        var host = new UiHost(canvas, font, w, h);
        host.SetRoot(
            Border(background: Tw.Slate100, padding: new Thickness(40))[sw]
        );

        using GpuBuffer fb = device.Malloc(w * h * 4, GpuMemoryKind.HostMapped);

        void Snap(string name)
        {
            using GpuCommandBuffer cmd = device.MainQueue.StartCommandRecording();
            canvas.Render(cmd, Camera2D.Pixels, w, h, fb);
            cmd.Finish();
            device.MainQueue.SubmitAndWait(cmd);
            string png = Path.Combine(AppContext.BaseDirectory, $"tailwind_switch_{name}.png");
            PngWriter.WriteRgba(png, (int)w, (int)h, fb.Span<byte>((int)(w * h * 4)));
        }

        (byte r, byte g, byte b) Rgb(int x, int y)
        { var p = fb.Span<byte>((int)(w * h * 4)); int i = (y * (int)w + x) * 4; return (p[i], p[i+1], p[i+2]); }

        // Switch サイズ 46x26、padding 40 → track world 位置 x=40..86, y=40..66
        // off 時 knob は left (x=43..63), on 時 knob は right (x=63..83)
        // track 色を観測するなら knob と反対側
        // off: knob 左、track 右側 (75, 53) を観測
        // on:  knob 右、track 左側 (50, 53) を観測
        Snap("00_off");
        var off = Rgb(75, 53);
        Console.WriteLine($"  off: track={off} (expect Tw.Slate300=(203,213,225))");

        isOn.Value = true;
        Snap("01_on");
        var on = Rgb(50, 53);
        Console.WriteLine($"  on:  track={on} (expect Tw.Green500=(34,197,94))");

        isOn.Value = false;
        Snap("02_off_again");
        var off2 = Rgb(75, 53);

        bool ok = true;
        if (Math.Abs(off.r - 203) > 5 || Math.Abs(off.b - 225) > 5)
        { ok = false; Console.Error.WriteLine($"FAILED: off track not Slate300: {off}"); }
        if (Math.Abs(on.r - 34) > 5 || Math.Abs(on.g - 197) > 5)
        { ok = false; Console.Error.WriteLine($"FAILED: on track not Green500: {on}"); }
        if (off2 != off)
        { ok = false; Console.Error.WriteLine($"FAILED: toggle not idempotent: {off} != {off2}"); }

        Console.WriteLine(ok ? "OK: TW-M4c (Switch + utility + On(Checked,...)) 動作" : "FAILED");
        return ok ? 0 : 1;
    }
}
