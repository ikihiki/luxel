namespace Luxel.Framework;

/// <summary>
/// pause menuやmodal dialog向けの要求駆動UI Scene。
/// 入力は既定でModalとなり、親系統に<see cref="IPausableScene"/>があればActive期間だけ停止leaseを保持する。
/// </summary>
public abstract class OverlayScene : UiScene
{
    private IPausableScene? _underlay;
    private IDisposable? _pauseLease;

    protected OverlayScene(IFrameScheduler frames) : base(frames) { }

    /// <summary>falseにすると描画・入力のoverlayだけを行い、下層simulationは継続する。</summary>
    protected virtual bool PauseUnderlyingScene => true;
    protected override SceneInputMode InputMode => SceneInputMode.Modal;

    protected virtual Task OnOverlayActivateAsync() => Task.CompletedTask;
    protected virtual Task OnOverlaySuspendAsync() => Task.CompletedTask;
    protected virtual Task OnOverlayResumeAsync() => Task.CompletedTask;
    protected virtual Task OnOverlayDeactivateAsync() => Task.CompletedTask;
    protected virtual void OnOverlayAttached(SceneNode node) { }
    protected virtual void OnOverlayDetached(SceneNode node) { }

    protected sealed override void OnAttached(SceneNode node)
    {
        for (SceneNode? current = node.Parent; current is not null; current = current.Parent)
        {
            if (current.Scene is not IPausableScene pausable) continue;
            _underlay = pausable;
            break;
        }
        OnOverlayAttached(node);
    }

    protected sealed override void OnDetached(SceneNode node)
    {
        try { OnOverlayDetached(node); }
        finally
        {
            ReleasePause();
            _underlay = null;
        }
    }

    protected sealed override async Task OnActivateAsync()
    {
        AcquirePause();
        try { await OnOverlayActivateAsync(); }
        catch
        {
            ReleasePause();
            throw;
        }
    }

    protected sealed override async Task OnSuspendAsync()
    {
        try { await OnOverlaySuspendAsync(); }
        finally { ReleasePause(); }
    }

    protected sealed override async Task OnResumeAsync()
    {
        AcquirePause();
        try { await OnOverlayResumeAsync(); }
        catch
        {
            ReleasePause();
            throw;
        }
    }

    protected sealed override async Task OnDeactivateAsync()
    {
        try { await OnOverlayDeactivateAsync(); }
        finally { ReleasePause(); }
    }

    private void AcquirePause()
    {
        if (PauseUnderlyingScene) _pauseLease ??= _underlay?.AcquirePause();
    }

    private void ReleasePause()
    {
        _pauseLease?.Dispose();
        _pauseLease = null;
    }
}
