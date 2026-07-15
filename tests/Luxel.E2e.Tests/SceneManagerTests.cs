using Luxel.Framework;
using Microsoft.Extensions.DependencyInjection;

namespace Luxel.Gallery.E2eTests;

public class SceneManagerTests
{
    [Fact]
    public async Task Graph_ChildSuspendResumeAndSwitch_RunLifecycleAtBoundaries()
    {
        var services = new ServiceCollection().BuildServiceProvider();
        var frames = new RecordingFrameScheduler();
        var manager = new SceneManager(services, frames);
        var events = new List<string>();
        var root = new RecordingScene("root", events, SceneExecutionMode.Continuous);
        var child = new RecordingScene("child", events, SceneExecutionMode.Continuous);

        await manager.InitializeAsync(root);
        Assert.Equal(1, frames.ContinuousLeases);
        SceneNode rootNode = manager.Root!;

        await manager.AddChildAsync(rootNode, child);
        await manager.ApplyPendingAsync();
        SceneNode childNode = Assert.Single(rootNode.Children);
        Assert.Equal(SceneLifecycleState.Active, childNode.State);
        Assert.Equal(2, frames.ContinuousLeases);

        await manager.SuspendAsync(childNode);
        await manager.ApplyPendingAsync();
        Assert.Equal(SceneLifecycleState.Suspended, childNode.State);
        Assert.Equal(1, frames.ContinuousLeases);

        await manager.ResumeAsync(childNode);
        await manager.ApplyPendingAsync();
        Assert.Equal(SceneLifecycleState.Active, childNode.State);
        Assert.Equal(2, frames.ContinuousLeases);

        var next = new RecordingScene("next", events, SceneExecutionMode.OnDemand);
        await manager.SwitchAsync(next);
        await manager.ApplyPendingAsync();

        Assert.Same(next, manager.Current);
        Assert.Equal(0, frames.ContinuousLeases);
        Assert.Equal(new[]
        {
            "root.load", "root.activate", "child.load", "child.activate",
            "child.suspend", "child.resume",
            "child.deactivate", "child.unload", "root.deactivate", "root.unload",
            "next.load", "next.activate",
        }, events);

        await manager.ShutdownAsync();
    }

    [Fact]
    public async Task ChildLocalSuspension_SurvivesParentSuspendResume()
    {
        var services = new ServiceCollection().BuildServiceProvider();
        var manager = new SceneManager(services, new RecordingFrameScheduler());
        var events = new List<string>();
        var root = new RecordingScene("root", events, SceneExecutionMode.Continuous);
        var child = new RecordingScene("child", events, SceneExecutionMode.Continuous);

        await manager.InitializeAsync(root);
        await manager.AddChildAsync(manager.Root!, child);
        await manager.ApplyPendingAsync();
        SceneNode childNode = Assert.Single(manager.Root!.Children);

        await manager.SuspendAsync(childNode);
        await manager.ApplyPendingAsync();
        await manager.SuspendAsync(manager.Root!);
        await manager.ApplyPendingAsync();
        await manager.ResumeAsync(manager.Root!);
        await manager.ApplyPendingAsync();

        Assert.Equal(SceneLifecycleState.Active, manager.Root!.State);
        Assert.Equal(SceneLifecycleState.Suspended, childNode.State);

        await manager.ResumeAsync(childNode);
        await manager.ApplyPendingAsync();
        Assert.Equal(SceneLifecycleState.Active, childNode.State);
        await manager.ShutdownAsync();
    }

    [Fact]
    public async Task UiScene_IsOnDemand_AndContinuousRequestUsesTemporaryLease()
    {
        var services = new ServiceCollection().BuildServiceProvider();
        var frames = new RecordingFrameScheduler();
        var manager = new SceneManager(services, frames);
        var scene = new RecordingUiScene(frames);

        await manager.InitializeAsync(scene);
        IScene contract = scene;
        Assert.True(contract.TryBeginFrame());
        Assert.False(contract.TryBeginFrame());

        scene.Invalidate();
        Assert.True(contract.TryBeginFrame());
        Assert.True(frames.FrameRequests > 0);

        using (scene.Animate())
        {
            Assert.Equal(1, frames.ContinuousLeases);
            Assert.True(contract.TryBeginFrame());
            Assert.True(contract.TryBeginFrame());
        }
        Assert.Equal(0, frames.ContinuousLeases);
        await manager.ShutdownAsync();
    }

    [Fact]
    public async Task PausedParent_DoesNotBlockActiveOverlayChild()
    {
        var services = new ServiceCollection().BuildServiceProvider();
        var manager = new SceneManager(services, new RecordingFrameScheduler());
        var parent = new DemandScene(run: false); // pause中のgameplay相当
        var overlay = new DemandScene(run: true); // pause menu相当

        await manager.InitializeAsync(parent);
        await manager.AddChildAsync(manager.Root!, overlay,
            SceneExecutionMode.OnDemand, SceneRenderMode.WhenDirty);
        await manager.ApplyPendingAsync();
        SceneNode child = Assert.Single(manager.Root!.Children);

        Assert.False(SceneRunner.ShouldRunNode(manager.Root));
        Assert.True(SceneRunner.ShouldRunNode(child));
        Assert.Equal(SceneLifecycleState.Active, child.State);

        await manager.ShutdownAsync();
    }

    private sealed class RecordingScene(
        string name, List<string> events, SceneExecutionMode executionMode) : IScene
    {
        public SceneExecutionMode ExecutionMode => executionMode;
        public Task OnLoadAsync() { events.Add($"{name}.load"); return Task.CompletedTask; }
        public Task OnActivateAsync() { events.Add($"{name}.activate"); return Task.CompletedTask; }
        public Task OnSuspendAsync() { events.Add($"{name}.suspend"); return Task.CompletedTask; }
        public Task OnResumeAsync() { events.Add($"{name}.resume"); return Task.CompletedTask; }
        public Task OnDeactivateAsync() { events.Add($"{name}.deactivate"); return Task.CompletedTask; }
        public Task OnUnloadAsync() { events.Add($"{name}.unload"); return Task.CompletedTask; }
    }

    private sealed class RecordingFrameScheduler : IFrameScheduler
    {
        public int ContinuousLeases { get; private set; }
        public int FrameRequests { get; private set; }

        public ValueTask WaitForNextFrameAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public void RequestFrame() => FrameRequests++;
        public IDisposable AcquireContinuousFrames()
        {
            ContinuousLeases++;
            return new Lease(this);
        }

        private sealed class Lease(RecordingFrameScheduler owner) : IDisposable
        {
            private RecordingFrameScheduler? _owner = owner;
            public void Dispose()
            {
                RecordingFrameScheduler? value = Interlocked.Exchange(ref _owner, null);
                if (value is not null) value.ContinuousLeases--;
            }
        }
    }

    private sealed class RecordingUiScene(IFrameScheduler frames) : UiScene(frames)
    {
        public IDisposable Animate() => BeginContinuousFrames();
    }

    private sealed class DemandScene(bool run) : IScene
    {
        public SceneExecutionMode ExecutionMode => SceneExecutionMode.OnDemand;
        public bool TryBeginFrame() => run;
    }
}
