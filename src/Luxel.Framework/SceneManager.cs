using System.Collections.Concurrent;
using Luxel.Input;
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
        SceneNode? Outgoing,
        IScene Scene,
        SceneTransitionSpec Spec,
        TaskCompletionSource Completion) : SceneOperation;
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
    private readonly InputStack? _inputStack;
    private readonly ConcurrentQueue<SceneOperation> _pending = new();
    private readonly HashSet<SceneNode> _inputNodes = new();
    private readonly Dictionary<InputContext, SceneNode> _inputOwners = new();
    private SceneNode? _root;
    private TransitionRuntime? _transition;

    public SceneManager(IServiceProvider services, IFrameScheduler frames)
    {
        _services = services;
        _frames = frames;
        _inputStack = services.GetService<InputStack>();
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
        Enqueue(new BeginTransitionOperation(null, next, transition, completion));
        return completion.Task;
    }

    public Task TransitionAsync<TScene>(SceneNode outgoing, SceneTransitionSpec transition)
        where TScene : class, IScene
        => TransitionAsync(outgoing, CreateScene<TScene>(), transition);

    /// <summary>
    /// SceneGraph内の<paramref name="outgoing"/>だけを<paramref name="next"/>へ遷移する。
    /// 親と兄弟はActiveのまま維持され、outgoing/incomingは遷移中だけ同じ親の下で同時実行される。
    /// </summary>
    public Task TransitionAsync(SceneNode outgoing, IScene next, SceneTransitionSpec transition)
    {
        ArgumentNullException.ThrowIfNull(outgoing);
        ArgumentNullException.ThrowIfNull(next);
        ArgumentNullException.ThrowIfNull(transition);
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Enqueue(new BeginTransitionOperation(outgoing, next, transition, completion));
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
                    if (_transition is { } active
                        && (ContainsNode(remove.Node, active.Outgoing)
                            || ContainsNode(remove.Node, active.Incoming)))
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
        if (_transition is { Incoming.Parent: null })
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
        // 同じoutgoing側を再指定した場合は元へ戻し、それ以外は画面に入りつつあるincomingを採用する。
        if (_transition is not null)
        {
            bool requestedOutgoingStillNeeded = operation.Outgoing is { } requested
                                                && ContainsNode(_transition.Outgoing, requested);
            await CancelTransitionAsync(promoteIncoming: !requestedOutgoingStillNeeded);
        }

        SceneNode? outgoing = operation.Outgoing ?? _root;
        if (outgoing is null)
        {
            if (operation.Outgoing is not null)
                throw new InvalidOperationException("遷移元のSceneNodeは現在のSceneGraphに属していません。");
            _root = CreateNode(operation.Scene, null, null);
            await LoadAndActivateAsync(_root, primary: true);
            operation.Completion.TrySetResult();
            return;
        }
        EnsureAttached(outgoing);
        if (outgoing.State != SceneLifecycleState.Active)
            throw new InvalidOperationException("ActiveではないSceneNodeを遷移元にできません。");

        SceneNode? parent = outgoing.Parent;
        if (parent is null && !ReferenceEquals(outgoing, _root))
            throw new InvalidOperationException("rootではないSceneNodeに親が設定されていません。");
        int outgoingIndex = parent?.MutableChildren.IndexOf(outgoing) ?? -1;
        if (parent is not null && outgoingIndex < 0)
            throw new InvalidOperationException("遷移元のSceneNodeが親の子一覧に存在しません。");

        SceneNode incoming = CreateNode(operation.Scene, null, null, parent);
        if (parent is not null) parent.MutableChildren.Insert(outgoingIndex + 1, incoming);
        try { await LoadAndActivateAsync(incoming, primary: parent is null); }
        catch
        {
            await DeactivateUnloadAndDetachAsync(incoming);
            throw;
        }

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
                await DeactivateUnloadAndDetachAsync(incoming);
                throw;
            }
            SetTransitioning(outgoing, false);
            SetTransitioning(incoming, false);
            if (parent is null) _root = incoming;
            await DeactivateUnloadAndDetachAsync(outgoing);
            operation.Completion.TrySetResult();
            return;
        }

        SetTransitioning(outgoing, true);
        SetTransitioning(incoming, true);
        var runtime = new TransitionRuntime(
            outgoing, incoming, operation.Spec, operation.Completion,
            _frames.AcquireContinuousFrames());
        _transition = runtime;
        RefreshInputRouting();
        try { ApplyTransition(runtime, 0f); }
        catch
        {
            _transition = null;
            SetTransitioning(outgoing, false);
            SetTransitioning(incoming, false);
            runtime.ContinuousLease.Dispose();
            await DeactivateUnloadAndDetachAsync(incoming);
            throw;
        }
    }

    private async Task CompleteTransitionAsync(TransitionRuntime transition)
    {
        if (!ReferenceEquals(_transition, transition)) return;
        _transition = null;
        SetTransitioning(transition.Outgoing, false);
        SetTransitioning(transition.Incoming, false);
        if (transition.Outgoing.Parent is null) _root = transition.Incoming;
        try
        {
            await DeactivateUnloadAndDetachAsync(transition.Outgoing);
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
                if (transition.Outgoing.Parent is null) _root = transition.Incoming;
                await DeactivateUnloadAndDetachAsync(transition.Outgoing);
            }
            else
            {
                await DeactivateUnloadAndDetachAsync(transition.Incoming);
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

    private static bool ContainsNode(SceneNode root, SceneNode candidate)
        => EnumeratePreOrder(root).Contains(candidate);

    private static void DetachNode(SceneNode node)
    {
        if (node.Parent is { } parent) parent.MutableChildren.Remove(node);
        node.Parent = null;
    }

    private async Task DeactivateUnloadAndDetachAsync(SceneNode node)
    {
        try { await DeactivateAndUnloadAsync(node); }
        finally { DetachNode(node); }
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
        RegisterInputContexts(node);
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
        RefreshInputRouting();
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
        RefreshInputRouting();
    }

    private async Task DeactivateAndUnloadAsync(SceneNode node)
    {
        for (int i = node.MutableChildren.Count - 1; i >= 0; i--)
            await DeactivateAndUnloadAsync(node.MutableChildren[i]);
        node.MutableChildren.Clear();

        ReleaseContinuousLease(node);
        if (node.State is SceneLifecycleState.Active or SceneLifecycleState.Suspended)
        {
            UnregisterInputContexts(node);
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
        RefreshInputRouting();
    }

    private void RegisterInputContexts(SceneNode node)
    {
        if (_inputStack is null || node.Scene is not ISceneInputParticipant participant) return;
        _inputNodes.Add(node);
        SynchronizeInputContexts(node, participant);
        RefreshInputRouting();
    }

    private void SynchronizeInputContexts(SceneNode node, ISceneInputParticipant participant)
    {
        if (_inputStack is null) return;
        IReadOnlyList<InputContext> contexts = participant.InputContexts
            ?? throw new InvalidOperationException("ISceneInputParticipant.InputContextsはnullを返せません。");
        var unique = new HashSet<InputContext>();
        foreach (InputContext context in contexts)
        {
            if (context is null)
                throw new InvalidOperationException("Scene所有InputContextにnullは指定できません。");
            if (!unique.Add(context))
                throw new InvalidOperationException("同じInputContextがScene内で重複しています。");
            if (_inputOwners.TryGetValue(context, out SceneNode? owner) && !ReferenceEquals(owner, node))
                throw new InvalidOperationException(
                    $"InputContext '{context.Name}' は既にScene '{owner.Scene.GetType().Name}' が所有しています。");
            if (!_inputOwners.ContainsKey(context) && _inputStack.Contains(context))
                throw new InvalidOperationException(
                    $"InputContext '{context.Name}' は既にInputStackへ登録されています。");
        }

        for (int i = node.ManagedInputContexts.Count - 1; i >= 0; i--)
        {
            InputContext existing = node.ManagedInputContexts[i];
            if (unique.Contains(existing)) continue;
            _inputStack.Remove(existing);
            _inputOwners.Remove(existing);
            node.ManagedInputContexts.RemoveAt(i);
        }

        foreach (InputContext context in contexts)
        {
            if (node.ManagedInputContexts.Contains(context)) continue;
            node.ManagedInputContexts.Add(context);
            _inputOwners.Add(context, node);
            _inputStack.Push(context);
        }
    }

    private void UnregisterInputContexts(SceneNode node)
    {
        if (_inputStack is null || !_inputNodes.Remove(node)) return;
        foreach (InputContext context in node.ManagedInputContexts)
        {
            _inputStack.Remove(context);
            _inputOwners.Remove(context);
        }
        node.ManagedInputContexts.Clear();
        RefreshInputRouting();
    }

    internal void RefreshInputRouting()
    {
        if (_inputStack is null || _inputNodes.Count == 0) return;
        foreach (SceneNode node in _inputNodes)
            if (node.Scene is ISceneInputParticipant participant)
                SynchronizeInputContexts(node, participant);

        SceneNode[] active = GetActiveNodes();
        var activeOrder = new Dictionary<SceneNode, int>(active.Length);
        for (int i = 0; i < active.Length; i++) activeOrder[active[i]] = i;

        int modalIndex = -1;
        for (int i = 0; i < active.Length; i++)
            if (active[i].Scene is ISceneInputParticipant { InputMode: SceneInputMode.Modal })
                modalIndex = i;

        foreach (SceneNode node in _inputNodes)
        {
            bool suspended = !activeOrder.TryGetValue(node, out int index) || index < modalIndex;
            foreach (InputContext context in node.ManagedInputContexts)
                _inputStack.SetSuspended(context, suspended);
        }
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
