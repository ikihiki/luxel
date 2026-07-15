using Luxel.Framework;
using Luxel.Input;
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
    public async Task ChildTransition_KeepsParentAndSiblingActive()
    {
        var services = new ServiceCollection().BuildServiceProvider();
        var manager = new SceneManager(services, new RecordingFrameScheduler());
        var events = new List<string>();
        var root = new RecordingScene("root", events, SceneExecutionMode.OnDemand);
        var outgoing = new TransitionRecordingScene("out", events, run: true);
        var sibling = new RecordingScene("sibling", events, SceneExecutionMode.OnDemand);
        var incoming = new TransitionRecordingScene("in", events, run: true);

        await manager.InitializeAsync(root);
        await manager.AddChildAsync(manager.Root!, outgoing);
        await manager.AddChildAsync(manager.Root!, sibling);
        await manager.ApplyPendingAsync();
        SceneNode outgoingNode = manager.Find(outgoing)!;
        SceneNode siblingNode = manager.Find(sibling)!;

        Task completion = manager.TransitionAsync(
            outgoingNode, incoming, new SceneTransitionSpec(0.1f));
        await manager.ApplyPendingAsync();

        Assert.Same(root, manager.Current);
        Assert.Equal(new IScene[] { outgoing, incoming, sibling },
            manager.Root!.Children.Select(n => n.Scene));
        Assert.Equal(4, manager.GetActiveNodes().Length);
        Assert.Equal(SceneLifecycleState.Active, siblingNode.State);

        await manager.AdvanceTransitionAsync(0.1f); // progress=0 frame
        await manager.AdvanceTransitionAsync(0.1f);
        await completion;

        Assert.Same(root, manager.Current);
        Assert.Equal(new IScene[] { incoming, sibling },
            manager.Root.Children.Select(n => n.Scene));
        Assert.Null(outgoingNode.Parent);
        Assert.Same(manager.Root, manager.Find(incoming)!.Parent);
        Assert.Equal(SceneLifecycleState.Active, siblingNode.State);
        Assert.DoesNotContain("root.unload", events);
        Assert.DoesNotContain("sibling.unload", events);
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

    [Fact]
    public async Task DockScene_TransitionsOnlySelectedSlot()
    {
        var services = new ServiceCollection().BuildServiceProvider();
        var manager = new SceneManager(services, new RecordingFrameScheduler());
        var events = new List<string>();
        var list = new RecordingScene("list", events, SceneExecutionMode.OnDemand);
        var first = new TransitionRecordingScene("first", events, run: true);
        var next = new TransitionRecordingScene("next", events, run: true);
        var dock = new RecordingDockScene(manager, events, list, first);

        await manager.InitializeAsync(dock);
        await manager.ApplyPendingAsync();
        SceneNode listNode = dock.GetSlotNode("stories")!;

        Task completion = dock.TransitionSlotAsync(
            "content", next, new SceneTransitionSpec(0.1f));
        await manager.ApplyPendingAsync();

        Assert.True(manager.IsTransitioning);
        Assert.Same(listNode, dock.GetSlotNode("stories"));
        Assert.Equal(SceneLifecycleState.Active, listNode.State);
        Assert.Equal(3, manager.Root!.Children.Count); // list + outgoing + incoming

        await manager.AdvanceTransitionAsync(0.1f);
        await manager.AdvanceTransitionAsync(0.1f);
        await completion;

        Assert.False(manager.IsTransitioning);
        Assert.Same(next, dock.GetSlotNode("content")!.Scene);
        Assert.Same(listNode, dock.GetSlotNode("stories"));
        Assert.DoesNotContain("list.unload", events);
        Assert.Contains("first.unload", events);
        await manager.ShutdownAsync();
    }

    [Fact]
    public async Task ModalScene_SuspendsLowerSceneInputUntilRemoved()
    {
        var input = new InputStack();
        var services = new ServiceCollection().AddSingleton(input).BuildServiceProvider();
        var manager = new SceneManager(services, new RecordingFrameScheduler());
        var gameplayContext = new InputContext("gameplay");
        ButtonAction move = gameplayContext.Add(new ButtonAction("Move", KeyCode.D));
        var menuContext = new InputContext("menu");
        var gameplay = new InputRecordingScene(gameplayContext, SceneInputMode.Shared);
        var menu = new InputRecordingScene(menuContext, SceneInputMode.Modal);

        await manager.InitializeAsync(gameplay);
        await manager.AddChildAsync(manager.Root!, menu);
        await manager.ApplyPendingAsync();

        Assert.Equal(new[] { gameplayContext, menuContext }, input.Contexts);
        Assert.True(input.IsSuspended(gameplayContext));
        Assert.False(input.IsSuspended(menuContext));
        var bus = new InputBus();
        bus.EnqueueKey(KeyCode.D, down: true);
        input.Update(bus);
        Assert.False(move.IsActive.Value);

        await manager.RemoveAsync(manager.Find(menu)!);
        await manager.ApplyPendingAsync();

        Assert.Equal(new[] { gameplayContext }, input.Contexts);
        Assert.False(input.IsSuspended(gameplayContext));
        input.Update(bus); // held状態はInputStackが維持している
        Assert.True(move.IsActive.Value);
        await manager.ShutdownAsync();
        Assert.Empty(input.Contexts);
    }

    [Fact]
    public async Task OverlayScene_HoldsReferenceCountedPauseWhileActive()
    {
        var input = new InputStack();
        var services = new ServiceCollection().AddSingleton(input).BuildServiceProvider();
        var frames = new RecordingFrameScheduler();
        var manager = new SceneManager(services, frames);
        var gameplay = new RecordingPausableScene();
        var first = new RecordingOverlayScene(frames, "first");
        var second = new RecordingOverlayScene(frames, "second");

        await manager.InitializeAsync(gameplay);
        await manager.AddChildAsync(manager.Root!, first);
        await manager.AddChildAsync(manager.Root!, second);
        await manager.ApplyPendingAsync();

        Assert.True(gameplay.IsPaused);
        Assert.Equal(2, gameplay.PauseRequests);
        Assert.Equal(new[] { first.Context, second.Context }, input.Contexts);
        Assert.True(input.IsSuspended(first.Context));
        Assert.False(input.IsSuspended(second.Context));

        await manager.RemoveAsync(manager.Find(first)!);
        await manager.ApplyPendingAsync();
        Assert.True(gameplay.IsPaused);
        Assert.Equal(1, gameplay.PauseRequests);
        Assert.Equal(new[] { second.Context }, input.Contexts);

        await manager.RemoveAsync(manager.Find(second)!);
        await manager.ApplyPendingAsync();
        Assert.False(gameplay.IsPaused);
        Assert.Equal(0, gameplay.PauseRequests);
        Assert.Empty(input.Contexts);
        await manager.ShutdownAsync();
    }

    [Fact]
    public async Task NavigationScene_NavigateAndGoBack_ReloadsPreviousScene()
    {
        var services = new ServiceCollection().BuildServiceProvider();
        var manager = new SceneManager(services, new RecordingFrameScheduler());
        var events = new List<string>();
        var first = new RecordingScene("first", events, SceneExecutionMode.OnDemand);
        var second = new RecordingScene("second", events, SceneExecutionMode.OnDemand);
        var navigation = new RecordingNavigationScene(manager, first);

        await manager.InitializeAsync(navigation);
        await manager.ApplyPendingAsync();
        Assert.Same(first, navigation.Current);
        Assert.Same(manager.Root, navigation.CurrentNode!.Parent);
        Assert.False(navigation.CanGoBack);

        Task navigate = navigation.NavigateAsync(second, new SceneTransitionSpec(0.1f));
        await manager.ApplyPendingAsync();
        await manager.AdvanceTransitionAsync(0.1f);
        await manager.AdvanceTransitionAsync(0.1f);
        await navigate;

        Assert.Same(second, navigation.Current);
        Assert.True(navigation.CanGoBack);
        Assert.Equal(1, navigation.HistoryCount);
        Assert.Same(second, Assert.Single(manager.Root!.Children).Scene);

        Task<bool> goBack = navigation.GoBackAsync(new SceneTransitionSpec(0.1f));
        await manager.ApplyPendingAsync();
        await manager.AdvanceTransitionAsync(0.1f);
        await manager.AdvanceTransitionAsync(0.1f);
        Assert.True(await goBack);

        Assert.Same(first, navigation.Current);
        Assert.False(navigation.CanGoBack);
        Assert.Equal(2, events.Count(e => e == "first.load"));
        Assert.Equal(2, events.Count(e => e == "first.activate"));
        Assert.Contains("second.unload", events);
        Assert.False(await navigation.GoBackAsync(new SceneTransitionSpec(0f)));
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

    private sealed class InputRecordingScene(
        InputContext context,
        SceneInputMode mode) : IScene, ISceneInputParticipant
    {
        public SceneInputMode InputMode => mode;
        public IReadOnlyList<InputContext> InputContexts { get; } = new[] { context };
    }

    private sealed class RecordingPausableScene : IScene, IPausableScene
    {
        public int PauseRequests { get; private set; }
        public bool IsPaused => PauseRequests > 0;

        public IDisposable AcquirePause()
        {
            PauseRequests++;
            return new Lease(this);
        }

        private sealed class Lease(RecordingPausableScene owner) : IDisposable
        {
            private RecordingPausableScene? _owner = owner;

            public void Dispose()
            {
                RecordingPausableScene? value = Interlocked.Exchange(ref _owner, null);
                if (value is not null) value.PauseRequests--;
            }
        }
    }

    private sealed class RecordingOverlayScene : OverlayScene
    {
        public RecordingOverlayScene(IFrameScheduler frames, string name) : base(frames)
        {
            Context = new InputContext(name);
            AddInputContext(Context);
        }

        public InputContext Context { get; }
    }

    private sealed class RecordingNavigationScene(
        SceneManager manager,
        IScene initial) : NavigationScene(manager)
    {
        protected override IScene CreateInitialScene() => initial;
    }
}
