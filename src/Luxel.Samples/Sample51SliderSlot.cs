using Luxel;
using Luxel.Typography;
using Luxel.TwoD;
using Luxel.UI;
using Luxel.UI.Styling;
using Luxel.UI.Tailwind;
using Luxel.Controls;
using static Luxel.Controls.Kit;

namespace Luxel.Samples;

/// <summary>
/// サンプル51 (TW-N3): Slot system demo。<c>SliderSlot.Knob(()=&gt;widget)</c> で
/// 内部の knob primitive を任意の widget に上書き。<c>Func&lt;Widget&gt;</c> は List の DataTemplate 相当。
///
/// <code>
/// Kit.Slider(value, trackColor: Tw.Slate200, fillColor: Tw.Blue500)[
///     SliderSlot.Knob(() => Border(background: () => Tw.Red500, rounded: 4)),
/// ]
/// </code>
/// </summary>
public static class Sample51SliderSlot
{
    public static int Run(Func<GpuDevice> createDevice)
    {
        const uint w = 320, h = 120;
        using GpuDevice device = createDevice();
        Console.WriteLine($"device: {device.Name}");

        using var raster = new Rasterizer2D(device);
        using var canvas = new RetainedCanvas(raster);
        using var font = VectorFont.LoadSystem();

        var v = new Signal<float>(0.5f);

        // Slider に slot 上書き: knob を赤い四角に
        var slider = Kit.Slider(v, 0, 1,
            trackColor: Tw.Slate200, fillColor: Tw.Blue500,
            width: 220)
        [
            SliderSlot.Knob(() => Border(
                background: Tw.Red500,
                rounded: 4,
                width: 18, height: 26))
        ];

        var host = new UiHost(canvas, font, w, h);
        host.SetRoot(
            Border(background: Tw.Slate100, padding: new Thickness(40))[slider]
        );

        using GpuBuffer fb = device.Malloc(w * h * 4, GpuMemoryKind.HostMapped);

        void Snap(string name)
        {
            using GpuCommandBuffer cmd = device.MainQueue.StartCommandRecording();
            canvas.Render(cmd, Camera2D.Pixels, w, h, fb);
            cmd.Finish();
            device.MainQueue.SubmitAndWait(cmd);
            string png = Path.Combine(AppContext.BaseDirectory, $"slider_slot_{name}.png");
            PngWriter.WriteRgba(png, (int)w, (int)h, fb.Span<byte>((int)(w * h * 4)));
        }

        (byte r, byte g, byte b) Rgb(int x, int y)
        { var p = fb.Span<byte>((int)(w * h * 4)); int i = (y * (int)w + x) * 4; return (p[i], p[i+1], p[i+2]); }

        // slider track world: x=40..260, y=50..76 (h=26 + padding 40)
        // value=0.5 → knob center x = 40 + 9 + 0.5*(220-18) = 40+9+101 = 150
        // knob は赤 (Tw.Red500=239,68,68) なら override 成功
        Snap("00");
        var knob = Rgb(150, 63);
        Console.WriteLine($"  knob: {knob} (expect Red500=(239,68,68) when slot override works)");

        bool ok = Math.Abs(knob.r - 239) <= 5 && Math.Abs(knob.g - 68) <= 5;
        Console.WriteLine(ok ? "OK: TW-N3 (Slot system: SliderSlot.Knob(()=>Border(...))) 動作"
                              : "FAILED: knob slot override 不発");
        return ok ? 0 : 1;
    }
}
