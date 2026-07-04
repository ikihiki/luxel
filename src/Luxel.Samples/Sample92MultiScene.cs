using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Luxel.Framework;

namespace Luxel.Samples;

/// <summary>
/// Sample 92: MenuScene → GameplayScene の切替 demo。
/// 各 Scene は <see cref="GameScene"/> を継承し、標準 Phase (Update) の virtual メソッドを override するだけ。
/// </summary>
public static class Sample92MultiScene
{
    public static int Run(Func<GpuDevice> createDevice)
    {
        Console.WriteLine("=== Sample 92: multi-scene 切替 demo ===");
        var progress = new SceneProgress();

        var host = LuxelHostBuilder.Create()
            .UseGpu(createDevice)
            .ConfigureServices(s =>
            {
                s.AddSingleton(progress);
                s.AddSingleton<MenuScene>();
                s.AddSingleton<GameplayScene>();
            })
            .AddScene<MenuScene>()
            .ConfigureServices(s => s.Configure<HostOptions>(o => o.ShutdownTimeout = TimeSpan.FromSeconds(3)))
            .Build();

        var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        host.RunAsync(cts.Token).GetAwaiter().GetResult();

        Console.WriteLine($"  menu frames: {progress.MenuFrames}, gameplay frames: {progress.GameplayFrames}, switched: {progress.Switched}");
        bool ok = progress.MenuFrames >= 2 && progress.GameplayFrames >= 2 && progress.Switched;
        Console.WriteLine(ok ? "OK: MenuScene → GameplayScene の切替が動作" : "FAILED");
        return ok ? 0 : 1;
    }

    public sealed class SceneProgress
    {
        public int MenuFrames;
        public int GameplayFrames;
        public bool Switched;
    }

    public sealed class MenuScene : GameScene
    {
        private readonly SceneProgress _progress;
        private readonly SceneManager _sceneManager;

        public MenuScene(SceneLoopServices loop, SceneProgress progress, SceneManager sceneManager)
            : base(loop)
        {
            _progress = progress;
            _sceneManager = sceneManager;
        }

        protected override void OnUpdate(UpdateContext ctx)
        {
            _progress.MenuFrames++;
            Console.WriteLine($"  [Menu] frame {ctx.Time.Frame}");
            if (ctx.Time.Frame == 1)
            {
                _progress.Switched = true;
                _ = _sceneManager.SwitchAsync<GameplayScene>();
            }
        }
    }

    public sealed class GameplayScene : GameScene
    {
        private readonly SceneProgress _progress;

        public GameplayScene(SceneLoopServices loop, SceneProgress progress) : base(loop) => _progress = progress;

        protected override void OnUpdate(UpdateContext ctx)
        {
            _progress.GameplayFrames++;
            Console.WriteLine($"  [Gameplay] frame {ctx.Time.Frame}");
        }
    }
}
