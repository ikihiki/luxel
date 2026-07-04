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
/// サンプル45 (TW-M4b): <c>Luxel.Controls.CheckBox</c> に StateStyle 引数 + Tailwind utility が波及。
/// <c>WidgetState.Checked</c> で「checked 時のスタイル」を宣言できる (Tailwind の <c>checked:</c> prefix と等価)。
/// (注: Luxel.Samples 内に sample 11 用の別 CheckBox があるため完全修飾名で参照)
///
/// <code>
/// var cb = Luxel.Controls.Kit.Check(sig, "Subscribe");
/// cb.ApplyParts([Bg(Tw.Slate300), On(WidgetState.Checked, Bg(Tw.Blue500))]);
/// </code>
/// </summary>
public static class Sample45TailwindCheckBox
{
    public static int Run(Func<GpuDevice> createDevice)
    {
        const uint w = 480, h = 200;
        using GpuDevice device = createDevice();
        Console.WriteLine($"device: {device.Name}");

        using var raster = new Rasterizer2D(device);
        using var canvas = new RetainedCanvas(raster);
        using var font = VectorFont.LoadSystem();

        var isChecked = new Signal<bool>(false);

        // 新 DSL (factory 引数 + 状態レイヤ parts)
        var cb = Luxel.Controls.Kit.Check(isChecked, "Subscribe",
            background: Tw.Slate300,
            foreground: Tw.Slate900,
            parts: S.On(WidgetState.Checked, S.Bg(Tw.Blue500), S.Fg(Tw.Slate900)));

        var host = new UiHost(canvas, font, w, h);
        host.SetRoot(cb);

        using GpuBuffer fb = device.Malloc(w * h * 4, GpuMemoryKind.HostMapped);

        void Snap(string name)
        {
            using GpuCommandBuffer cmd = device.MainQueue.StartCommandRecording();
            canvas.Render(cmd, Camera2D.Pixels, w, h, fb);
            cmd.Finish();
            device.MainQueue.SubmitAndWait(cmd);
            string png = Path.Combine(AppContext.BaseDirectory, $"tailwind_cb_{name}.png");
            PngWriter.WriteRgba(png, (int)w, (int)h, fb.Span<byte>((int)(w * h * 4)));
        }

        (byte r, byte g, byte b) Rgb(int x, int y)
        { var p = fb.Span<byte>((int)(w * h * 4)); int i = (y * (int)w + x) * 4; return (p[i], p[i+1], p[i+2]); }

        // CheckBox を root に直接配置 → CheckBox.Size は constrain (480, 200) に拡張される
        // box は CheckBox 内 (0, boxY)..(20, boxY+20), boxY = (200-20)/2 = 90
        // box 中心: (10, 100)
        int bx = 10, by = 100;

        // === unchecked ===
        Snap("00_unchecked");
        var ucBox = Rgb(bx, by);
        Console.WriteLine($"  unchecked: box={ucBox} (expect Tw.Slate300 ≈ (203,213,225))");

        // === toggle (programmatic) ===
        isChecked.Value = true;
        Snap("01_checked");
        var cBox = Rgb(bx, by);
        // box 中心は check 印ストロークの AA fringe にかかる ─ 反対側 (4, 94) で box の真の色を取る
        var cBoxEdge = Rgb(4, 94);
        Console.WriteLine($"  checked:   box_center={cBox} (AA fringe), box_edge={cBoxEdge} (true color, expect Blue500=(59,130,246))");

        // === unchecked に戻し ===
        isChecked.Value = false;
        Snap("02_unchecked_again");
        var ucBox2 = Rgb(bx, by);
        Console.WriteLine($"  unchecked2: box={ucBox2}");

        // === 検証 ===
        bool ok = true;
        // unchecked = Slate300 (203, 213, 225)
        if (Math.Abs(ucBox.r - 203) > 5 || Math.Abs(ucBox.b - 225) > 5)
        { ok = false; Console.Error.WriteLine($"FAILED: unchecked box not Slate300: {ucBox}"); }
        // checked = Blue500 (59, 130, 246) at edge (away from check stroke)
        if (Math.Abs(cBoxEdge.r - 59) > 5 || Math.Abs(cBoxEdge.b - 246) > 5)
        { ok = false; Console.Error.WriteLine($"FAILED: checked box edge not Blue500: {cBoxEdge}"); }
        // idempotent: 戻したら unchecked と一致
        if (ucBox2 != ucBox)
        { ok = false; Console.Error.WriteLine($"FAILED: toggle not idempotent: {ucBox} != {ucBox2}"); }

        Console.WriteLine(ok ? "OK: TW-M4b (CheckBox + utility + On(Checked,...) で状態切替) 動作"
                             : "FAILED");
        return ok ? 0 : 1;
    }
}
