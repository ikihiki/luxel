using System.Numerics;
using Luxel;
using Luxel.Typography;
using Luxel.TwoD;

namespace Luxel.Samples;

/// <summary>
/// サンプル06: コンピュートベース 2D ベクターラスタライザ (M1)。
/// 塗り矩形と穴あきリング (EvenOdd) を GPU compute でラスタライズし、framebuffer バッファへ
/// 書き出して読み戻し、PNG 化 + ピクセル検証する。三角形分割は行わない (パスのまま GPU で塗る)。
/// </summary>
public static class Sample06Vector2D
{
    public static int Run(Func<GpuDevice> createDevice)
    {
        const uint w = 256, h = 256;
        using GpuDevice device = createDevice();
        Console.WriteLine($"device: {device.Name}");

        var scene = new Scene2D();
        scene.FillRect(Color2D.Green, 40, 40, 120, 80);          // 緑の矩形
        // 青のリング (ドーナツ): EvenOdd で 2 重円 → 中心が穴になる
        scene.BeginFill(Color2D.Blue, FillRule.EvenOdd);
        AddCircle(scene, 170, 160, 60);                          // 外円
        AddCircle(scene, 170, 160, 30);                          // 内円 (穴)
        scene.EndFill();
        scene.StrokeLine(Color2D.Rgba(240, 140, 30), 8, 20, 230, 110, 150);  // オレンジの線
        using (var font = VectorFont.LoadSystem())                            // ベクターテキスト
            font.AppendText(scene, "Luxel 2D", 12, 30, 26, Color2D.Rgba(20, 20, 40));

        using var rasterizer = new Rasterizer2D(device);
        using EncodedScene encoded = rasterizer.Encode(scene);
        using GpuBuffer fb = device.Malloc(w * h * 4, GpuMemoryKind.HostMapped);

        using GpuCommandBuffer cmd = device.MainQueue.StartCommandRecording();
        rasterizer.Render(cmd, encoded, Camera2D.Pixels, w, h, fb);
        cmd.Finish();
        device.MainQueue.SubmitAndWait(cmd);

        Span<byte> px = fb.Span<byte>((int)(w * h * 4));
        int RectI(int x, int y) => (y * (int)w + x) * 4;
        int ir = RectI(100, 80), ih = RectI(170, 160), ib = RectI(215, 160), ik = RectI(65, 190);
        var rect = (r: px[ir], g: px[ir + 1], b: px[ir + 2]);     // 緑矩形の内側
        var hole = (r: px[ih], g: px[ih + 1], b: px[ih + 2]);     // リングの穴 → 白背景
        var band = (r: px[ib], g: px[ib + 1], b: px[ib + 2]);     // リングの帯 → 青
        var strk = (r: px[ik], g: px[ik + 1], b: px[ik + 2]);     // ストローク線 → オレンジ
        Console.WriteLine($"rect=({rect.r},{rect.g},{rect.b}) hole=({hole.r},{hole.g},{hole.b}) band=({band.r},{band.g},{band.b}) stroke=({strk.r},{strk.g},{strk.b})");

        // テキストのインク (上部の帯, 背景以外) を数える
        int ink = 0;
        for (int yy = 4; yy <= 34; yy++)
            for (int xx = 10; xx <= 200; xx++)
            { int i = (yy * (int)w + xx) * 4; if (px[i] < 120) ink++; }
        Console.WriteLine($"テキストのインク画素: {ink}");

        PngWriter.WriteRgba(Path.Combine(AppContext.BaseDirectory, "vector2d.png"), (int)w, (int)h, px);
        int blueBase = CountBlue(px, (int)(w * h));

        // --- スムーズズーム: 同じ encoded シーンをカメラだけ変えて 2.5x 拡大 (再エンコードなし) ---
        var zoomCam = Camera2D.Create(2.5f, new Vector2(170, 160), w, h);  // リング中心を画面中心へ
        using GpuCommandBuffer cmd2 = device.MainQueue.StartCommandRecording();
        rasterizer.Render(cmd2, encoded, zoomCam, w, h, fb);
        cmd2.Finish();
        device.MainQueue.SubmitAndWait(cmd2);
        Span<byte> pz = fb.Span<byte>((int)(w * h * 4));
        int blueZoom = CountBlue(pz, (int)(w * h));
        PngWriter.WriteRgba(Path.Combine(AppContext.BaseDirectory, "vector2d_zoom.png"), (int)w, (int)h, pz);
        Console.WriteLine($"青ピクセル数: 1x={blueBase}, 2.5x={blueZoom} (再エンコードなしで拡大)");

        bool ok = rect.g > 140 && rect.r < 100                         // 矩形=緑
               && hole.r > 230 && hole.g > 230 && hole.b > 230         // 穴=白背景
               && band.b > 180 && band.r < 120                         // 帯=青
               && strk.r > 180 && strk.b < 110                         // ストローク=オレンジ
               && ink > 80                                            // ベクターテキストが描かれている
               && blueZoom > blueBase * 3;                             // 2.5x で面積が拡大
        Console.WriteLine(ok ? "OK: ベクター塗り + スムーズズーム (再エンコードなし) を確認"
                             : "FAILED: ベクター塗り/ズームの結果が想定と異なります");
        return ok ? 0 : 1;
    }

    private static int CountBlue(ReadOnlySpan<byte> px, int pixels)
    {
        int n = 0;
        for (int i = 0; i < pixels; i++)
            if (px[i * 4] < 120 && px[i * 4 + 2] > 180) n++;   // 青っぽい
        return n;
    }

    private static void AddCircle(Scene2D s, float cx, float cy, float r, int segments = 64)
    {
        for (int i = 0; i < segments; i++)
        {
            float t = i / (float)segments * MathF.Tau;
            float x = cx + MathF.Cos(t) * r, y = cy + MathF.Sin(t) * r;
            if (i == 0) s.MoveTo(x, y); else s.LineTo(x, y);
        }
        s.Close();
    }
}
