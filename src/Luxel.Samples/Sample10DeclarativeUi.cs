using Luxel;
using Luxel.Typography;
using Luxel.TwoD;
using Luxel.UI;
using Luxel.Controls;
using Luxel.UI.Styling;
using S = Luxel.UI.Tailwind.S;  // 状態別スタイル utility (On/Bg ...)
using static Luxel.Controls.Kit;       // ベアファクトリ (Grid/Button/Text/Border ...)
using static Luxel.UI.Decl;     // 添付プロパティ P.Grid.Column(1) 用の P

namespace Luxel.Samples;

/// <summary>
/// サンプル10: 宣言的 UI ライブラリ (signals + Flutter 風単一パスレイアウト + 入力)。
/// Grid(star列/行) + signal 束縛テキスト + Button を宣言的に構築し、Click/hover を programmatic に与える。
///   - hover → ボタン背景色を部分更新 (スタイルのみ, Segment 不変)
///   - Click → onClick 発火で signal 更新 → カウンタ表示が変わる (内容変更=再構築)
/// 構文: Grid(columns:[1,2])[ Button(text, onClick, P.Grid.Column(1)) ] (構築=P無し, 添付=P.Grid.*)。
/// </summary>
public static class Sample10DeclarativeUi
{
    public static int Run(Func<GpuDevice> createDevice)
    {
        const uint w = 480, h = 320;
        using GpuDevice device = createDevice();
        Console.WriteLine($"device: {device.Name}");

        using var raster = new Rasterizer2D(device);
        using var canvas = new RetainedCanvas(raster);
        using var font = VectorFont.LoadSystem();

        uint panel = Color2D.Rgba(245, 246, 250), dark = Color2D.Rgba(30, 33, 40);
        uint blue = Color2D.Rgba(60, 120, 210), blueHi = Color2D.Rgba(95, 160, 245);

        var count = new Signal<int>(0);

        var host = new UiHost(canvas, font, w, h);
        host.SetRoot(
            Border(background: panel, padding: new Thickness(16))
            [
                Grid(columns: [1, 2], rows: [GridLength.Px(70), GridLength.Star(1)])
                [
                    Text($"Count: {count}", 30, color: dark,
                        parts: [P.Grid.Row(0), P.Grid.ColumnSpan(2)]),
                    Button(_ => count.Value--, "- 1",
                        background: blue, foreground: Luxel.TwoD.Color2D.White,
                        parts: [S.On(WidgetState.Hover, S.Bg(blueHi)), P.Grid.Column(0), P.Grid.Row(1)]),
                    Button(_ => count.Value++, "+ 1",
                        background: blue, foreground: Luxel.TwoD.Color2D.White,
                        parts: [S.On(WidgetState.Hover, S.Bg(blueHi)), P.Grid.Column(1), P.Grid.Row(1)])
                ]
            ]
        );

        using GpuBuffer fb = device.Malloc(w * h * 4, GpuMemoryKind.HostMapped);

        // --- 初期描画 ---
        RenderOnce(device, canvas, w, h, fb);
        Console.WriteLine($"[build] fullRebuild={canvas.LastWasFullRebuild}");
        Span<byte> p0 = fb.Span<byte>((int)(w * h * 4));
        var plusBefore = Rgb(p0, w, 173, 105);                 // +1 ボタン左寄り (青, 文字を避ける)
        PngWriter.WriteRgba(Path.Combine(AppContext.BaseDirectory, "ui_before.png"), (int)w, (int)h, p0);

        // --- hover: +1 ボタン → スタイルのみ部分更新 ---
        host.PointerMove(190, 105);
        RenderOnce(device, canvas, w, h, fb);
        bool hoverPartial = !canvas.LastWasFullRebuild && canvas.LastSegmentBytesWritten == 0
                          && canvas.LastStyleWrites >= 1;
        Console.WriteLine($"[hover] fullRebuild={canvas.LastWasFullRebuild} segBytes={canvas.LastSegmentBytesWritten} " +
                          $"styleWrites={canvas.LastStyleWrites}");
        Span<byte> p1 = fb.Span<byte>((int)(w * h * 4));
        var plusHover = Rgb(p1, w, 173, 105);
        PngWriter.WriteRgba(Path.Combine(AppContext.BaseDirectory, "ui_hover.png"), (int)w, (int)h, p1);

        // --- click: +1 → count++ ---
        int before = count.Value;
        bool clicked = host.Click(190, 105);
        int afterPlus = count.Value;
        RenderOnce(device, canvas, w, h, fb);
        Span<byte> p2 = fb.Span<byte>((int)(w * h * 4));
        PngWriter.WriteRgba(Path.Combine(AppContext.BaseDirectory, "ui_after.png"), (int)w, (int)h, p2);

        // --- click: -1 (別カラムのヒットテスト) ---
        host.Click(40, 105);
        int afterMinus = count.Value;

        Console.WriteLine($"click +1: {before}->{afterPlus} (clicked={clicked}); then -1 -> {afterMinus}");
        Console.WriteLine($"+1 color: before={plusBefore} hover={plusHover}");

        bool hitOk = clicked && afterPlus == before + 1 && afterMinus == afterPlus - 1;
        bool hoverColorOk = plusBefore.b > 150 && plusBefore.r < 120                 // 通常=青
                          && plusHover.r > plusBefore.r && plusHover.g > plusBefore.g; // hover=明るい青
        bool ok = hitOk && hoverPartial && hoverColorOk;
        Console.WriteLine($"hit={hitOk} hoverPartial={hoverPartial} hoverColor={hoverColorOk}");
        Console.WriteLine(ok ? "OK: 宣言的UI (Grid star + signal束縛 + Click/hover 部分更新) を確認"
                             : "FAILED: 宣言的UI の結果が想定と異なります");
        return ok ? 0 : 1;
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
