using Luxel;
using Luxel.Controls;
using Luxel.Typography;
using Luxel.TwoD;
using Luxel.UI;
using static Luxel.Controls.Kit;

namespace Luxel.Samples;

/// <summary>
/// サンプル97: SurfaceView (iframe 相当)。子 UiHost の UI を親キャンバスに画像として埋め込み、
/// 入力ブリッジ (クリック) と子の再描画伝搬をオフスクリーンで検証する。
/// 子: 青背景 + ボタン (押すと背景が赤 + カウント増)。親のクリックが子ボタンに届き、
/// 子の変更が親のフレームに反映されることをピクセルとツリーで確認する。
/// </summary>
public static class Sample97SurfaceView
{
    public static int Run(Func<GpuDevice> createDevice)
    {
        const uint w = 260, h = 180;
        using GpuDevice device = createDevice();
        Console.WriteLine($"device: {device.Name}");
        using VectorFont font = VectorFont.LoadSystem();
        using var raster = new Rasterizer2D(device);
        using var canvas = new RetainedCanvas(raster);
        using var host = new UiHost(canvas, font, w, h);

        // ---- 子 UI: 背景 signal + カウンタボタン ----
        var bg = new Signal<uint>(Color2D.Rgba(200, 220, 255));   // 初期=淡青
        var count = new Signal<int>(0);
        Widget childRoot = Border(background: bg, padding: new Thickness(10))[
            Center()[VStack(8)[
                Text($"count: {count}", 14, color: Color2D.Rgba(20, 20, 40)),
                Button(_ => { count.Value++; bg.Value = Color2D.Rgba(230, 60, 60); }, "hit me")]]];

        using SurfaceView surface = SurfaceView(200, 120);
        surface.SetContent(childRoot);

        // ---- 親 UI: 白ボーダーの中に SurfaceView (10,10) ----
        host.SetRoot(Border(background: Color2D.White, padding: new Thickness(10))[surface]);

        using GpuBuffer fb = device.Malloc(w * h * 4, GpuMemoryKind.HostMapped);
        void Frame() { host.Tick(1f / 60f); RenderOnce(device, canvas, w, h, fb); }
        for (int i = 0; i < 3; i++) Frame();   // 子の初回 render + 合成

        byte[] p0 = Read(fb, w, h);
        var inSurf = Rgb(p0, w, 30, 30);       // SurfaceView 内 = 子の淡青背景
        var outSurf = Rgb(p0, w, 240, 160);    // SurfaceView 外 = 親の白
        PngWriter.WriteRgba(Path.Combine(AppContext.BaseDirectory, "sample97_before.png"), (int)w, (int)h, p0);

        // ---- 子ボタンの位置をツリーから取得し、親座標でクリック ----
        DebugNode? btn = Find(surface.Child!.DebugSnapshot(), "Button");
        if (btn is null) { Console.WriteLine("FAILED: 子ツリーに Button が見つからない"); return 1; }
        float px = 10 + btn.X + btn.W / 2, py = 10 + btn.Y + btn.H / 2;   // 親 (10,10) + 子内座標
        host.PointerDown(px, py);
        host.PointerUp(px, py);
        for (int i = 0; i < 3; i++) Frame();   // 子再描画 → 親へ伝搬

        byte[] p1 = Read(fb, w, h);
        var after = Rgb(p1, w, 30, 30);        // 子背景が赤に変わったはず
        string countText = Find(surface.Child!.DebugSnapshot(), "Text")?.Detail ?? "";
        PngWriter.WriteRgba(Path.Combine(AppContext.BaseDirectory, "sample97_after.png"), (int)w, (int)h, p1);

        Console.WriteLine($"in={inSurf} out={outSurf} after={after} countText='{countText}'");
        bool embedOk = inSurf.b > 240 && inSurf.r < 220 && outSurf is { r: > 245, g: > 245, b: > 245 };
        bool clickOk = countText == "count: 1";
        bool propagateOk = after.r > 200 && after.b < 100;   // 淡青 → 赤
        bool ok = embedOk && clickOk && propagateOk;
        Console.WriteLine($"embed={embedOk} click={clickOk} propagate={propagateOk}");
        Console.WriteLine(ok ? "OK: SurfaceView (埋め込み表示 / クリック転送 / 子変更の伝搬)"
                             : "FAILED: SurfaceView の結果が想定と異なります");
        return ok ? 0 : 1;
    }

    private static DebugNode? Find(DebugNode? n, string type)
    {
        if (n is null) return null;
        if (n.Type == type) return n;
        foreach (DebugNode c in n.Children)
            if (Find(c, type) is { } found) return found;
        return null;
    }

    private static void RenderOnce(GpuDevice device, RetainedCanvas canvas, uint w, uint h, GpuBuffer fb)
    {
        using GpuCommandBuffer cmd = device.MainQueue.StartCommandRecording();
        canvas.Render(cmd, Camera2D.Pixels, w, h, fb);
        cmd.Finish();
        device.MainQueue.SubmitAndWait(cmd);
    }

    private static byte[] Read(GpuBuffer fb, uint w, uint h)
    {
        byte[] copy = new byte[w * h * 4];
        fb.Span<byte>((int)(w * h * 4)).CopyTo(copy);
        return copy;
    }

    private static (byte r, byte g, byte b) Rgb(ReadOnlySpan<byte> px, uint w, int x, int y)
    { int i = (y * (int)w + x) * 4; return (px[i], px[i + 1], px[i + 2]); }
}
