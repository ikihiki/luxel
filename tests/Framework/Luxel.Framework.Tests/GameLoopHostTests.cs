using Luxel.Framework.Game;

namespace Luxel.Tests;

public sealed class GameLoopHostTests
{
    [Fact]
    public async Task HostedService_InvokesLoopOnce_AndForwardsStopToken()
    {
        var loop = new RecordingGameLoop();
        var hosted = new GameLoopHostedService(loop);

        await hosted.StartAsync(CancellationToken.None);
        await loop.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await hosted.StopAsync(CancellationToken.None);

        Assert.Equal(1, loop.CallCount);
        Assert.True(loop.ObservedToken.IsCancellationRequested);
    }

    [Fact]
    public void AddGameLoop_RejectsMultipleRegistrations()
    {
        LuxelHostBuilder builder = LuxelHostBuilder.Create()
            .AddGameLoop<RecordingGameLoop>();

        Assert.Throws<InvalidOperationException>(() => builder.AddGameLoop<SecondGameLoop>());
    }

    private sealed class RecordingGameLoop : IGameLoop
    {
        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int CallCount { get; private set; }
        public CancellationToken ObservedToken { get; private set; }

        public async Task RunAsync(CancellationToken token)
        {
            CallCount++;
            ObservedToken = token;
            Started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
        }
    }

    private sealed class SecondGameLoop : IGameLoop
    {
        public Task RunAsync(CancellationToken token) => Task.CompletedTask;
    }
}
