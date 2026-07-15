using Luxel.Framework;
using Luxel.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Luxel.Gallery.E2eTests;

public class FrameSchedulerTests
{
    [Fact]
    public async Task GameScheduler_PacesEveryFrame()
    {
        int waits = 0;
        var scheduler = new GameFrameScheduler(_ => { waits++; return Task.CompletedTask; });

        await scheduler.WaitForNextFrameAsync(CancellationToken.None);
        scheduler.RequestFrame();
        await scheduler.WaitForNextFrameAsync(CancellationToken.None);

        Assert.Equal(2, waits);
    }

    [Fact]
    public async Task UiScheduler_WaitsAfterInitialFrame_UntilRequested()
    {
        var scheduler = new UiFrameScheduler(frameDelayMs: 0);

        await scheduler.WaitForNextFrameAsync(CancellationToken.None); // 初回描画
        Task waiting = scheduler.WaitForNextFrameAsync(CancellationToken.None).AsTask();
        Assert.False(waiting.IsCompleted);

        scheduler.RequestFrame();
        await waiting.WaitAsync(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task UiScheduler_ContinuousLease_WakesAndDrivesFrames()
    {
        var scheduler = new UiFrameScheduler(frameDelayMs: 0);
        await scheduler.WaitForNextFrameAsync(CancellationToken.None);
        Task waiting = scheduler.WaitForNextFrameAsync(CancellationToken.None).AsTask();

        using (scheduler.AcquireContinuousFrames())
        {
            await waiting.WaitAsync(TimeSpan.FromSeconds(1));
            await scheduler.WaitForNextFrameAsync(CancellationToken.None);
        }

        Task idle = scheduler.WaitForNextFrameAsync(CancellationToken.None).AsTask();
        Assert.False(idle.IsCompleted);
        scheduler.RequestFrame();
        await idle.WaitAsync(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void HostBuilder_UseGameSceneManager_RegistersGameScheduler()
    {
        using var host = LuxelHostBuilder.Create()
            .UseGpu(() => throw new InvalidOperationException("GPU should remain lazy"))
            .UseGameSceneManager()
            .Build();

        Assert.IsType<GameFrameScheduler>(host.Services.GetRequiredService<IFrameScheduler>());
    }

    [Fact]
    public void HostBuilder_UseUiSceneManager_RegistersUiScheduler()
    {
        using var host = LuxelHostBuilder.Create()
            .UseGpu(() => throw new InvalidOperationException("GPU should remain lazy"))
            .UseGameSceneManager()
            .UseUiSceneManager()
            .Build();

        Assert.IsType<UiFrameScheduler>(host.Services.GetRequiredService<IFrameScheduler>());
    }

    [Fact]
    public async Task UiHostBuilder_RemoteCommandWakesIdleScheduler()
    {
        using var host = LuxelHostBuilder.Create()
            .UseGpu(() => throw new InvalidOperationException("GPU should remain lazy"))
            .UseUiSceneManager()
            .Build();

        IFrameScheduler scheduler = host.Services.GetRequiredService<IFrameScheduler>();
        EngineCommands commands = host.Services.GetRequiredService<EngineCommands>();
        await scheduler.WaitForNextFrameAsync(CancellationToken.None); // 初回
        Task idle = scheduler.WaitForNextFrameAsync(CancellationToken.None).AsTask();
        Assert.False(idle.IsCompleted);

        commands.Enqueue("remote.noop", null);

        await idle.WaitAsync(TimeSpan.FromSeconds(1));
    }
}
