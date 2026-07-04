using Luxel;
using Luxel.Typography;
using Luxel.TwoD;
using Luxel.UI;
using Luxel.UI.Styling;
using S = Luxel.UI.Tailwind.S;
using static Luxel.Controls.Kit;
using static Luxel.UI.Decl;

namespace Luxel.Samples;

/// <summary>
/// サンプル42 (TW-M3): <b>ユーザー定義の AppTheme record</b> + <c>Signal&lt;AppTheme&gt;</c> で
/// Light / Dark 切替する demo。Luxel 本体に Theme 型は存在せず、テーマはユーザーが record として
/// 自由に書く (複数種類の Theme record を同居させることも可)。
///
/// <para>方針:</para>
/// <list type="bullet">
///   <item>Theme = 単なる値持ち POCO ─ プロパティ名は自由</item>
///   <item>Light / Dark は同じ AppTheme 型の異なるインスタンス (Make* 関数で生成)</item>
///   <item><c>Signal&lt;AppTheme&gt;</c> の値変化で全 UI が rebuild され recolor される</item>
///   <item>Luxel は何も強制しない ─ ユーザーは自分の規約で Theme record を定義する</item>
/// </list>
/// </summary>
public static class Sample42AppTheme
{
    /// <summary>ユーザーが自由に定義するテーマ record。命名は完全にユーザー責任。</summary>
    public sealed record AppTheme
    {
        public required uint Primary { get; init; }
        public required uint PrimaryHover { get; init; }
        public required uint Surface { get; init; }
        public required uint OnPrimary { get; init; }
        public required uint OnSurface { get; init; }
        public required float RoundedMd { get; init; }
    }

    static AppTheme MakeLight() => new()
    {
        Primary      = Color2D.Rgba( 60, 120, 210),
        PrimaryHover = Color2D.Rgba( 80, 145, 235),
        Surface      = Color2D.Rgba(245, 246, 250),
        OnPrimary    = Color2D.White,
        OnSurface    = Color2D.Rgba( 30,  33,  40),
        RoundedMd    = 8f,
    };

    static AppTheme MakeDark() => new()
    {
        Primary      = Color2D.Rgba(120, 175, 255),
        PrimaryHover = Color2D.Rgba(150, 200, 255),
        Surface      = Color2D.Rgba( 24,  26,  32),
        OnPrimary    = Color2D.Rgba( 20,  22,  28),
        OnSurface    = Color2D.Rgba(232, 235, 240),
        RoundedMd    = 8f,
    };

    public static int Run(Func<GpuDevice> createDevice)
    {
        const uint w = 480, h = 200;
        using GpuDevice device = createDevice();
        Console.WriteLine($"device: {device.Name}");

        using var raster = new Rasterizer2D(device);
        using var canvas = new RetainedCanvas(raster);
        using var font = VectorFont.LoadSystem();

        var theme = new Signal<AppTheme>(MakeLight());
        var host = new UiHost(canvas, font, w, h);

        // テーマが切り替わったら UI を rebuild (StateStyle はビルド時に theme.Value から焼き込み)
        // Button と Border が同じ流儀 (style: 引数で StateStyle を直接渡す)
        Reactive.Effect(() =>
        {
            var t = theme.Value;
            host.SetRoot(
                Border(background: Bind.From(() => t.Surface), padding: new Thickness(24))
                [
                    Grid(columns: [1], rows: [GridLength.Star(1)])
                    [
                        Button(_ => {}, "Click",
                            background: t.Primary,
                            foreground: t.OnPrimary,
                            rounded: t.RoundedMd,
                            width: 200, height: 100,
                            parts: S.On(WidgetState.Hover, S.Bg(t.PrimaryHover)))
                    ]
                ]
            );
        });

        using GpuBuffer fb = device.Malloc(w * h * 4, GpuMemoryKind.HostMapped);

        void Snap(string name)
        {
            using GpuCommandBuffer cmd = device.MainQueue.StartCommandRecording();
            canvas.Render(cmd, Camera2D.Pixels, w, h, fb);
            cmd.Finish();
            device.MainQueue.SubmitAndWait(cmd);
            string png = Path.Combine(AppContext.BaseDirectory, $"apptheme_{name}.png");
            PngWriter.WriteRgba(png, (int)w, (int)h, fb.Span<byte>((int)(w * h * 4)));
        }

        (byte r, byte g, byte b) Rgb(int x, int y)
        { var p = fb.Span<byte>((int)(w * h * 4)); int i = (y * (int)w + x) * 4; return (p[i], p[i+1], p[i+2]); }

        // === Light ===
        Snap("01_light");
        var lightBtn = Rgb(120, 60);
        var lightBg  = Rgb(10, 10);
        Console.WriteLine($"  light: btn={lightBtn}, bg={lightBg}");

        // === Dark に切替 (Signal 経由で UI rebuild) ===
        theme.Value = MakeDark();
        Snap("02_dark");
        var darkBtn = Rgb(120, 60);
        var darkBg  = Rgb(10, 10);
        Console.WriteLine($"  dark:  btn={darkBtn}, bg={darkBg}");

        // === Light に戻す (再現性) ===
        theme.Value = MakeLight();
        Snap("03_light_again");
        var lightBtn2 = Rgb(120, 60);
        Console.WriteLine($"  light again: btn={lightBtn2}");

        // === 検証 ===
        bool ok = true;
        // light primary = (60, 120, 210)
        if (Math.Abs(lightBtn.r - 60) > 5 || Math.Abs(lightBtn.b - 210) > 5)
        { ok = false; Console.Error.WriteLine($"FAILED: light btn not (60,_,210): {lightBtn}"); }
        // dark primary = (120, 175, 255)
        if (Math.Abs(darkBtn.r - 120) > 5 || Math.Abs(darkBtn.b - 255) > 5)
        { ok = false; Console.Error.WriteLine($"FAILED: dark btn not (120,_,255): {darkBtn}"); }
        // dark surface (背景) は暗い (RGB 全て < 50)
        if (darkBg.r > 50 || darkBg.g > 50 || darkBg.b > 50)
        { ok = false; Console.Error.WriteLine($"FAILED: dark bg not dark: {darkBg}"); }
        // light surface は明るい
        if (lightBg.r < 200 || lightBg.g < 200 || lightBg.b < 200)
        { ok = false; Console.Error.WriteLine($"FAILED: light bg not light: {lightBg}"); }
        // light → dark → light で再現
        if (lightBtn2 != lightBtn)
        { ok = false; Console.Error.WriteLine($"FAILED: theme swap not idempotent: {lightBtn} != {lightBtn2}"); }

        Console.WriteLine(ok ? "OK: TW-M3 (AppTheme record + Light/Dark 切替) 動作"
                             : "FAILED");
        return ok ? 0 : 1;
    }
}
