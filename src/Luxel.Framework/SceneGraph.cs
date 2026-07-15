using Luxel.Input;

namespace Luxel.Framework;

/// <summary>Scene node がフレーム処理へ参加する頻度。</summary>
public enum SceneExecutionMode
{
    /// <summary>親 node の設定を継承する。root では OnDemand と同じ。</summary>
    Inherit,
    /// <summary>Scene が要求したフレームだけ処理する。</summary>
    OnDemand,
    /// <summary>Active な間、連続フレームを要求して毎フレーム処理する。</summary>
    Continuous,
}

/// <summary>Scene node の描画方針。Update頻度とは独立して指定する。</summary>
public enum SceneRenderMode
{
    /// <summary>親 node の設定を継承する。root では WhenDirty と同じ。</summary>
    Inherit,
    /// <summary>Scene がフレームへ参加したときだけ描画する。</summary>
    WhenDirty,
    /// <summary>Scene がフレームへ参加するたび描画する。</summary>
    EveryFrame,
    /// <summary>Updateは許可するが描画結果を更新しない。</summary>
    Frozen,
}

/// <summary>Scene node のライフサイクル状態。</summary>
public enum SceneLifecycleState
{
    Created,
    Loading,
    Loaded,
    Active,
    Suspended,
    Unloading,
    Unloaded,
}

/// <summary>
/// SceneGraph の1 node。親子関係、実行・描画方針、現在のライフサイクル状態を保持する。
/// Graphの変更は <see cref="SceneManager"/> のAPIを通してフレーム境界で適用する。
/// </summary>
public sealed class SceneNode
{
    private readonly List<SceneNode> _children = new();
    private readonly IReadOnlyList<SceneNode> _childrenView;

    internal SceneNode(IScene scene, SceneExecutionMode executionMode, SceneRenderMode renderMode)
    {
        Scene = scene;
        ExecutionMode = executionMode;
        RenderMode = renderMode;
        _childrenView = _children.AsReadOnly();
    }

    public IScene Scene { get; }
    public SceneNode? Parent { get; internal set; }
    public IReadOnlyList<SceneNode> Children => _childrenView;
    public SceneExecutionMode ExecutionMode { get; internal set; }
    public SceneRenderMode RenderMode { get; internal set; }
    public SceneLifecycleState State { get; internal set; } = SceneLifecycleState.Created;
    /// <summary>Scene遷移のoutgoing/incoming subtreeとして同時実行中か。</summary>
    public bool IsTransitioning { get; internal set; }

    public SceneExecutionMode EffectiveExecutionMode
    {
        get
        {
            for (SceneNode? n = this; n is not null; n = n.Parent)
                if (n.ExecutionMode != SceneExecutionMode.Inherit) return n.ExecutionMode;
            return SceneExecutionMode.OnDemand;
        }
    }

    public SceneRenderMode EffectiveRenderMode
    {
        get
        {
            for (SceneNode? n = this; n is not null; n = n.Parent)
                if (n.RenderMode != SceneRenderMode.Inherit) return n.RenderMode;
            return SceneRenderMode.WhenDirty;
        }
    }

    internal List<SceneNode> MutableChildren => _children;
    internal IDisposable? ContinuousLease { get; set; }
    internal bool IsLocallySuspended { get; set; }
    internal FixedTimestep? FixedTimestep { get; set; }
    internal long Frame { get; set; }
    internal double TotalSeconds { get; set; }
    internal double? LastRunAt { get; set; }
    internal List<InputContext> ManagedInputContexts { get; } = new();
}
