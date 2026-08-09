using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Luxel.Framework.Game;

/// <summary>
/// engine の起動 wrapper。<see cref="BackgroundService"/> として Host.RunAsync で起動され、
/// startup Scene を <see cref="SceneManager"/> に流して <see cref="SceneManager.RunLoopAsync"/> に委譲する。
///
/// <para>ゲームループの本体は <see cref="IScene.RunAsync"/> 内 (Scene 所有)。GameLoop は Scene 切替の
/// 外側ループを回すだけ。</para>
/// </summary>
public sealed class GameLoop(IServiceProvider services, SceneManager sceneManager, StartupScene? startup = null)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (startup is null) return;

        // Startup Scene: DI にあれば再利用、無ければ生成
        var scene = (IScene?)services.GetService(startup.SceneType)
                    ?? (IScene)ActivatorUtilities.CreateInstance(services, startup.SceneType);

        await sceneManager.RunLoopAsync(scene, stoppingToken);
    }
}
