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
    private sealed record AddChildOperation(SceneNode Parent, IScene Scene,
        SceneExecutionMode? ExecutionMode, SceneRenderMode? RenderMode) : SceneOperation;
    private sealed record RemoveOperation(SceneNode Node) : SceneOperation;
    private sealed record SuspendOperation(SceneNode Node) : SceneOperation;
    private sealed record ResumeOperation(SceneNode Node) : SceneOperation;

    private readonly IServiceProvider _services;
    private readonly IFrameScheduler _frames;
    private readonly ConcurrentQueue<SceneOperation> _pending = new();
    private SceneNode? _root;

    public SceneManager(IServiceProvider services, IFrameScheduler frames)
    {
        _services = services;
        _frames = frames;
    }

    public IScene? Current => _root?.Scene;
    public SceneNode? Root => _root;

    public Task SwitchAsync<TScene>() where TScene : class, IScene
        => SwitchAsync(CreateScene<TScene>());

    /// <summary>root Sceneを置換する。要求は安全なフレーム境界で適用される。</summary>
    public Task SwitchAsync(IScene next)
    {
        ArgumentNullException.ThrowIfNull(next);
        Enqueue(new ReplaceRootOperation(next));
        return Task.CompletedTask;
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
        => _root is null ? null : EnumeratePreOrder(_root).FirstOrDefault(n => ReferenceEquals(n.Scene, scene));

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
                    if (_root is not null) await DeactivateAndUnloadAsync(_root);
                    _root = CreateNode(replace.Scene, null, null);
                    await LoadAndActivateAsync(_root, primary: true);
                    break;

                case AddChildOperation add:
                    EnsureAttached(add.Parent);
                    SceneNode child = CreateNode(add.Scene, add.ExecutionMode, add.RenderMode);
                    child.Parent = add.Parent;
                    add.Parent.MutableChildren.Add(child);
                    await LoadAndActivateAsync(child, primary: false);
                    if (add.Parent.State == SceneLifecycleState.Suspended)
                        await SuspendSubtreeAsync(child);
                    break;

                case RemoveOperation remove:
                    EnsureAttached(remove.Node);
                    await DeactivateAndUnloadAsync(remove.Node);
                    if (remove.Node.Parent is { } parent) parent.MutableChildren.Remove(remove.Node);
                    else _root = null;
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
        => _root is null
            ? Array.Empty<SceneNode>()
            : EnumeratePreOrder(_root).Where(n => n.State == SceneLifecycleState.Active).ToArray();

    internal async Task ShutdownAsync()
    {
        if (_root is null) return;
        await DeactivateAndUnloadAsync(_root);
        _root = null;
        while (_pending.TryDequeue(out _)) { }
    }

    private TScene CreateScene<TScene>() where TScene : class, IScene
        => (TScene?)_services.GetService(typeof(TScene))
           ?? ActivatorUtilities.CreateInstance<TScene>(_services);

    private void Enqueue(SceneOperation operation)
    {
        _pending.Enqueue(operation);
        _frames.RequestFrame();
    }

    private static SceneNode CreateNode(IScene scene, SceneExecutionMode? executionMode, SceneRenderMode? renderMode)
        => new(scene, executionMode ?? scene.ExecutionMode, renderMode ?? scene.RenderMode);

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
        if (_root is null || !EnumeratePreOrder(_root).Contains(node))
            throw new InvalidOperationException("SceneNodeは現在のSceneGraphに属していません。");
    }

    private static IEnumerable<SceneNode> EnumeratePreOrder(SceneNode node)
    {
        yield return node;
        foreach (SceneNode child in node.Children)
            foreach (SceneNode descendant in EnumeratePreOrder(child))
                yield return descendant;
    }
}
