using Luxel;
using Luxel.Typography;
using Luxel.TwoD;

namespace Luxel.Samples;

/// <summary>
/// サンプル09: 保持型 (retained) UI ツリー + 部分更新。
/// ツリーを構築→描画 (retained_before.png)、ノードの移動・色変更だけ行って再描画
/// (retained_after.png)。移動/色変更は変換/スタイルの小さな書込のみで Segment 不変 (検証)。
/// クリップ (AABB) と描画順も確認。
/// </summary>
public static class Sample09RetainedUi
{
    public static int Run(Func<GpuDevice> createDevice)
    {
        const uint w = 480, h = 320;
        using GpuDevice device = createDevice();
        Console.WriteLine($"device: {device.Name}");

        uint accent = Color2D.Rgba(70, 110, 200), gray = Color2D.Rgba(200, 200, 205);
        uint green = Color2D.Rgba(70, 180, 90), red = Color2D.Rgba(220, 60, 60);

        using var raster = new Rasterizer2D(device);
        using var canvas = new RetainedCanvas(raster);

        // パネル (白) — root の子
        UiNode panel = RectNode(canvas, canvas.Root, 40, 40, 400, 240, Color2D.Rgba(250, 250, 252), 16);
        // タイトルバー (accent) — パネルの子 (local 0,0)
        RectNode(canvas, panel, 0, 0, 400, 40, accent, 16);
        // クリップ付きリスト領域 (パネル子)。子の行が幅を超えてもクリップされる。
        UiNode list = RectNode(canvas, panel, 20, 60, 160, 90, Color2D.Rgba(238, 240, 245), 0);
        list.Clip = new RectClip(0, 0, 160, 90);                       // local クリップ矩形
        RectNode(canvas, list, 0, 10, 300, 26, green, 0);              // 幅300 → クリップ(160)で切れる
        RectNode(canvas, list, 0, 46, 300, 26, Color2D.Rgba(120, 160, 230), 0);
        // ボタン2つ (パネル子)
        UiNode button1 = RectNode(canvas, panel, 250, 190, 90, 32, accent, 8);
        UiNode button2 = RectNode(canvas, panel, 60, 190, 90, 32, gray, 8);
        // テキスト
        using (var jp = VectorFont.LoadSystemJapanese())
        {
            TextNode(canvas, panel, jp, "設定パネル", 16, 28, 22, Color2D.White);
            TextNode(canvas, button1, jp, "ＯＫ", 26, 22, 16, Color2D.White);
        }

        using GpuBuffer fb = device.Malloc(w * h * 4, GpuMemoryKind.HostMapped);

        // --- 初期描画 (フル構築) ---
        RenderOnce(device, canvas, w, h, fb);
        Console.WriteLine($"[build] fullRebuild={canvas.LastWasFullRebuild} segBytes={canvas.LastSegmentBytesWritten}");
        Span<byte> p0 = fb.Span<byte>((int)(w * h * 4));
        var b1Before = Rgb(p0, w, 370, 255);     // button1 (OK 文字を避けた位置, accent)
        var clipIn = Rgb(p0, w, 130, 120);       // クリップ内 (green 行1)
        var clipOut = Rgb(p0, w, 330, 120);      // クリップ外 (行は切れて背景)
        PngWriter.WriteRgba(Path.Combine(AppContext.BaseDirectory, "retained_before.png"), (int)w, (int)h, p0);

        // --- 部分更新: button1 を赤に、button2 を上へ移動 (ジオメトリ不変) ---
        button1.Color = red;
        button2.Transform = Affine2D.Translate(60, 150);
        RenderOnce(device, canvas, w, h, fb);
        Console.WriteLine($"[update] fullRebuild={canvas.LastWasFullRebuild} segBytes={canvas.LastSegmentBytesWritten} " +
                          $"tfWrites={canvas.LastTransformWrites} styleWrites={canvas.LastStyleWrites}");
        Span<byte> p1 = fb.Span<byte>((int)(w * h * 4));
        var b1After = Rgb(p1, w, 370, 255);      // button1 (now red)
        var b2NewCenter = Rgb(p1, w, 145, 206);  // button2 移動後の中心 (gray)
        var b2OldCenter = Rgb(p1, w, 145, 246);  // button2 旧位置 → パネル白
        PngWriter.WriteRgba(Path.Combine(AppContext.BaseDirectory, "retained_after.png"), (int)w, (int)h, p1);

        Console.WriteLine($"button1: {b1Before} -> {b1After};  button2 new={b2NewCenter} old={b2OldCenter}");
        Console.WriteLine($"clip in={clipIn} out={clipOut}");

        bool partialOk = !canvas.LastWasFullRebuild && canvas.LastSegmentBytesWritten == 0
                       && canvas.LastTransformWrites >= 1 && canvas.LastStyleWrites >= 1;
        bool recolorOk = b1After.r > 180 && b1After.b < 120;                 // 赤になった
        bool moveOk = b2NewCenter.r is > 180 and < 230 && b2OldCenter.r > 230; // 移動先 gray / 旧位置 白
        bool clipOk = clipIn.g > 140 && clipIn.r < 120 && clipOut.r > 220;    // 内=緑, 外=背景

        bool ok = partialOk && recolorOk && moveOk && clipOk;
        Console.WriteLine($"partial={partialOk} recolor={recolorOk} move={moveOk} clip={clipOk}");
        Console.WriteLine(ok ? "OK: 保持型ツリー + 部分更新 (移動/色=Segment不変) + クリップ + 順序を確認"
                             : "FAILED: 保持型/部分更新の結果が想定と異なります");
        return ok ? 0 : 1;
    }

    private static UiNode RectNode(RetainedCanvas c, UiNode parent, float x, float y, float w, float h,
        uint color, float radius)
    {
        UiNode n = c.AddChild(parent);
        n.Transform = Affine2D.Translate(x, y);
        n.Color = color;
        var s = new Scene2D();
        if (radius > 0) s.FillRoundedRect(color, 0, 0, w, h, radius);
        else s.FillRect(color, 0, 0, w, h);
        n.Content = s;
        return n;
    }

    private static void TextNode(RetainedCanvas c, UiNode parent, VectorFont font, string text,
        float x, float y, float size, uint color)
    {
        UiNode n = c.AddChild(parent);
        n.Transform = Affine2D.Translate(x, y);
        n.Color = color;
        var s = new Scene2D();
        font.AppendText(s, text, 0, 0, size, color);
        n.Content = s;
    }

    private static void RenderOnce(GpuDevice device, RetainedCanvas canvas, uint w, uint h, GpuBuffer fb)
    {
        using GpuCommandBuffer cmd = device.MainQueue.StartCommandRecording();
        canvas.Render(cmd, Camera2D.Pixels, w, h, fb);
        cmd.Finish();
        device.MainQueue.SubmitAndWait(cmd);
    }

    private static (byte r, byte g, byte b) Rgb(ReadOnlySpan<byte> px, uint w, int x, int y)
    { int i = (y * (int)w + x) * 4; return (px[i], px[i + 1], px[i + 2]); }
}
