using System.Numerics;
using Luxel;
using Luxel.Typography;
using Luxel.TwoD;

namespace Luxel.Samples;

/// <summary>
/// サンプル08: GUI 風モックアップ (パネル/タイトルバー/ボタン/チェックボックス/スライダ + 日本語)。
/// すべてベクター (角丸塗り + ストローク + ベクターテキスト) を GPU コンピュートで描画。gui.png 出力。
/// </summary>
public static class Sample08Gui
{
    public static int Run(Func<GpuDevice> createDevice)
    {
        const uint w = 512, h = 340;
        using GpuDevice device = createDevice();
        Console.WriteLine($"device: {device.Name}");

        var s = new Scene2D();
        uint accent = Color2D.Rgba(70, 110, 200);
        uint dark = Color2D.Rgba(40, 44, 52);
        uint gray = Color2D.Rgba(150, 154, 162);

        s.FillRect(Color2D.Rgba(232, 234, 238), 0, 0, w, h);                 // デスクトップ背景
        s.FillRoundedRect(Color2D.Rgba(0, 0, 0, 40), 44, 40, 424, 268, 16);  // 影
        s.FillRoundedRect(Color2D.White, 40, 34, 424, 268, 16);              // ウィンドウ
        s.FillRoundedRect(accent, 40, 34, 424, 44, 16);                      // タイトルバー
        s.FillRect(accent, 40, 60, 424, 18);                                 // タイトルバー下の角を埋める

        // チェックボックス
        s.FillRoundedRect(Color2D.White, 66, 120, 22, 22, 5);
        s.BeginStroke(gray, 2).MoveTo(66 + 1, 120 + 1).LineTo(66 + 21, 120 + 1)
            .LineTo(66 + 21, 120 + 21).LineTo(66 + 1, 120 + 21).Close().End();    // 枠
        s.StrokePolyline(accent, 3, new Vector2(70, 131), new Vector2(76, 138), new Vector2(86, 124)); // チェック

        // スライダ
        s.StrokeLine(gray, 4, 66, 185, 300, 185);
        s.StrokeLine(accent, 4, 66, 185, 210, 185);
        s.FillCircle(accent, 210, 185, 11);
        s.FillCircle(Color2D.White, 210, 185, 5);

        // ボタン
        s.FillRoundedRect(Color2D.Rgba(225, 227, 232), 250, 250, 96, 34, 8);  // キャンセル
        s.FillRoundedRect(accent, 356, 250, 96, 34, 8);                       // OK

        // 日本語テキスト
        using (var jp = VectorFont.LoadSystemJapanese())
        {
            jp.AppendText(s, "設定パネル", 56, 64, 22, Color2D.White);
            jp.AppendText(s, "通知を有効にする", 100, 138, 20, dark);
            jp.AppendText(s, "音量", 66, 168, 18, dark);
            jp.AppendText(s, "言語: 日本語", 66, 226, 18, dark);
            jp.AppendText(s, "キャンセル", 262, 272, 17, dark);
            jp.AppendText(s, "ＯＫ", 388, 272, 18, Color2D.White);
        }

        using var raster = new Rasterizer2D(device);
        using EncodedScene encoded = raster.Encode(s);
        using GpuBuffer fb = device.Malloc(w * h * 4, GpuMemoryKind.HostMapped);
        using (GpuCommandBuffer cmd = device.MainQueue.StartCommandRecording())
        {
            raster.Render(cmd, encoded, Camera2D.Pixels, w, h, fb);
            cmd.Finish();
            device.MainQueue.SubmitAndWait(cmd);
        }

        Span<byte> px = fb.Span<byte>((int)(w * h * 4));
        int accentPixels = 0;
        for (int i = 0; i < w * h; i++)
            if (px[i * 4] is > 50 and < 100 && px[i * 4 + 2] > 170) accentPixels++;  // アクセント青
        PngWriter.WriteRgba(Path.Combine(AppContext.BaseDirectory, "gui.png"), (int)w, (int)h, px);

        Console.WriteLine($"アクセント色ピクセル: {accentPixels}");
        Console.WriteLine("PNG: gui.png");
        bool ok = accentPixels > 2000;   // タイトルバー/ボタン等が描かれている
        Console.WriteLine(ok ? "OK: GUI 風モックアップ (日本語) を描画"
                             : "FAILED: GUI シーンの内容が不足");
        return ok ? 0 : 1;
    }
}
