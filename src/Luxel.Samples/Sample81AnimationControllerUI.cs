using Luxel;
using Luxel.Typography;
using Luxel.TwoD;
using Luxel.UI;
using Luxel.Controls;
using Luxel.UI.Styling;
using static Luxel.Controls.Kit;
using static Luxel.UI.Decl;

namespace Luxel.Samples;

/// <summary>
/// Sample 81: AnimationController の UI Widget を実描画 + PNG 検証。
/// PlaybackState 相当の Signal 群 (time / speed / playing) と Play/Stop Button を
/// 実際に画面にレンダリング → PNG。UI 操作 (Click) で state → UI 反映を検証。
/// </summary>
public static class Sample81AnimationControllerUI
{
    public static int Run(Func<GpuDevice> createDevice)
    {
        const uint w = 480, h = 320;
        Console.WriteLine("=== Sample 81: AnimationController Widget 実描画 + PNG ===");
        using GpuDevice device = createDevice();
        Console.WriteLine($"device: {device.Name}");

        using var raster = new Rasterizer2D(device);
        using var canvas = new RetainedCanvas(raster);
        using var font = VectorFont.LoadSystem();

        uint panel = Color2D.Rgba(245, 246, 250), dark = Color2D.Rgba(30, 33, 40);
        uint blue = Color2D.Rgba(60, 120, 210), blueHi = Color2D.Rgba(95, 160, 245);
        uint gray = Color2D.Rgba(100, 116, 139), grayHi = Color2D.Rgba(130, 146, 169);

        // PlaybackState 相当 (Signal<int> で構築、Sample 10 の count と同じスタイル)
        var time = new Signal<int>(0);       // ミリ秒
        var speed = new Signal<int>(100);    // %
        var playing = new Signal<int>(0);    // 0/1

        var host = new UiHost(canvas, font, w, h);
        host.SetRoot(
            Border(background: panel, padding: new Thickness(16))
            [
                Grid(columns: [1, 1], rows: [GridLength.Px(70), GridLength.Star(1)])
                [
                    Text($"time {time} speed {speed} playing {playing}", 22, color: dark,
                        parts: [P.Grid.Row(0), P.Grid.ColumnSpan(2)]),
                    Button(_ => playing.Value = 1 - playing.Value, "Play/Pause",
                        background: blue, foreground: Luxel.TwoD.Color2D.White,
                        parts: [S.On(WidgetState.Hover, S.Bg(blueHi)), P.Grid.Column(0), P.Grid.Row(1)]),
                    Button(_ => Stop, "Stop"(playing, time),
                        background: gray, foreground: Luxel.TwoD.Color2D.White,
                        parts: [S.On(WidgetState.Hover, S.Bg(grayHi)), P.Grid.Column(1), P.Grid.Row(1)])
                ]
            ]
        );

        using GpuBuffer fb = device.Malloc(w * h * 4, GpuMemoryKind.HostMapped);
        void Render()
        {
            using var cmd = device.MainQueue.StartCommandRecording();
            canvas.Render(cmd, Camera2D.Pixels, w, h, fb);
            cmd.Finish();
            device.MainQueue.SubmitAndWait(cmd);
        }

        // 初期描画
        Render();
        var p0 = fb.Span<byte>((int)(w * h * 4)).ToArray();
        string png0 = Path.Combine(AppContext.BaseDirectory, "anim_widget_00_initial.png");
        PngWriter.WriteRgba(png0, (int)w, (int)h, p0);
        int uiPix0 = CountNonBgPixels(p0, w * h, 245, 246, 250, 30);
        Console.WriteLine($"  UI 要素 (初期): {uiPix0} ピクセル");

        // Signal 値変更 (Slider 想定)
        time.Value = 1250;
        speed.Value = 200;
        Render();
        var p1 = fb.Span<byte>((int)(w * h * 4)).ToArray();
        PngWriter.WriteRgba(Path.Combine(AppContext.BaseDirectory, "anim_widget_01_state_changed.png"), (int)w, (int)h, p1);

        // Play ボタンクリック
        int playingBefore = playing.Value;
        bool clicked = host.Click(120, 105);  // Column 0 Row 1 Button
        int playingAfter = playing.Value;
        Render();
        var p2 = fb.Span<byte>((int)(w * h * 4)).ToArray();
        PngWriter.WriteRgba(Path.Combine(AppContext.BaseDirectory, "anim_widget_02_after_click.png"), (int)w, (int)h, p2);

        // Stop ボタンクリック
        host.Click(360, 105);
        int timeAfterStop = time.Value;
        Render();
        var p3 = fb.Span<byte>((int)(w * h * 4)).ToArray();
        PngWriter.WriteRgba(Path.Combine(AppContext.BaseDirectory, "anim_widget_03_after_stop.png"), (int)w, (int)h, p3);

        long d01 = ByteDiff(p0, p1), d12 = ByteDiff(p1, p2), d23 = ByteDiff(p2, p3);
        Console.WriteLine($"  Play click hit: {clicked}, playing: {playingBefore}→{playingAfter}");
        Console.WriteLine($"  Stop click: time now {timeAfterStop}");
        Console.WriteLine($"  diff: state={d01}, click={d12}, stop={d23}");

        bool ok = uiPix0 > (int)(w * h) / 30 && d01 > 500 && clicked && playingAfter != playingBefore;
        Console.WriteLine(ok ? "OK: DEMO-M6 (AnimationController Widget 実描画 + Click + Signal 反映) 動作"
                              : "FAILED");
        return ok ? 0 : 1;
    }

    private static int CountNonBgPixels(byte[] px, uint total, byte r, byte g, byte b, int threshold)
    {
        int c = 0;
        for (int i = 0; i < total; i++)
        {
            int d = Math.Abs(px[i * 4] - r) + Math.Abs(px[i * 4 + 1] - g) + Math.Abs(px[i * 4 + 2] - b);
            if (d > threshold) c++;
        }
        return c;
    }
    private static void Stop(Signal<int> playing, Signal<int> time)
    { playing.Value = 0; time.Value = 0; }

    private static long ByteDiff(byte[] a, byte[] b)
    {
        long s = 0;
        for (int i = 0; i < a.Length; i++) s += Math.Abs(a[i] - b[i]);
        return s;
    }
}
