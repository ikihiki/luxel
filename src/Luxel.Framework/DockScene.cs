namespace Luxel.Framework;

/// <summary>DockSceneが同時実行する名前付きの子Scene定義。</summary>
public sealed class DockSceneSlot
{
    public DockSceneSlot(
        string name,
        IScene scene,
        SceneExecutionMode? executionMode = null,
        SceneRenderMode? renderMode = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(scene);
        Name = name;
        Scene = scene;
        ExecutionMode = executionMode;
        RenderMode = renderMode;
    }

    public string Name { get; }
    public IScene Scene { get; }
    public SceneExecutionMode? ExecutionMode { get; }
    public SceneRenderMode? RenderMode { get; }
}

/// <summary>
/// Story一覧と内容、editorの各paneなど、名前付きの複数Sceneを子として同時実行するcontainer Scene。
/// DockScene自身はphaseや描画へ参加せず、各slotのSceneが独立した実行・描画方針で参加する。
/// </summary>
public abstract class DockScene : IScene
{
    private readonly SceneManager _scenes;
    private readonly Dictionary<string, DockSceneSlot> _slots = new(StringComparer.Ordinal);
    private SceneNode? _node;

    protected DockScene(SceneManager scenes) => _scenes = scenes;

    /// <summary>LoadごとにこのDockを構成するslotを返す。</summary>
    protected abstract IEnumerable<DockSceneSlot> CreateSlots();

    protected virtual Task OnLoadAsync() => Task.CompletedTask;
    protected virtual Task OnActivateAsync() => Task.CompletedTask;
    protected virtual Task OnSuspendAsync() => Task.CompletedTask;
    protected virtual Task OnResumeAsync() => Task.CompletedTask;
    protected virtual Task OnDeactivateAsync() => Task.CompletedTask;
    protected virtual Task OnUnloadAsync() => Task.CompletedTask;

    protected SceneNode Node => _node
        ?? throw new InvalidOperationException("DockSceneがSceneGraphへ接続されていません。");

    /// <summary>現在構成されているslot。子nodeへの反映はフレーム境界で行われる。</summary>
    public IReadOnlyCollection<DockSceneSlot> Slots => _slots.Values;

    /// <summary>slotに対応する現在の子nodeを返す。まだフレーム境界で追加されていない場合はnull。</summary>
    public SceneNode? GetSlotNode(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (_node is null || !_slots.TryGetValue(name, out DockSceneSlot? slot)) return null;
        return _node.Children.FirstOrDefault(child => ReferenceEquals(child.Scene, slot.Scene));
    }

    /// <summary>
    /// 指定slotのSceneだけを遷移する。他のslotはActiveのまま実行される。
    /// 返るTaskは遷移完了後に完了し、中断された場合はCanceledになる。
    /// </summary>
    public async Task TransitionSlotAsync(
        string name,
        IScene next,
        SceneTransitionSpec transition)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(next);
        ArgumentNullException.ThrowIfNull(transition);
        if (!_slots.TryGetValue(name, out DockSceneSlot? current))
            throw new KeyNotFoundException($"DockScene slot '{name}' は存在しません。");
        SceneNode outgoing = GetSlotNode(name)
            ?? throw new InvalidOperationException($"DockScene slot '{name}' はまだSceneGraphへ反映されていません。");

        await _scenes.TransitionAsync(outgoing, next, transition);
        _slots[name] = new DockSceneSlot(
            current.Name,
            next,
            current.ExecutionMode,
            current.RenderMode);
    }

    /// <summary>指定slotを次のフレーム境界で即時置換する。</summary>
    public Task ReplaceSlotAsync(string name, IScene next)
        => TransitionSlotAsync(name, next, new SceneTransitionSpec(0f));

    SceneExecutionMode IScene.ExecutionMode => SceneExecutionMode.OnDemand;
    SceneRenderMode IScene.RenderMode => SceneRenderMode.Frozen;
    bool IScene.TryBeginFrame() => false;

    void IScene.OnAttached(SceneNode node)
    {
        if (_node is not null && !ReferenceEquals(_node, node))
            throw new InvalidOperationException("同じDockScene instanceを複数のnodeへ接続できません。");
        _node = node;
    }

    async Task IScene.OnLoadAsync()
    {
        _slots.Clear();
        IEnumerable<DockSceneSlot> slots = CreateSlots()
            ?? throw new InvalidOperationException("DockScene.CreateSlots()はnullを返せません。");
        foreach (DockSceneSlot slot in slots)
        {
            if (slot is null)
                throw new InvalidOperationException("DockScene slotにnullは指定できません。");
            if (!_slots.TryAdd(slot.Name, slot))
                throw new InvalidOperationException($"DockScene slot名 '{slot.Name}' が重複しています。");
        }
        await OnLoadAsync();
    }

    async Task IScene.OnActivateAsync()
    {
        await OnActivateAsync();
        SceneNode node = _node
            ?? throw new InvalidOperationException("DockSceneがSceneGraphへ接続されていません。");
        foreach (DockSceneSlot slot in _slots.Values)
            await _scenes.AddChildAsync(node, slot.Scene, slot.ExecutionMode, slot.RenderMode);
    }

    async Task IScene.OnSuspendAsync() => await OnSuspendAsync();
    async Task IScene.OnResumeAsync() => await OnResumeAsync();
    async Task IScene.OnDeactivateAsync() => await OnDeactivateAsync();

    async Task IScene.OnUnloadAsync()
    {
        await OnUnloadAsync();
        _slots.Clear();
    }

    void IScene.OnDetached(SceneNode node)
    {
        if (ReferenceEquals(_node, node)) _node = null;
    }
}
