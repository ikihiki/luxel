using Luxel;
using Luxel.TwoD;

namespace Luxel.Samples;

/// <summary>
/// サンプル96: image プリミティブ (Kind=2)。別バッファに描いた premultiplied RGBA を
/// 矩形へサンプリング合成する (iframe 相当の基盤)。
/// 検証: 等倍 blit / 透過合成 / 変換 (縮小) / opacity 継承 / クリップ / 移動の部分更新。
/// </summary>
public static class Sample96Image
{
    public static int Run(Func<GpuDevice> createDevice)
    {
        const uint w = 200, h = 160;
        using GpuDevice device = createDevice();
        Console.WriteLine($"device: {device.Name}");

        using var raster = new Rasterizer2D(device);

        // --- 子キャンバス (64x64, 透過): 左半分=不透明赤, 右半分=半透明緑 ---
        // 保持型は node.Color が Scene2D の形状色を上書きするため、色ごとにノードを分ける。
        using var child = new RetainedCanvas(raster);
        UiNode srcRed = child.AddChild(child.Root);
        srcRed.Color = Color2D.Rgba(255, 0, 0);
        srcRed.Content = new Scene2D().FillRect(Color2D.White, 0, 0, 32, 64);
        UiNode srcGreen = child.AddChild(child.Root);
        srcGreen.Color = Color2D.Rgba(0, 255, 0, 128);
        srcGreen.Content = new Scene2D().FillRect(Color2D.White, 32, 0, 32, 64);
        using GpuBuffer childFb = device.Malloc(64 * 64 * 4, GpuMemoryKind.HostMapped);
        using (GpuCommandBuffer cmd = device.MainQueue.StartCommandRecording())
        {
            child.Render(cmd, Camera2D.Pixels, 64, 64, childFb, transparent: true);
            cmd.Finish();
            device.MainQueue.SubmitAndWait(cmd);
        }
        byte[] cpx = Read(childFb, 64, 64);
        Console.WriteLine($"child (5,5)={Rgb(cpx, 64, 5, 5)} a={cpx[(5 * 64 + 5) * 4 + 3]} " +
                          $"(40,5)={Rgb(cpx, 64, 40, 5)} a={cpx[(5 * 64 + 40) * 4 + 3]} srcIndex={childFb.BindlessIndex}");

        // --- 親キャンバス (白背景): 4 パターンの ImageRect ---
        using var canvas = new RetainedCanvas(raster);
        Scene2D Img() => new Scene2D().ImageRect(childFb.BindlessIndex, 64, 64, 64, 0, 0, 64, 64);

        UiNode img1 = canvas.AddChild(canvas.Root);            // 等倍
        img1.Transform = Affine2D.Translate(10, 10);
        img1.Content = Img();

        UiNode img2 = canvas.AddChild(canvas.Root);            // 0.5 倍縮小
        img2.Transform = Affine2D.Mul(Affine2D.Translate(90, 10), Affine2D.Scale(0.5f, 0.5f));
        img2.Content = Img();

        UiNode img3 = canvas.AddChild(canvas.Root);            // opacity 0.5
        img3.Transform = Affine2D.Translate(10, 90);
        img3.Opacity = 0.5f;
        img3.Content = Img();

        UiNode img4 = canvas.AddChild(canvas.Root);            // クリップ (左 16px のみ)
        img4.Transform = Affine2D.Translate(140, 90);
        img4.Clip = new RectClip(0, 0, 16, 64);
        img4.Content = Img();

        using GpuBuffer fb = device.Malloc(w * h * 4, GpuMemoryKind.HostMapped);
        RenderOnce(device, canvas, w, h, fb);

        byte[] px = Read(fb, w, h);   // WC メモリは一括コピーしてから読む
        var blitRed = Rgb(px, w, 20, 40);        // img1 左半分 = 赤
        var blitGreen = Rgb(px, w, 52, 40);      // img1 右半分 = 半透明緑 over 白 ≒ (127,255,127)
        var outside = Rgb(px, w, 80, 40);        // img1 の外 = 白
        var scaled = Rgb(px, w, 98, 20);         // img2 (0.5x) 左半分 = 赤
        var faded = Rgb(px, w, 20, 120);         // img3 赤 × opacity0.5 over 白 ≒ (255,127,127)
        var clipIn = Rgb(px, w, 148, 120);       // img4 クリップ内 = 赤
        var clipOut = Rgb(px, w, 165, 120);      // img4 クリップ外 = 白
        PngWriter.WriteRgba(Path.Combine(AppContext.BaseDirectory, "sample96_image.png"), (int)w, (int)h, px);

        // --- 部分更新: 移動は transform 書込のみで image が追従 ---
        img1.Transform = Affine2D.Translate(10, 20);
        RenderOnce(device, canvas, w, h, fb);
        byte[] px2 = Read(fb, w, h);
        var movedRed = Rgb(px2, w, 20, 50);
        var movedOld = Rgb(px2, w, 20, 12);      // 旧位置上端 = 白
        bool partialOk = !canvas.LastWasFullRebuild && canvas.LastSegmentBytesWritten == 0 && canvas.LastTransformWrites >= 1;

        Console.WriteLine($"blit red={blitRed} green={blitGreen} outside={outside}");
        Console.WriteLine($"scaled={scaled} faded={faded} clip in={clipIn} out={clipOut}");
        Console.WriteLine($"moved red={movedRed} old={movedOld} partial={partialOk}");

        bool blitOk = IsRed(blitRed) && IsWhite(outside)
                   && blitGreen.g > 240 && Near(blitGreen.r, 127, 12) && Near(blitGreen.b, 127, 12);
        bool scaleOk = IsRed(scaled);
        bool fadeOk = Near(faded.r, 255, 8) && Near(faded.g, 127, 12) && Near(faded.b, 127, 12);
        bool clipOk = IsRed(clipIn) && IsWhite(clipOut);
        bool moveOk = IsRed(movedRed) && IsWhite(movedOld) && partialOk;

        bool ok = blitOk && scaleOk && fadeOk && clipOk && moveOk;
        Console.WriteLine($"blit={blitOk} scale={scaleOk} fade={fadeOk} clip={clipOk} move={moveOk}");
        Console.WriteLine(ok ? "OK: image プリミティブ (blit/透過/変換/opacity/クリップ/部分更新)"
                             : "FAILED: image プリミティブの結果が想定と異なります");
        return ok ? 0 : 1;
    }

    private static bool IsRed((byte r, byte g, byte b) c) => c.r > 240 && c.g < 15 && c.b < 15;
    private static bool IsWhite((byte r, byte g, byte b) c) => c is { r: > 245, g: > 245, b: > 245 };
    private static bool Near(byte v, int target, int tol) => Math.Abs(v - target) <= tol;

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
