using System.Numerics;
using Luxel;
using Luxel.Typography;
using Luxel.TwoD;

namespace Luxel.Samples;

/// <summary>
/// サンプル07: 地図風ベクターシーン (日本語ラベル) を GPU コンピュートラスタライザで描画し、
/// 同一エンコードのままカメラだけ変えて拡大 (スムーズズーム) する。map.png / map_zoom.png 出力。
/// </summary>
public static class Sample07Map
{
    public static int Run(Func<GpuDevice> createDevice)
    {
        const uint w = 512, h = 384;
        using GpuDevice device = createDevice();
        Console.WriteLine($"device: {device.Name}");

        var s = new Scene2D();
        // 背景 (ベージュ)
        s.FillRect(Color2D.Rgba(238, 236, 228), 0, 0, w, h);
        // 川 (青の太いストローク) と湖
        uint water = Color2D.Rgba(150, 190, 230);
        s.StrokePolyline(water, 26,
            new Vector2(-10, 90), new Vector2(120, 140), new Vector2(260, 120),
            new Vector2(400, 180), new Vector2(530, 150));
        s.FillCircle(water, 440, 305, 55);
        // 公園 (緑の角丸)
        s.FillRoundedRect(Color2D.Rgba(180, 215, 160), 40, 215, 150, 120, 18);
        // 街区 (灰色の矩形グリッド)
        uint block = Color2D.Rgba(222, 220, 212);
        for (int gx = 0; gx < 4; gx++)
            for (int gy = 0; gy < 2; gy++)
                s.FillRect(block, 230 + gx * 70, 220 + gy * 75, 56, 60);
        // 道路: 太い幹線 (灰の縁取り→白) と細い道
        uint roadEdge = Color2D.Rgba(200, 200, 196), road = Color2D.White;
        Vector2[] main = { new(40, 60), new(200, 110), new(360, 90), new(500, 140) };
        s.StrokePolyline(roadEdge, 18, main);
        s.StrokePolyline(road, 12, main);
        Vector2[] main2 = { new(150, 360), new(180, 200), new(300, 120) };
        s.StrokePolyline(roadEdge, 16, main2);
        s.StrokePolyline(road, 10, main2);
        for (int i = 0; i < 5; i++)
            s.StrokeLine(Color2D.Rgba(228, 226, 218), 4, 230, 215 + i * 38, 510, 215 + i * 38);

        // 日本語ラベル + ラテン
        using (var jp = VectorFont.LoadSystemJapanese())
        {
            jp.AppendText(s, "中央公園", 60, 285, 22, Color2D.Rgba(60, 95, 55));
            jp.AppendText(s, "東川", 250, 110, 24, Color2D.Rgba(45, 85, 130));
            jp.AppendText(s, "本町", 250, 255, 20, Color2D.Rgba(60, 60, 60));
            jp.AppendText(s, "みなと湖", 392, 300, 20, Color2D.Rgba(45, 85, 130));
        }
        using (var en = VectorFont.LoadSystem())
            en.AppendText(s, "Luxel Map", 16, 30, 24, Color2D.Rgba(90, 90, 100));

        using var raster = new Rasterizer2D(device);
        using EncodedScene encoded = raster.Encode(s);
        using GpuBuffer fb = device.Malloc(w * h * 4, GpuMemoryKind.HostMapped);

        // ベース
        Render(device, raster, encoded, Camera2D.Pixels, w, h, fb);
        Span<byte> p0 = fb.Span<byte>((int)(w * h * 4));
        int content = CountNonBg(p0, (int)(w * h));
        PngWriter.WriteRgba(Path.Combine(AppContext.BaseDirectory, "map.png"), (int)w, (int)h, p0);

        // 2.5x ズーム (再エンコードなし)
        Render(device, raster, encoded, Camera2D.Create(2.5f, new Vector2(300, 150), w, h), w, h, fb);
        Span<byte> p1 = fb.Span<byte>((int)(w * h * 4));
        PngWriter.WriteRgba(Path.Combine(AppContext.BaseDirectory, "map_zoom.png"), (int)w, (int)h, p1);

        Console.WriteLine($"非背景ピクセル: {content} / {w * h}");
        Console.WriteLine($"PNG: map.png / map_zoom.png");
        bool ok = content > (int)(w * h) / 10;   // 十分な内容が描かれている
        Console.WriteLine(ok ? "OK: 地図風シーン + 日本語ラベル + スムーズズームを描画"
                             : "FAILED: 地図シーンの内容が不足");
        return ok ? 0 : 1;
    }

    private static void Render(GpuDevice device, Rasterizer2D raster, EncodedScene scene,
        Camera2D cam, uint w, uint h, GpuBuffer fb)
    {
        using GpuCommandBuffer cmd = device.MainQueue.StartCommandRecording();
        raster.Render(cmd, scene, cam, w, h, fb);
        cmd.Finish();
        device.MainQueue.SubmitAndWait(cmd);
    }

    private static int CountNonBg(ReadOnlySpan<byte> px, int pixels)
    {
        int n = 0;
        for (int i = 0; i < pixels; i++)
            if (px[i * 4] < 235 || px[i * 4 + 1] < 233 || px[i * 4 + 2] < 225) n++;  // ベージュ背景以外
        return n;
    }
}
