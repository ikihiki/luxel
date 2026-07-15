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
    private abstract record SceneOperation(TaskCompletionSource Completion);
    private sealed record ReplaceRootOperation(
        IScene Scene,
        TaskCompletionSource OperationCompletion) : SceneOperation(OperationCompletion);
    private sealed record BeginTransitionOperation(
        SceneNode? Outgoing,
        IScene Scene,
        SceneTransitionSpec Spec,
        TaskCompletionSource OperationCompletion) : SceneOperation(OperationCompletion);
    private sealed record AddChildOperation(SceneNode Parent, IScene Scene,
        SceneExecutionMode? ExecutionMode, SceneRenderMode? RenderMode,
        TaskCompletionSource OperationCompletion) : SceneOperation(OperationCompletion);
    private sealed record RemoveOperation(
        SceneNode Node,
        TaskCompletionSource OperationCompletion) : SceneOperation(OperationCompletion);
    private sealed record SuspendOperation(
        SceneNode Node,
        TaskCompletionSource OperationCompletion) : SceneOperation(OperationCompletion);
    private sealed record ResumeOperation(
        SceneNode Node,
        TaskCompletionSource OperationCompletion) : SceneOperation(OperationCompletion);

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
    private readonly List<TransitionRuntime> _transitions = new();
    private SceneNode? _root;

    public SceneManager(IServiceProvider services, IFrameScheduler frames)
    {
        _services = services;
        _frames = frames;
        _inputStack = services.GetService<InputStack>();
    }

    public IScene? Current => _root?.Scene;
    public SceneNode? Root => _root;
    public bool IsTransitioning => _transitions.Count > 0;
    /// <summary>直近に開始した遷移のincoming。複数遷移の列挙には<see cref="TransitionIncomings"/>を使う。</summary>
    public SceneNode? TransitionIncoming => _transitions.Count == 0 ? null : _transitions[^1].Incoming;
    public IReadOnlyList<SceneNode> TransitionIncomings
        => _transitions.Select(transition => transition.Incoming).ToArray();

    public Task SwitchAsync<TScene>() where TScene : class, IScene
        => SwitchAsync(CreateScene<TScene>());

    /// <summary>
    /// root Sceneを安全なフレーム境界で置換する。
    /// 返るTaskは新しいSceneのLoad/Activate完了後に完了する。
    /// </summary>
    public Task SwitchAsync(IScene next)
    {
        ArgumentNullException.ThrowIfNull(next);
        return Enqueue(new ReplaceRootOperation(next, CreateCompletion()));
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
        return Enqueue(new BeginTransitionOperation(
            null, next, transition, CreateCompletion()));
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
        return Enqueue(new BeginTransitionOperation(
            outgoing, next, transition, CreateCompletion()));
    }

    public Task AddChildAsync<TScene>(SceneNode parent,
        SceneExecutionMode? executionMode = null, SceneRenderMode? renderMode = null)
        where TScene : class, IScene
        => AddChildAsync(parent, CreateScene<TScene>(), executionMode, renderMode);

    /// <summary>
    /// 既存nodeの子としてSceneを追加し、親と同時にphaseへ参加させる。
    /// 返るTaskは子SceneのLoad/Activate完了後に完了する。
    /// </summary>
    public Task AddChildAsync(SceneNode parent, IScene child,
        SceneExecutionMode? executionMode = null, SceneRenderMode? renderMode = null)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(child);
        return Enqueue(new AddChildOperation(
            parent, child, executionMode, renderMode, CreateCompletion()));
    }

    /// <summary>指定subtreeをフレーム境界で削除し、Unload完了後にTaskを完了する。</summary>
    public Task RemoveAsync(SceneNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return Enqueue(new RemoveOperation(node, CreateCompletion()));
    }

    /// <summary>指定subtreeをフレーム境界でSuspendし、完了後にTaskを完了する。</summary>
    public Task SuspendAsync(SceneNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return Enqueue(new SuspendOperation(node, CreateCompletion()));
    }

    /// <summary>指定subtreeをフレーム境界でResumeし、完了後にTaskを完了する。</summary>
    public Task ResumeAsync(SceneNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return Enqueue(new ResumeOperation(node, CreateCompletion()));
    }

    public SceneNode? Find(IScene scene)
    {
        if (_root is not null)
        {
            SceneNode? found = EnumeratePreOrder(_root).FirstOrDefault(n => ReferenceEquals(n.Scene, scene));
            if (found is not null) return found;
        }
        foreach (TransitionRuntime transition in _transitions)
        {
            SceneNode? found = EnumeratePreOrder(transition.Incoming)
                .FirstOrDefault(n => ReferenceEquals(n.Scene, scene));
            if (found is not null) return found;
        }
        return null;
    }

    /// <summary>startup Sceneから始め、外部cancelまでSceneGraphを実行する。</summary>
    public Task RunLoopAsync(IScene startup, CancellationToken outerToken)
        => _services.GetRequiredService<SceneRunner>().RunAsync(this, startup, outerToken);

    internal async Task InitializeAsync(IScene startup)
    {
        if (_root is not null) return;
        _root = CreateNode(startup, null, null);
        try { await LoadAndActivateAsync(_root, primary: true); }
        catch (Exception ex)
        {
            await RollbackNewNodeAsync(_root, ex);
            throw;
        }
    }

    internal async Task ApplyPendingAsync()
    {
        while (_pending.TryDequeue(out SceneOperation? operation))
        {
            try
            {
                switch (operation)
                {
                    case ReplaceRootOperation replace:
                        await CancelAllTransitionsAsync(promoteIncoming: false);
                        if (_root is not null)
                        {
                            SceneNode previous = _root;
                            _root = null;
                            await DeactivateUnloadAndDetachAsync(previous);
                        }
                        _root = CreateNode(replace.Scene, null, null);
                        try { await LoadAndActivateAsync(_root, primary: true); }
                        catch (Exception ex)
                        {
                            await RollbackNewNodeAsync(_root, ex);
                            throw;
                        }
                        break;

                    case BeginTransitionOperation begin:
                        await BeginTransitionAsync(begin);
                        break;

                    case AddChildOperation add:
                        EnsureAttached(add.Parent);
                        SceneNode child = CreateNode(add.Scene, add.ExecutionMode, add.RenderMode, add.Parent);
                        add.Parent.MutableChildren.Add(child);
                        try
                        {
                            await LoadAndActivateAsync(child, primary: false);
                            if (add.Parent.IsTransitioning) SetTransitioning(child, true);
                            if (add.Parent.State == SceneLifecycleState.Suspended)
                                await SuspendSubtreeAsync(child);
                        }
                        catch (Exception ex)
                        {
                            await RollbackNewNodeAsync(child, ex);
                            throw;
                        }
                        break;

                    case RemoveOperation remove:
                        EnsureAttached(remove.Node);
                        TransitionRuntime? incomingTransition = _transitions
                            .FirstOrDefault(transition => ReferenceEquals(remove.Node, transition.Incoming));
                        if (incomingTransition is not null)
                        {
                            await CancelTransitionAsync(incomingTransition, promoteIncoming: false);
                            break;
                        }
                        TransitionRuntime[] affectedTransitions = _transitions
                            .Where(active => ContainsNode(remove.Node, active.Outgoing)
                                             || ContainsNode(remove.Node, active.Incoming))
                            .ToArray();
                        foreach (TransitionRuntime active in affectedTransitions)
                            await CancelTransitionAsync(active, promoteIncoming: false);
                        if (ReferenceEquals(remove.Node, _root)) _root = null;
                        await DeactivateUnloadAndDetachAsync(remove.Node);
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

                // Transitionはduration完了時に通知する。それ以外はこのフレーム境界で適用済み。
                if (operation is not BeginTransitionOperation)
                    operation.Completion.TrySetResult();
            }
            catch (Exception ex)
            {
                operation.Completion.TrySetException(ex);
                throw;
            }
        }
    }

    internal SceneNode[] GetActiveNodes()
    {
        IEnumerable<SceneNode> nodes = _root is null
            ? Enumerable.Empty<SceneNode>()
            : EnumeratePreOrder(_root);
        foreach (TransitionRuntime transition in _transitions)
            if (transition.Incoming.Parent is null)
                nodes = nodes.Concat(EnumeratePreOrder(transition.Incoming));
        return nodes.Where(n => n.State == SceneLifecycleState.Active).ToArray();
    }

    /// <summary>SceneRunnerがphase dispatch前に遷移時計を進める。</summary>
    internal async Task AdvanceTransitionAsync(float deltaSeconds)
    {
        foreach (TransitionRuntime transition in _transitions.ToArray())
        {
            if (!_transitions.Contains(transition)) continue;
            if (transition.SkipFirstAdvance)
            {
                transition.SkipFirstAdvance = false;
                continue; // 開始frameはprogress=0を必ず1回描画する
            }

            transition.ElapsedSeconds += Math.Clamp(deltaSeconds, 0f, 0.1f);
            float linear = transition.Spec.DurationSeconds <= 0f
                ? 1f
                : Math.Clamp(transition.ElapsedSeconds / transition.Spec.DurationSeconds, 0f, 1f);
            ApplyTransition(transition, linear);
            if (linear >= 1f) await CompleteTransitionAsync(transition);
        }
    }

    internal async Task ShutdownAsync()
    {
        await CancelAllTransitionsAsync(promoteIncoming: false);
        while (_pending.TryDequeue(out SceneOperation? operation))
            operation.Completion.TrySetCanceled();
        if (_root is null) return;
        SceneNode root = _root;
        _root = null;
        await DeactivateUnloadAndDetachAsync(root);
    }

    private TScene CreateScene<TScene>() where TScene : class, IScene
        => (TScene?)_services.GetService(typeof(TScene))
           ?? ActivatorUtilities.CreateInstance<TScene>(_services);

    private static TaskCompletionSource CreateCompletion()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private Task Enqueue(SceneOperation operation)
    {
        _pending.Enqueue(operation);
        _frames.RequestFrame();
        return operation.Completion.Task;
    }

    private async Task BeginTransitionAsync(BeginTransitionOperation operation)
    {
        if (operation.Outgoing is null)
        {
            // root遷移はGraph全体を置換するため、進行中の部分遷移をincoming側へ確定してから始める。
            await CancelAllTransitionsAsync(promoteIncoming: true);
        }
        else
        {
            SceneNode requested = operation.Outgoing;
            TransitionRuntime[] conflicts = _transitions
                .Where(active => TransitionsOverlap(requested, active))
                .ToArray();
            foreach (TransitionRuntime active in conflicts)
            {
                bool requestedOutgoingStillNeeded = ContainsNode(active.Outgoing, requested);
                await CancelTransitionAsync(active, promoteIncoming: !requestedOutgoingStillNeeded);
            }
        }

        SceneNode? outgoing = operation.Outgoing ?? _root;
        if (outgoing is null)
        {
            if (operation.Outgoing is not null)
                throw new InvalidOperationException("遷移元のSceneNodeは現在のSceneGraphに属していません。");
            _root = CreateNode(operation.Scene, null, null);
            try { await LoadAndActivateAsync(_root, primary: true); }
            catch (Exception ex)
            {
                await RollbackNewNodeAsync(_root, ex);
                throw;
            }
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
        catch (Exception ex)
        {
            await RollbackNewNodeAsync(incoming, ex);
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
        _transitions.Add(runtime);
        RefreshInputRouting();
        try { ApplyTransition(runtime, 0f); }
        catch
        {
            _transitions.Remove(runtime);
            SetTransitioning(outgoing, false);
            SetTransitioning(incoming, false);
            runtime.ContinuousLease.Dispose();
            await DeactivateUnloadAndDetachAsync(incoming);
            throw;
        }
    }

    private async Task CompleteTransitionAsync(TransitionRuntime transition)
    {
        if (!_transitions.Remove(transition)) return;
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

    private async Task CancelTransitionAsync(TransitionRuntime transition, bool promoteIncoming)
    {
        if (!_transitions.Remove(transition)) return;
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

    private async Task CancelAllTransitionsAsync(bool promoteIncoming)
    {
        foreach (TransitionRuntime transition in _transitions.ToArray())
            await CancelTransitionAsync(transition, promoteIncoming);
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

    private static bool TransitionsOverlap(SceneNode requested, TransitionRuntime active)
        => NodesOverlap(requested, active.Outgoing) || NodesOverlap(requested, active.Incoming);

    private static bool NodesOverlap(SceneNode first, SceneNode second)
        => ContainsNode(first, second) || ContainsNode(second, first);

    private static void DetachNode(SceneNode node)
    {
        if (node.Parent is { } parent) parent.MutableChildren.Remove(node);
        node.Parent = null;
    }

    private async Task DeactivateUnloadAndDetachAsync(SceneNode node)
    {
        try { await DeactivateAndUnloadAsync(node); }
        finally
        {
            DetachNode(node);
            node.Scene.OnDetached(node);
        }
    }

    private async Task RollbackNewNodeAsync(SceneNode node, Exception activationError)
    {
        if (ReferenceEquals(_root, node)) _root = null;
        try { await DeactivateUnloadAndDetachAsync(node); }
        catch (Exception rollbackError)
        {
            throw new AggregateException(
                "新規Sceneの適用失敗後のロールバックにも失敗しました。",
                activationError,
                rollbackError);
        }
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
        while (node.MutableChildren.Count > 0)
            await DeactivateUnloadAndDetachAsync(node.MutableChildren[^1]);

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
        attached |= _transitions.Any(transition =>
            EnumeratePreOrder(transition.Incoming).Contains(node));
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
