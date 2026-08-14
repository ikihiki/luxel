using Luxel.Framework.Game;
using Luxel.Graphics.RenderSystem;
using Microsoft.Extensions.DependencyInjection;

namespace Luxel.Tests;

public sealed class GameSceneSystemTests
{
    [Fact]
    public async Task Push_UpdatesRunningScene_AndPublishesAssignments()
    {
        using ServiceProvider services = new ServiceCollection().BuildServiceProvider();
        var system = new GameSceneSystem(services);
        var feature = new StubFeature();
        var scene = new RecordingScene(feature);
        GameSceneId id = GameSceneId.New();

        GameSceneCommandHandle handle = system.Enqueue(new GameSceneCommand.Push(id, scene));
        await system.CommitCommandsAsync(CancellationToken.None);
        await handle.Completion;

        system.FixedUpdate(new FixedUpdateContext(1, 1, 1f / 60));
        system.Update(new UpdateContext(new FrameTime(1, 1f / 60, 1)));
        RenderSystemFrameSnapshot snapshot = system.CreateRenderSnapshot(new FrameTime(1, 1f / 60, 1));

        Assert.Equal(1, scene.LoadCount);
        Assert.Equal(1, scene.FixedCount);
        Assert.Equal(1, scene.UpdateCount);
        Assert.Single(snapshot.FeatureSets.Sets[RecordingScene.Set].Features);
        Assert.True(snapshot.Context.Changes.HasFlag(RenderSystemChangeFlags.Assignment));
    }

    [Fact]
    public async Task SleepingScene_IsExcludedFromUpdateAndRendering()
    {
        using ServiceProvider services = new ServiceCollection().BuildServiceProvider();
        var system = new GameSceneSystem(services);
        var scene = new RecordingScene(new StubFeature());
        GameSceneId id = GameSceneId.New();
        system.Enqueue(new GameSceneCommand.Push(id, scene));
        await system.CommitCommandsAsync(CancellationToken.None);
        system.Enqueue(new GameSceneCommand.SetState(id, GameSceneState.Sleeping));
        await system.CommitCommandsAsync(CancellationToken.None);

        system.Update(new UpdateContext(new FrameTime(1, 1f / 60, 1)));
        RenderSystemFrameSnapshot snapshot = system.CreateRenderSnapshot(new FrameTime(1, 1f / 60, 1));

        Assert.Equal(0, scene.UpdateCount);
        Assert.Empty(snapshot.FeatureSets.Sets);
    }

    [Fact]
    public async Task ConfigureFailure_UnloadsCandidate_AndDoesNotPublishIt()
    {
        using ServiceProvider services = new ServiceCollection().BuildServiceProvider();
        var system = new GameSceneSystem(services);
        var scene = new RecordingScene(new StubFeature()) { ThrowOnConfigure = true };
        GameSceneCommandHandle handle = system.Enqueue(new GameSceneCommand.Push(GameSceneId.New(), scene));

        await system.CommitCommandsAsync(CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await handle.Completion);
        Assert.Equal(1, scene.UnloadCount);
        Assert.Empty(system.CreateRenderSnapshot(default).FeatureSets.Sets);
    }

    private sealed class RecordingScene(IRenderFeature feature) : IGameScene
    {
        public static RenderFeatureSetId Set { get; } = new("test");
        public int LoadCount { get; private set; }
        public int UnloadCount { get; private set; }
        public int FixedCount { get; private set; }
        public int UpdateCount { get; private set; }
        public bool ThrowOnConfigure { get; init; }

        public ValueTask LoadAsync(GameSceneContext context, CancellationToken token)
        {
            LoadCount++;
            return ValueTask.CompletedTask;
        }

        public void ConfigureRendering(
            RenderFeatureSetCatalog featureSets,
            RenderFeatureAssignmentBuilder assignments)
        {
            if (ThrowOnConfigure) throw new InvalidOperationException("configure failed");
            assignments.Register(Set, feature);
        }

        public void FixedUpdate(in FixedUpdateContext context) => FixedCount++;
        public void Update(in UpdateContext context) => UpdateCount++;

        public ValueTask UnloadAsync(GameSceneContext context, CancellationToken token)
        {
            UnloadCount++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class StubFeature : IRenderFeature
    {
        public void AddPasses(RenderFeatureContext context) { }
    }
}
