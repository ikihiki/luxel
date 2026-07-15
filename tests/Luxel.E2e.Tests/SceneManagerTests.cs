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

    [Fact]
    public async Task RootTransition_RunsOutgoingAndIncomingUntilCompletion()
    {
        var services = new ServiceCollection().BuildServiceProvider();
        var frames = new RecordingFrameScheduler();
        var manager = new SceneManager(services, frames);
        var events = new List<string>();
        var outgoing = new TransitionRecordingScene("out", events, run: false);
        var incoming = new TransitionRecordingScene("in", events, run: false);
        var progress = new List<float>();

        await manager.InitializeAsync(outgoing);
        Task completion = manager.TransitionAsync(incoming,
            new SceneTransitionSpec(0.2f, c => progress.Add(c.Progress), p => p * p));
        Assert.False(completion.IsCompleted);

        await manager.ApplyPendingAsync();

        Assert.True(manager.IsTransitioning);
        Assert.Same(outgoing, manager.Current);
        Assert.Same(incoming, manager.TransitionIncoming!.Scene);
        Assert.Equal(2, manager.GetActiveNodes().Length);
        Assert.Equal(1, frames.ContinuousLeases); // transition自身の一時lease
        Assert.All(manager.GetActiveNodes(), n => Assert.True(n.IsTransitioning));
        Assert.All(manager.GetActiveNodes(), n => Assert.True(SceneRunner.ShouldRunNode(n)));
        Assert.Equal(0f, progress[^1]);

        await manager.AdvanceTransitionAsync(0.1f); // progress=0を1 frame保持
        Assert.Equal(0f, progress[^1]);
        await manager.AdvanceTransitionAsync(0.1f);
        Assert.Equal(0.25f, progress[^1]); // easing p²
        await manager.AdvanceTransitionAsync(0.1f);
        await completion;

        Assert.False(manager.IsTransitioning);
        Assert.Same(incoming, manager.Current);
        Assert.Null(manager.TransitionIncoming);
        Assert.Equal(1f, progress[^1]);
        Assert.Equal(0, frames.ContinuousLeases);
        Assert.Contains("out.deactivate", events);
        Assert.Contains("out.unload", events);
        Assert.DoesNotContain("in.deactivate", events);
        await manager.ShutdownAsync();
    }

    [Fact]
    public async Task HardSwitch_InterruptsTransitionAndUnloadsBothPreviousRoots()
    {
        var services = new ServiceCollection().BuildServiceProvider();
        var manager = new SceneManager(services, new RecordingFrameScheduler());
        var events = new List<string>();
        var outgoing = new TransitionRecordingScene("out", events, run: true);
        var incoming = new TransitionRecordingScene("in", events, run: true);
        var replacement = new TransitionRecordingScene("replacement", events, run: true);

        await manager.InitializeAsync(outgoing);
        Task transition = manager.TransitionAsync(incoming, new SceneTransitionSpec(10f));
        await manager.ApplyPendingAsync();
        await manager.SwitchAsync(replacement);
        await manager.ApplyPendingAsync();

        Assert.True(transition.IsCanceled);
        Assert.False(manager.IsTransitioning);
        Assert.Same(replacement, manager.Current);
        Assert.Contains("in.unload", events);
        Assert.Contains("out.unload", events);
        await manager.ShutdownAsync();
    }

    [Fact]
    public async Task DockScene_AttachesNamedSlotsAsIndependentChildren()
    {
        var services = new ServiceCollection().BuildServiceProvider();
        var frames = new RecordingFrameScheduler();
        var manager = new SceneManager(services, frames);
        var events = new List<string>();
        var list = new RecordingScene("list", events, SceneExecutionMode.OnDemand);
        var content = new RecordingScene("content", events, SceneExecutionMode.Continuous);
        var dock = new RecordingDockScene(manager, events, list, content);

        await manager.InitializeAsync(dock);
        Assert.Empty(manager.Root!.Children);

        await manager.ApplyPendingAsync();

        Assert.Equal(2, manager.Root.Children.Count);
        Assert.Same(list, dock.GetSlotNode("stories")!.Scene);
        Assert.Same(content, dock.GetSlotNode("content")!.Scene);
        Assert.Same(manager.Root, dock.GetSlotNode("stories")!.Parent);
        Assert.False(SceneRunner.ShouldRunNode(manager.Root));
        Assert.True(SceneRunner.ShouldRunNode(dock.GetSlotNode("stories")!));
        Assert.Equal(1, frames.ContinuousLeases); // content slotだけがcontinuous

        await manager.ShutdownAsync();

        Assert.Equal(new[]
        {
            "dock.load", "dock.activate",
            "list.load", "list.activate", "content.load", "content.activate",
            "content.deactivate", "content.unload", "list.deactivate", "list.unload",
            "dock.deactivate", "dock.unload",
        }, events);
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

    private sealed class TransitionRecordingScene(
        string name, List<string> events, bool run) : IScene, ISceneTransitionParticipant
    {
        public SceneExecutionMode ExecutionMode => SceneExecutionMode.OnDemand;
        public bool TryBeginFrame() => run;
        public Task OnLoadAsync() { events.Add($"{name}.load"); return Task.CompletedTask; }
        public Task OnActivateAsync() { events.Add($"{name}.activate"); return Task.CompletedTask; }
        public Task OnDeactivateAsync() { events.Add($"{name}.deactivate"); return Task.CompletedTask; }
        public Task OnUnloadAsync() { events.Add($"{name}.unload"); return Task.CompletedTask; }
        public void OnSceneTransition(SceneTransitionContext context, SceneTransitionRole role)
            => events.Add($"{name}.{role}:{context.Progress:0.##}");
    }

    private sealed class RecordingDockScene(
        SceneManager manager,
        List<string> events,
        IScene list,
        IScene content) : DockScene(manager)
    {
        protected override IEnumerable<DockSceneSlot> CreateSlots()
        {
            yield return new DockSceneSlot("stories", list);
            yield return new DockSceneSlot("content", content);
        }

        protected override Task OnLoadAsync()
        {
            events.Add("dock.load");
            return Task.CompletedTask;
        }

        protected override Task OnActivateAsync()
        {
            events.Add("dock.activate");
            return Task.CompletedTask;
        }

        protected override Task OnDeactivateAsync()
        {
            events.Add("dock.deactivate");
            return Task.CompletedTask;
        }

        protected override Task OnUnloadAsync()
        {
            events.Add("dock.unload");
            return Task.CompletedTask;
        }
    }
}
