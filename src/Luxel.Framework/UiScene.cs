using Luxel.UI;

namespace Luxel.Framework;

/// <summary>
/// 要求駆動UI向けのScene基底。登録した <see cref="UiHost"/> のdirtyとanimationをまとめ、
/// 通常は変更されたフレームだけ、animation/transition中は一時的に連続駆動する。
/// </summary>
public abstract class UiScene : IScene, ISceneTransitionParticipant
{
    private readonly IFrameScheduler _frames;
    private readonly List<UiHost> _hosts = new();
    private int _frameRequested = 1;
    private int _continuousRequests;
    private bool _active;
    private IDisposable? _manualContinuousLease;
    private IDisposable? _hostAnimationLease;

    protected UiScene(IFrameScheduler frames) => _frames = frames;

    protected virtual Task OnLoadAsync() => Task.CompletedTask;
    protected virtual Task OnActivateAsync() => Task.CompletedTask;
    protected virtual Task OnSuspendAsync() => Task.CompletedTask;
    protected virtual Task OnResumeAsync() => Task.CompletedTask;
    protected virtual Task OnDeactivateAsync() => Task.CompletedTask;
    protected virtual Task OnUnloadAsync() => Task.CompletedTask;

    protected virtual void OnEarlyUpdate(EarlyUpdateContext context) { }
    protected virtual void OnUpdate(UpdateContext context) { }
    protected virtual void OnLateUpdate(LateUpdateContext context) { }
    protected virtual void OnPreRender(PreRenderContext context) { }
    protected virtual void OnRender(RenderContext context) { }
    protected virtual void OnPostRender(PostRenderContext context) { }
    /// <summary>Scene遷移のprogressをUI固有のopacity/transform等へ反映する。</summary>
    protected virtual void OnTransition(SceneTransitionContext context, SceneTransitionRole role) { }
    protected virtual void OnAttached(SceneNode node) { }
    protected virtual void OnDetached(SceneNode node) { }

    /// <summary>このSceneで駆動するUiHostを登録する。所有権は呼び出し側に残る。</summary>
    protected void AddHost(UiHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        if (_hosts.Contains(host)) return;
        _hosts.Add(host);
        host.FrameRequested += Invalidate;
        Invalidate();
    }

    protected void RemoveHost(UiHost host)
    {
        if (!_hosts.Remove(host)) return;
        host.FrameRequested -= Invalidate;
        UpdateSchedulerDemand();
    }

    protected IReadOnlyList<UiHost> Hosts => _hosts;

    /// <summary>状態変更を次のUIフレームへまとめる。任意スレッドから呼べる。</summary>
    public void Invalidate()
    {
        Interlocked.Exchange(ref _frameRequested, 1);
        if (_active) _frames.RequestFrame();
    }

    /// <summary>Scene固有のanimation/transitionの間だけ連続フレームを要求する。</summary>
    protected IDisposable BeginContinuousFrames()
    {
        Interlocked.Increment(ref _continuousRequests);
        Interlocked.Exchange(ref _frameRequested, 1);
        UpdateSchedulerDemand();
        return new ContinuousRequest(this);
    }

    SceneExecutionMode IScene.ExecutionMode => SceneExecutionMode.OnDemand;
    SceneRenderMode IScene.RenderMode => SceneRenderMode.WhenDirty;
    void IScene.OnAttached(SceneNode node) => OnAttached(node);
    void IScene.OnDetached(SceneNode node) => OnDetached(node);

    async Task IScene.OnLoadAsync() => await OnLoadAsync();

    async Task IScene.OnActivateAsync()
    {
        _active = true;
        await OnActivateAsync();
        Invalidate();
        UpdateSchedulerDemand();
    }

    async Task IScene.OnSuspendAsync()
    {
        _active = false;
        ReleaseSchedulerLeases();
        await OnSuspendAsync();
    }

    async Task IScene.OnResumeAsync()
    {
        _active = true;
        await OnResumeAsync();
        Invalidate();
        UpdateSchedulerDemand();
    }

    async Task IScene.OnDeactivateAsync()
    {
        _active = false;
        ReleaseSchedulerLeases();
        await OnDeactivateAsync();
    }

    async Task IScene.OnUnloadAsync()
    {
        foreach (UiHost host in _hosts) host.FrameRequested -= Invalidate;
        _hosts.Clear();
        await OnUnloadAsync();
    }

    bool IScene.TryBeginFrame()
        => Volatile.Read(ref _continuousRequests) > 0
           || _hosts.Any(h => h.HasActiveAnimations)
           || Interlocked.Exchange(ref _frameRequested, 0) != 0;

    void IScene.EarlyUpdate(EarlyUpdateContext context) => OnEarlyUpdate(context);

    void IScene.Update(UpdateContext context)
    {
        foreach (UiHost host in _hosts)
        {
            host.FlushRealize();
            host.AdvanceAnimations(context.Time.DeltaSeconds);
        }
        OnUpdate(context);
        UpdateSchedulerDemand();
    }

    void IScene.LateUpdate(LateUpdateContext context) => OnLateUpdate(context);
    void IScene.PreRender(PreRenderContext context) => OnPreRender(context);
    void IScene.Render(RenderContext context) => OnRender(context);
    void IScene.PostRender(PostRenderContext context) => OnPostRender(context);

    void ISceneTransitionParticipant.OnSceneTransition(
        SceneTransitionContext context, SceneTransitionRole role)
    {
        OnTransition(context, role);
        Invalidate();
    }

    private void EndContinuousFrames()
    {
        int remaining = Interlocked.Decrement(ref _continuousRequests);
        if (remaining < 0) Interlocked.Exchange(ref _continuousRequests, 0);
        UpdateSchedulerDemand();
    }

    private void UpdateSchedulerDemand()
    {
        if (!_active)
        {
            ReleaseSchedulerLeases();
            return;
        }

        if (Volatile.Read(ref _continuousRequests) > 0)
            _manualContinuousLease ??= _frames.AcquireContinuousFrames();
        else
        {
            _manualContinuousLease?.Dispose();
            _manualContinuousLease = null;
        }

        if (_hosts.Any(h => h.HasActiveAnimations))
            _hostAnimationLease ??= _frames.AcquireContinuousFrames();
        else
        {
            _hostAnimationLease?.Dispose();
            _hostAnimationLease = null;
        }
    }

    private void ReleaseSchedulerLeases()
    {
        _manualContinuousLease?.Dispose();
        _manualContinuousLease = null;
        _hostAnimationLease?.Dispose();
        _hostAnimationLease = null;
    }

    private sealed class ContinuousRequest(UiScene owner) : IDisposable
    {
        private UiScene? _owner = owner;
        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.EndContinuousFrames();
    }
}
