namespace Luxel.Framework;

/// <summary>
/// UIのpage、wizard、設定画面など、単一の子Sceneを履歴付きで切り替えるcontainer Scene。
/// Navigate/GoBackは子nodeだけを遷移するため、親や周囲のDock slotはActiveのまま維持される。
/// </summary>
public abstract class NavigationScene : IScene
{
    private readonly SceneManager _scenes;
    private readonly Stack<IScene> _history = new();
    private readonly SemaphoreSlim _navigationGate = new(1, 1);
    private SceneNode? _node;
    private IScene? _current;

    protected NavigationScene(SceneManager scenes) => _scenes = scenes;

    /// <summary>Loadごとに最初に表示するSceneを返す。</summary>
    protected abstract IScene CreateInitialScene();

    protected virtual Task OnLoadAsync() => Task.CompletedTask;
    protected virtual Task OnActivateAsync() => Task.CompletedTask;
    protected virtual Task OnSuspendAsync() => Task.CompletedTask;
    protected virtual Task OnResumeAsync() => Task.CompletedTask;
    protected virtual Task OnDeactivateAsync() => Task.CompletedTask;
    protected virtual Task OnUnloadAsync() => Task.CompletedTask;

    protected SceneNode Node => _node
        ?? throw new InvalidOperationException("NavigationSceneがSceneGraphへ接続されていません。");

    public IScene? Current => _current;
    public bool CanGoBack => _history.Count > 0;
    public int HistoryCount => _history.Count;
    public SceneNode? CurrentNode => _current is null
        ? null
        : Node.Children.FirstOrDefault(child => ReferenceEquals(child.Scene, _current));

    /// <summary>現在のSceneを履歴へ積み、<paramref name="next"/>へ遷移する。</summary>
    public async Task NavigateAsync(IScene next, SceneTransitionSpec transition)
    {
        ArgumentNullException.ThrowIfNull(next);
        ArgumentNullException.ThrowIfNull(transition);
        await _navigationGate.WaitAsync();
        try
        {
            IScene outgoing = _current
                ?? throw new InvalidOperationException("NavigationSceneがLoadされていません。");
            if (ReferenceEquals(outgoing, next)) return;
            SceneNode outgoingNode = CurrentNode
                ?? throw new InvalidOperationException("現在のNavigation childがまだSceneGraphへ反映されていません。");
            await _scenes.TransitionAsync(outgoingNode, next, transition);
            _history.Push(outgoing);
            _current = next;
        }
        finally
        {
            _navigationGate.Release();
        }
    }

    /// <summary>履歴を増やさずに現在のSceneを置換する。</summary>
    public async Task ReplaceAsync(IScene next, SceneTransitionSpec transition)
    {
        ArgumentNullException.ThrowIfNull(next);
        ArgumentNullException.ThrowIfNull(transition);
        await _navigationGate.WaitAsync();
        try
        {
            IScene outgoing = _current
                ?? throw new InvalidOperationException("NavigationSceneがLoadされていません。");
            if (ReferenceEquals(outgoing, next)) return;
            SceneNode outgoingNode = CurrentNode
                ?? throw new InvalidOperationException("現在のNavigation childがまだSceneGraphへ反映されていません。");
            await _scenes.TransitionAsync(outgoingNode, next, transition);
            _current = next;
        }
        finally
        {
            _navigationGate.Release();
        }
    }

    /// <summary>1つ前のSceneへ遷移する。履歴が空ならfalse。</summary>
    public async Task<bool> GoBackAsync(SceneTransitionSpec transition)
    {
        ArgumentNullException.ThrowIfNull(transition);
        await _navigationGate.WaitAsync();
        try
        {
            if (_history.Count == 0) return false;
            SceneNode outgoingNode = CurrentNode
                ?? throw new InvalidOperationException("現在のNavigation childがまだSceneGraphへ反映されていません。");
            IScene previous = _history.Peek();
            await _scenes.TransitionAsync(outgoingNode, previous, transition);
            _history.Pop();
            _current = previous;
            return true;
        }
        finally
        {
            _navigationGate.Release();
        }
    }

    SceneExecutionMode IScene.ExecutionMode => SceneExecutionMode.OnDemand;
    SceneRenderMode IScene.RenderMode => SceneRenderMode.Frozen;
    bool IScene.TryBeginFrame() => false;

    void IScene.OnAttached(SceneNode node)
    {
        if (_node is not null && !ReferenceEquals(_node, node))
            throw new InvalidOperationException("同じNavigationScene instanceを複数のnodeへ接続できません。");
        _node = node;
    }

    async Task IScene.OnLoadAsync()
    {
        _history.Clear();
        _current = CreateInitialScene()
            ?? throw new InvalidOperationException("NavigationScene.CreateInitialScene()はnullを返せません。");
        await OnLoadAsync();
    }

    async Task IScene.OnActivateAsync()
    {
        await OnActivateAsync();
        _ = _scenes.AddChildAsync(Node, _current!);
    }

    async Task IScene.OnSuspendAsync() => await OnSuspendAsync();
    async Task IScene.OnResumeAsync() => await OnResumeAsync();
    async Task IScene.OnDeactivateAsync() => await OnDeactivateAsync();

    async Task IScene.OnUnloadAsync()
    {
        await OnUnloadAsync();
        _history.Clear();
        _current = null;
    }

    void IScene.OnDetached(SceneNode node)
    {
        if (ReferenceEquals(_node, node)) _node = null;
    }
}
