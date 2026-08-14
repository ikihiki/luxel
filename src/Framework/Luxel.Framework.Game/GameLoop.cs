using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using System.Diagnostics;
using Luxel.Audio;
using Luxel.Input;
using Luxel.Resources;
using Luxel.Graphics.RenderSystem;

namespace Luxel.Framework.Game;

/// <summary>The single application loop started by the Luxel host.</summary>
public interface IGameLoop
{
    Task RunAsync(CancellationToken token);
}

/// <summary>
/// Hosted adapter for <see cref="IGameLoop"/>. It deliberately does not use <see cref="Task.Run(Action)"/>
/// so graphics-thread affinity remains owned by the configured loop and frame waiter.
/// </summary>
public sealed class GameLoopHostedService(IGameLoop gameLoop) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => gameLoop.RunAsync(stoppingToken);
}

/// <summary>
/// Transitional implementation used while repository scenes are migrated to <see cref="GameSceneSystem"/>.
/// It preserves the old scene loop without making the hosted adapter aware of scenes.
/// </summary>
internal sealed class LegacySceneGameLoop(
    IServiceProvider services,
    SceneManager sceneManager,
    StartupScene? startup = null) : IGameLoop
{
    public async Task RunAsync(CancellationToken token)
    {
        if (startup is null) return;

        var scene = (IScene?)services.GetService(startup.SceneType)
                    ?? (IScene)ActivatorUtilities.CreateInstance(services, startup.SceneType);

        await sceneManager.RunLoopAsync(scene, token);
    }
}

public interface IRenderFrameScheduler
{
    ValueTask<RenderOpportunity> WaitAsync(CancellationToken token);
}

public interface IGameSceneBootstrap
{
    ValueTask BootstrapAsync(IGameSceneSystem scenes, CancellationToken token);
}

public sealed class EmptyGameSceneBootstrap : IGameSceneBootstrap
{
    public ValueTask BootstrapAsync(IGameSceneSystem scenes, CancellationToken token) => ValueTask.CompletedTask;
}

public sealed class DelegateRenderFrameScheduler : IRenderFrameScheduler
{
    private readonly Func<CancellationToken, Task> _wait;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private TimeSpan _last;
    private ulong _sequence;

    public DelegateRenderFrameScheduler(Func<CancellationToken, Task>? wait = null)
        => _wait = wait ?? (token => Task.Delay(16, token));

    public async ValueTask<RenderOpportunity> WaitAsync(CancellationToken token)
    {
        await _wait(token);
        TimeSpan now = _clock.Elapsed;
        TimeSpan delta = now - _last;
        _last = now;
        return new RenderOpportunity(++_sequence, now, delta);
    }
}

/// <summary>Standard application loop. Rendering decisions are delegated to the cadence coordinator.</summary>
public sealed class GameLoop(
    IRenderFrameScheduler frameScheduler,
    IGameSceneSystem scenes,
    IGameSceneBootstrap bootstrap,
    ICadenceExecutionCoordinator coordinator,
    InputBus inputBus,
    InputStack inputStack,
    IEnumerable<IInputSource> inputSources,
    ResourceSystem? resources = null,
    AudioMixer? mixer = null) : IGameLoop
{
    private readonly FixedTimestep _fixed = new();

    public async Task RunAsync(CancellationToken token)
    {
        await bootstrap.BootstrapAsync(scenes, token);
        await scenes.CommitCommandsAsync(token);
        long frameNumber = 0;

        try
        {
            while (!token.IsCancellationRequested)
            {
                RenderOpportunity opportunity = await frameScheduler.WaitAsync(token);
                float deltaSeconds = FixedTimestep.ScaleDt(opportunity.Delta.TotalSeconds, 1);
                var time = new FrameTime(++frameNumber, deltaSeconds, opportunity.Timestamp.TotalSeconds);

                foreach (IInputSource source in inputSources) source.Poll(inputBus);
                inputStack.Update(inputBus);

                int fixedSteps = _fixed.Advance(deltaSeconds);
                for (int index = 0; index < fixedSteps; index++)
                    scenes.FixedUpdate(new FixedUpdateContext(
                        time.Frame, time.TotalSeconds, (float)_fixed.FixedDt));

                scenes.Update(new UpdateContext(time));
                await scenes.CommitCommandsAsync(token);
                RenderSystemFrameSnapshot snapshot = scenes.CreateRenderSnapshot(time);
                await coordinator.ExecuteAsync(opportunity, snapshot, token);

                resources?.Pump();
                mixer?.Tick();
            }
        }
        finally
        {
            await coordinator.DrainAsync(CancellationToken.None);
            await scenes.ShutdownAsync(CancellationToken.None);
        }
    }
}
