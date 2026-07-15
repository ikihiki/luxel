using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;

namespace Luxel.Framework;

/// <summary>
/// SceneGraph、親子関係、遷移、Suspend/Resume、lifecycleを管理する。
/// 変更要求はqueueへ積み、<see cref="SceneRunner"/> がフレーム境界で適用する。
/// </summary>
public sealed class SceneManager
{
    private abstract record SceneOperation;
    private sealed record ReplaceRootOperation(IScene Scene) : SceneOperation;
    private sealed record BeginTransitionOperation(
        IScene Scene, SceneTransitionSpec Spec, TaskCompletionSource Completion) : SceneOperation;
    private sealed record AddChildOperation(SceneNode Parent, IScene Scene,
        SceneExecutionMode? ExecutionMode, SceneRenderMode? RenderMode) : SceneOperation;
    private sealed record RemoveOperation(SceneNode Node) : SceneOperation;
    private sealed record SuspendOperation(SceneNode Node) : SceneOperation;
    private sealed record ResumeOperation(SceneNode Node) : SceneOperation;

    private sealed class TransitionRuntime(
        SceneNode outgoing,
        SceneNode incoming,
        SceneTransitionSpec spec,
        TaskCompletionSource completion,
        IDisposable continuousLease)
    {
        public SceneNode Outgoing { get; } = outgoing;
        public SceneNode Incoming { get; } = incoming;
        public SceneTransitionSpec Spec { get; } = spec;
        public TaskCompletionSource Completion { get; } = completion;
        public IDisposable ContinuousLease { get; } = continuousLease;
        public float ElapsedSeconds { get; set; }
        public bool SkipFirstAdvance { get; set; } = true;
    }

    private readonly IServiceProvider _services;
    private readonly IFrameScheduler _frames;
    private readonly ConcurrentQueue<SceneOperation> _pending = new();
    private SceneNode? _root;
    private TransitionRuntime? _transition;

    public SceneManager(IServiceProvider services, IFrameScheduler frames)
    {
        _services = services;
        _frames = frames;
    }

    public IScene? Current => _root?.Scene;
    public SceneNode? Root => _root;
    public bool IsTransitioning => _transition is not null;
    public SceneNode? TransitionIncoming => _transition?.Incoming;

    public Task SwitchAsync<TScene>() where TScene : class, IScene
        => SwitchAsync(CreateScene<TScene>());

    /// <summary>root Sceneを置換する。要求は安全なフレーム境界で適用される。</summary>
    public Task SwitchAsync(IScene next)
    {
        ArgumentNullException.ThrowIfNull(next);
        Enqueue(new ReplaceRootOperation(next));
        return Task.CompletedTask;
    }

    public Task TransitionAsync<TScene>(SceneTransitionSpec transition)
        where TScene : class, IScene
        => TransitionAsync(CreateScene<TScene>(), transition);

    /// <summary>
    /// 現在のrootをoutgoing、<paramref name="next"/>をincomingとして同時実行する。
    /// 返るTaskは遷移完了後に完了し、hard switchや別遷移で割り込まれた場合はCanceledになる。
    /// </summary>
    public Task TransitionAsync(IScene next, SceneTransitionSpec transition)
    {
        ArgumentNullException.ThrowIfNull(next);
        ArgumentNullException.ThrowIfNull(transition);
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Enqueue(new BeginTransitionOperation(next, transition, completion));
        return completion.Task;
    }

    public Task AddChildAsync<TScene>(SceneNode parent,
        SceneExecutionMode? executionMode = null, SceneRenderMode? renderMode = null)
        where TScene : class, IScene
        => AddChildAsync(parent, CreateScene<TScene>(), executionMode, renderMode);

    /// <summary>既存nodeの子としてSceneを追加し、親と同時にphaseへ参加させる。</summary>
    public Task AddChildAsync(SceneNode parent, IScene child,
        SceneExecutionMode? executionMode = null, SceneRenderMode? renderMode = null)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(child);
        Enqueue(new AddChildOperation(parent, child, executionMode, renderMode));
        return Task.CompletedTask;
    }

    public Task RemoveAsync(SceneNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        Enqueue(new RemoveOperation(node));
        return Task.CompletedTask;
    }

    public Task SuspendAsync(SceneNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        Enqueue(new SuspendOperation(node));
        return Task.CompletedTask;
    }

    public Task ResumeAsync(SceneNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        Enqueue(new ResumeOperation(node));
        return Task.CompletedTask;
    }

    public SceneNode? Find(IScene scene)
    {
        if (_root is not null)
        {
            SceneNode? found = EnumeratePreOrder(_root).FirstOrDefault(n => ReferenceEquals(n.Scene, scene));
            if (found is not null) return found;
        }
        return _transition is null
            ? null
            : EnumeratePreOrder(_transition.Incoming).FirstOrDefault(n => ReferenceEquals(n.Scene, scene));
    }

    /// <summary>startup Sceneから始め、外部cancelまでSceneGraphを実行する。</summary>
    public Task RunLoopAsync(IScene startup, CancellationToken outerToken)
        => _services.GetRequiredService<SceneRunner>().RunAsync(this, startup, outerToken);

    internal async Task InitializeAsync(IScene startup)
    {
        if (_root is not null) return;
        _root = CreateNode(startup, null, null);
        await LoadAndActivateAsync(_root, primary: true);
    }

    internal async Task ApplyPendingAsync()
    {
        while (_pending.TryDequeue(out SceneOperation? operation))
        {
            switch (operation)
            {
                case ReplaceRootOperation replace:
                    if (_transition is not null)
                        await CancelTransitionAsync(promoteIncoming: false);
                    if (_root is not null) await DeactivateAndUnloadAsync(_root);
                    _root = CreateNode(replace.Scene, null, null);
                    await LoadAndActivateAsync(_root, primary: true);
                    break;

                case BeginTransitionOperation begin:
                    try { await BeginTransitionAsync(begin); }
                    catch (Exception ex)
                    {
                        begin.Completion.TrySetException(ex);
                        throw;
                    }
                    break;

                case AddChildOperation add:
                    EnsureAttached(add.Parent);
                    SceneNode child = CreateNode(add.Scene, add.ExecutionMode, add.RenderMode, add.Parent);
                    add.Parent.MutableChildren.Add(child);
                    await LoadAndActivateAsync(child, primary: false);
                    if (add.Parent.IsTransitioning) SetTransitioning(child, true);
                    if (add.Parent.State == SceneLifecycleState.Suspended)
                        await SuspendSubtreeAsync(child);
                    break;

                case RemoveOperation remove:
                    EnsureAttached(remove.Node);
                    if (ReferenceEquals(remove.Node, _transition?.Incoming))
                    {
                        await CancelTransitionAsync(promoteIncoming: false);
                        break;
                    }
                    if (ReferenceEquals(remove.Node, _root) && _transition is not null)
                        await CancelTransitionAsync(promoteIncoming: false);
                    await DeactivateAndUnloadAsync(remove.Node);
                    if (remove.Node.Parent is { } parent) parent.MutableChildren.Remove(remove.Node);
                    else if (ReferenceEquals(remove.Node, _root)) _root = null;
                    remove.Node.Parent = null;
                    break;

                case SuspendOperation suspend:
                    EnsureAttached(suspend.Node);
                    suspend.Node.IsLocallySuspended = true;
                    await SuspendSubtreeAsync(suspend.Node);
                    break;

                case ResumeOperation resume:
                    EnsureAttached(resume.Node);
                    resume.Node.IsLocallySuspended = false;
                    if (resume.Node.Parent is null || resume.Node.Parent.State == SceneLifecycleState.Active)
                        await ResumeSubtreeAsync(resume.Node);
                    break;
            }
        }
    }

    internal SceneNode[] GetActiveNodes()
    {
        IEnumerable<SceneNode> nodes = _root is null
            ? Enumerable.Empty<SceneNode>()
            : EnumeratePreOrder(_root);
        if (_transition is not null)
            nodes = nodes.Concat(EnumeratePreOrder(_transition.Incoming));
        return nodes.Where(n => n.State == SceneLifecycleState.Active).ToArray();
    }

    /// <summary>SceneRunnerがphase dispatch前に遷移時計を進める。</summary>
    internal async Task AdvanceTransitionAsync(float deltaSeconds)
    {
        TransitionRuntime? transition = _transition;
        if (transition is null) return;
        if (transition.SkipFirstAdvance)
        {
            transition.SkipFirstAdvance = false;
            return; // 開始frameはprogress=0を必ず1回描画する
        }

        transition.ElapsedSeconds += Math.Clamp(deltaSeconds, 0f, 0.1f);
        float linear = transition.Spec.DurationSeconds <= 0f
            ? 1f
            : Math.Clamp(transition.ElapsedSeconds / transition.Spec.DurationSeconds, 0f, 1f);
        ApplyTransition(transition, linear);
        if (linear >= 1f) await CompleteTransitionAsync(transition);
    }

    internal async Task ShutdownAsync()
    {
        if (_transition is not null)
            await CancelTransitionAsync(promoteIncoming: false);
        while (_pending.TryDequeue(out SceneOperation? operation))
            if (operation is BeginTransitionOperation transition)
                transition.Completion.TrySetCanceled();
        if (_root is null) return;
        await DeactivateAndUnloadAsync(_root);
        _root = null;
    }

    private TScene CreateScene<TScene>() where TScene : class, IScene
        => (TScene?)_services.GetService(typeof(TScene))
           ?? ActivatorUtilities.CreateInstance<TScene>(_services);

    private void Enqueue(SceneOperation operation)
    {
        _pending.Enqueue(operation);
        _frames.RequestFrame();
    }

    private async Task BeginTransitionAsync(BeginTransitionOperation operation)
    {
        // 新しい遷移で割り込む場合は、画面に入りつつあるincomingを次のoutgoingとして採用する。
        if (_transition is not null)
            await CancelTransitionAsync(promoteIncoming: true);

        if (_root is null)
        {
            _root = CreateNode(operation.Scene, null, null);
            await LoadAndActivateAsync(_root, primary: true);
            operation.Completion.TrySetResult();
            return;
        }

        SceneNode incoming = CreateNode(operation.Scene, null, null);
        try { await LoadAndActivateAsync(incoming, primary: true); }
        catch
        {
            await DeactivateAndUnloadAsync(incoming);
            throw;
        }
        SceneNode outgoing = _root;

        if (operation.Spec.DurationSeconds <= 0f)
        {
            var immediate = new TransitionRuntime(
                outgoing, incoming, operation.Spec, operation.Completion,
                NoopDisposable.Instance);
            SetTransitioning(outgoing, true);
            SetTransitioning(incoming, true);
            try { ApplyTransition(immediate, 1f); }
            catch
            {
                SetTransitioning(outgoing, false);
                SetTransitioning(incoming, false);
                await DeactivateAndUnloadAsync(incoming);
                throw;
            }
            SetTransitioning(outgoing, false);
            SetTransitioning(incoming, false);
            _root = incoming;
            await DeactivateAndUnloadAsync(outgoing);
            operation.Completion.TrySetResult();
            return;
        }

        SetTransitioning(outgoing, true);
        SetTransitioning(incoming, true);
        var runtime = new TransitionRuntime(
            outgoing, incoming, operation.Spec, operation.Completion,
            _frames.AcquireContinuousFrames());
        _transition = runtime;
        try { ApplyTransition(runtime, 0f); }
        catch
        {
            _transition = null;
            SetTransitioning(outgoing, false);
            SetTransitioning(incoming, false);
            runtime.ContinuousLease.Dispose();
            await DeactivateAndUnloadAsync(incoming);
            throw;
        }
    }

    private async Task CompleteTransitionAsync(TransitionRuntime transition)
    {
        if (!ReferenceEquals(_transition, transition)) return;
        _transition = null;
        SetTransitioning(transition.Outgoing, false);
        SetTransitioning(transition.Incoming, false);
        _root = transition.Incoming;
        try
        {
            await DeactivateAndUnloadAsync(transition.Outgoing);
            transition.Completion.TrySetResult();
        }
        catch (Exception ex)
        {
            transition.Completion.TrySetException(ex);
            throw;
        }
        finally
        {
            transition.ContinuousLease.Dispose();
        }
    }

    private async Task CancelTransitionAsync(bool promoteIncoming)
    {
        TransitionRuntime? transition = _transition;
        if (transition is null) return;
        _transition = null;
        SetTransitioning(transition.Outgoing, false);
        SetTransitioning(transition.Incoming, false);
        transition.ContinuousLease.Dispose();

        try
        {
            if (promoteIncoming)
            {
                _root = transition.Incoming;
                await DeactivateAndUnloadAsync(transition.Outgoing);
            }
            else
            {
                await DeactivateAndUnloadAsync(transition.Incoming);
            }
        }
        finally
        {
            transition.Completion.TrySetCanceled();
        }
    }

    private static void ApplyTransition(TransitionRuntime transition, float linearProgress)
    {
        float linear = Math.Clamp(linearProgress, 0f, 1f);
        var context = new SceneTransitionContext(
            transition.Outgoing,
            transition.Incoming,
            linear,
            transition.Spec.Evaluate(linear));
        if (transition.Outgoing.Scene is ISceneTransitionParticipant outgoing)
            outgoing.OnSceneTransition(context, SceneTransitionRole.Outgoing);
        if (transition.Incoming.Scene is ISceneTransitionParticipant incoming)
            incoming.OnSceneTransition(context, SceneTransitionRole.Incoming);
        transition.Spec.Apply?.Invoke(context);
    }

    private static void SetTransitioning(SceneNode root, bool value)
    {
        foreach (SceneNode node in EnumeratePreOrder(root)) node.IsTransitioning = value;
    }

    private static SceneNode CreateNode(
        IScene scene,
        SceneExecutionMode? executionMode,
        SceneRenderMode? renderMode,
        SceneNode? parent = null)
    {
        var node = new SceneNode(
            scene,
            executionMode ?? scene.ExecutionMode,
            renderMode ?? scene.RenderMode)
        {
            Parent = parent,
        };
        scene.OnAttached(node);
        return node;
    }

    private async Task LoadAndActivateAsync(SceneNode node, bool primary)
    {
        node.State = SceneLifecycleState.Loading;
        await node.Scene.OnLoadAsync();
        node.State = SceneLifecycleState.Loaded;
        if (node.Scene is GameScene game) game.AttachToRunner(primary);
        await node.Scene.OnActivateAsync();
        node.State = SceneLifecycleState.Active;
        AcquireContinuousLease(node);
    }

    private async Task SuspendSubtreeAsync(SceneNode node)
    {
        if (node.State == SceneLifecycleState.Active)
        {
            ReleaseContinuousLease(node);
            await node.Scene.OnSuspendAsync();
            node.State = SceneLifecycleState.Suspended;
            node.FixedTimestep?.Reset();
            node.LastRunAt = null;
        }
        foreach (SceneNode child in node.MutableChildren)
            await SuspendSubtreeAsync(child);
    }

    private async Task ResumeSubtreeAsync(SceneNode node)
    {
        if (node.IsLocallySuspended) return;
        if (node.State == SceneLifecycleState.Suspended)
        {
            await node.Scene.OnResumeAsync();
            node.State = SceneLifecycleState.Active;
            node.FixedTimestep?.Reset();
            node.LastRunAt = null;
            AcquireContinuousLease(node);
        }
        foreach (SceneNode child in node.MutableChildren)
            await ResumeSubtreeAsync(child);
    }

    private async Task DeactivateAndUnloadAsync(SceneNode node)
    {
        for (int i = node.MutableChildren.Count - 1; i >= 0; i--)
            await DeactivateAndUnloadAsync(node.MutableChildren[i]);
        node.MutableChildren.Clear();

        ReleaseContinuousLease(node);
        if (node.State is SceneLifecycleState.Active or SceneLifecycleState.Suspended)
        {
            await node.Scene.OnDeactivateAsync();
            node.State = SceneLifecycleState.Loaded;
        }
        if (node.State is SceneLifecycleState.Loaded or SceneLifecycleState.Loading)
        {
            node.State = SceneLifecycleState.Unloading;
            await node.Scene.OnUnloadAsync();
        }
        node.State = SceneLifecycleState.Unloaded;
        node.Scene.OnDetached(node);
    }

    private void AcquireContinuousLease(SceneNode node)
    {
        if (node.EffectiveExecutionMode == SceneExecutionMode.Continuous)
            node.ContinuousLease ??= _frames.AcquireContinuousFrames();
    }

    private static void ReleaseContinuousLease(SceneNode node)
    {
        node.ContinuousLease?.Dispose();
        node.ContinuousLease = null;
    }

    private void EnsureAttached(SceneNode node)
    {
        bool attached = _root is not null && EnumeratePreOrder(_root).Contains(node);
        attached |= _transition is not null && EnumeratePreOrder(_transition.Incoming).Contains(node);
        if (!attached)
            throw new InvalidOperationException("SceneNodeは現在のSceneGraphに属していません。");
    }

    private static IEnumerable<SceneNode> EnumeratePreOrder(SceneNode node)
    {
        yield return node;
        foreach (SceneNode child in node.Children)
            foreach (SceneNode descendant in EnumeratePreOrder(child))
                yield return descendant;
    }

    private sealed class NoopDisposable : IDisposable
    {
        public static NoopDisposable Instance { get; } = new();
        public void Dispose() { }
    }
}
