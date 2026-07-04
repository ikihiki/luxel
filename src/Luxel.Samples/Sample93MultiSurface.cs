using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Luxel.Framework;

namespace Luxel.Samples;

/// <summary>
/// Sample 93: 複数 <see cref="UiSurface"/> を異なる <see cref="UiSurface.RateHz"/> で登録し、
/// フレーム間で描画回数が rate に応じて振り分けられることを確認する demo。
/// (Option A: 時間ベース throttle。Option B: 表示 rate の整数分の 1 は RateHz の値だけで自然に実現される。)
/// </summary>
public static class Sample93MultiSurface
{
    public static int Run(Func<GpuDevice> createDevice)
    {
        Console.WriteLine("=== Sample 93: 複数 UiSurface + rate throttle ===");

        var host = LuxelHostBuilder.Create()
            .UseGpu(createDevice)
            .ConfigureServices(s => s.AddSingleton<MainScene>())
            .AddScene<MainScene>()
            .ConfigureServices(s => s.Configure<HostOptions>(o => o.ShutdownTimeout = TimeSpan.FromSeconds(3)))
            .Build();

        var scene = host.Services.GetRequiredService<MainScene>();

        // 500ms 走らせる (16ms/frame ≒ 30 frame 前後)
        var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        host.RunAsync(cts.Token).GetAwaiter().GetResult();

        long total = scene.TotalFrames;
        long hud   = scene.HudSurface.RenderedFrames;
        long radar = scene.RadarSurface.RenderedFrames;
        long dmg   = scene.DamageSurface.RenderedFrames;
        long onDemand = scene.OnDemandSurface.RenderedFrames;

        Console.WriteLine($"  Total frames:        {total}");
        Console.WriteLine($"  HUD (60Hz):          {hud}");
        Console.WriteLine($"  Radar (30Hz):        {radar}   (期待: total の 約半分)");
        Console.WriteLine($"  Damage (10Hz):       {dmg}   (期待: total の 約 1/6)");
        Console.WriteLine($"  OnDemand (dirty のみ、1 回だけ dirty した): {onDemand}");

        bool ok = total > 3
               && hud == total                       // 60Hz = 毎フレーム
               && radar <  hud && radar >= hud / 3   // Radar は HUD より少ない (30Hz ≒ 半分)
               && dmg <  radar                       // Damage は Radar より更に少ない
               && onDemand == 1;                     // dirty-only、1 回だけ dirty flag 立てた

        Console.WriteLine(ok ? "OK: rate に応じて描画回数が分かれる" : "FAILED");
        return ok ? 0 : 1;
    }

    public sealed class MainScene : GameScene
    {
        public UiSurface HudSurface { get; }
        public UiSurface RadarSurface { get; }
        public UiSurface DamageSurface { get; }
        public UiSurface OnDemandSurface { get; }
        public long TotalFrames { get; private set; }

        public MainScene(SceneLoopServices loop) : base(loop)
        {
            HudSurface       = new UiSurface(Device, "hud",       320, 180) { RateHz = 60 };
            RadarSurface     = new UiSurface(Device, "radar",     128, 128, UiSurfaceKind.WorldSpaceQuad) { RateHz = 30 };
            DamageSurface    = new UiSurface(Device, "damage",    128,  64, UiSurfaceKind.WorldSpaceQuad) { RateHz = 10 };
            OnDemandSurface  = new UiSurface(Device, "on-demand", 128,  64) { RateHz = -1, NeedsRedraw = false };

            // 各 surface の描画: shader が無いのでダミー clear のみ
            HudSurface.Draw = ctx => ctx.Cmd
                .BeginRendering(HudSurface.Target, null, 0.10f, 0.10f, 0.10f, 1f)
                .EndRendering();
            RadarSurface.Draw = ctx => ctx.Cmd
                .BeginRendering(RadarSurface.Target, null, 0.20f, 0.30f, 0.20f, 1f)
                .EndRendering();
            DamageSurface.Draw = ctx => ctx.Cmd
                .BeginRendering(DamageSurface.Target, null, 0.40f, 0.10f, 0.10f, 1f)
                .EndRendering();
            OnDemandSurface.Draw = ctx => ctx.Cmd
                .BeginRendering(OnDemandSurface.Target, null, 0.10f, 0.20f, 0.30f, 1f)
                .EndRendering();

            AddSurface(HudSurface);
            AddSurface(RadarSurface);
            AddSurface(DamageSurface);
            AddSurface(OnDemandSurface);
        }

        protected override void OnUpdate(UpdateContext ctx)
        {
            TotalFrames++;
            // Frame 10 で OnDemand を 1 回 dirty にする → 1 度だけ描画される
            if (ctx.Time.Frame == 10) OnDemandSurface.NeedsRedraw = true;
        }

        public override Task OnUnloadAsync()
        {
            HudSurface.Dispose();
            RadarSurface.Dispose();
            DamageSurface.Dispose();
            OnDemandSurface.Dispose();
            return Task.CompletedTask;
        }
    }
}
