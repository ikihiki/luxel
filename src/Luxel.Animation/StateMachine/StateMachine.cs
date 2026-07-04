namespace Luxel.Animation;

/// <summary>
/// 状態遷移付きアニメーション再生器。Rive State Machine / Unity Mecanim と同じ「states + transitions」モデル。
/// Trigger 名でマッチする transition を発火、Crossfade 中は BlendNode で from→to を線形に混合。
///
/// 使い方:
/// <code>
/// var sm = new StateMachine(target);
/// var idle = new State("idle", new ClipNode(idleClip));
/// var jump = new State("jump", new ClipNode(jumpClip));
/// idle.AddTransition("press", jump, crossfadeSec: 0.15f);
/// jump.AddTransition("done", idle, crossfadeSec: 0.15f);
/// sm.AddState(idle).AddState(jump).SetInitial(idle);
///
/// sm.Start(clock);
/// for (each frame) sm.Tick(clock);
/// sm.Trigger("press");  // 状態遷移開始
/// </code>
/// </summary>
public sealed class StateMachine
{
    private readonly Dictionary<string, State> _states = new();
    private readonly IAnimationTarget _target;

    // active 状態と (もし遷移中なら) クロスフェード情報
    private State? _current;
    private State? _from;       // 遷移元 (transition 中のみ非 null)
    private float _currentStartTime;
    private float _fromStartTime;
    private float _transitionStartTime;
    private float _transitionDuration;

    public StateMachine(IAnimationTarget target)
    {
        _target = target ?? throw new ArgumentNullException(nameof(target));
    }

    /// <summary>現在 active な state (transition 中は遷移先)。</summary>
    public State? Current => _current;

    /// <summary>遷移中か。</summary>
    public bool IsTransitioning => _from != null;

    public StateMachine AddState(State s)
    {
        _states[s.Name] = s;
        return this;
    }

    public StateMachine SetInitial(State s)
    {
        _current = s;
        return this;
    }

    /// <summary>再生開始。<paramref name="clock"/> の現在時刻を Current state の StartTime とする。</summary>
    public void Start(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        if (_current == null) throw new InvalidOperationException("Initial state が未設定");
        _currentStartTime = clock.TimeSec;
        _from = null;
    }

    /// <summary>Trigger 名で transition を発火。マッチする transition があれば遷移開始。</summary>
    public void Trigger(string name, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        if (_current == null) return;
        foreach (var t in _current.Transitions)
        {
            if (t.Trigger == name)
            {
                _from = _current;
                _fromStartTime = _currentStartTime;
                _current = t.To;
                _currentStartTime = clock.TimeSec;
                _transitionStartTime = clock.TimeSec;
                _transitionDuration = t.CrossfadeSec;
                return;
            }
        }
    }

    /// <summary>毎フレーム評価。遷移中は from/to を BlendNode で動的に合成。</summary>
    public void Tick(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        if (_current == null) return;
        float now = clock.TimeSec;
        var eval = new GraphEvaluator();

        if (_from != null && _transitionDuration > 0f)
        {
            float p = (now - _transitionStartTime) / _transitionDuration;
            if (p >= 1f)
            {
                // 遷移完了
                _from = null;
                _current.Graph.Evaluate(now - _currentStartTime, eval);
            }
            else
            {
                // 動的 BlendNode を作って評価
                var subFrom = new GraphEvaluator();
                var subTo = new GraphEvaluator();
                _from.Graph.Evaluate(now - _fromStartTime, subFrom);
                _current.Graph.Evaluate(now - _currentStartTime, subTo);
                var paths = new HashSet<string>();
                foreach (var x in subFrom.Paths) paths.Add(x);
                foreach (var x in subTo.Paths) paths.Add(x);
                foreach (var path in paths)
                {
                    bool ha = subFrom.TryGet(path, out var va, out var sa);
                    bool hb = subTo.TryGet(path, out var vb, out var sb);
                    if (ha && hb) eval.Set(path, GraphEvaluator.Lerp(va, vb, p, sa), sa);
                    else if (ha) eval.Set(path, va, sa);
                    else if (hb) eval.Set(path, vb, sb);
                }
            }
        }
        else
        {
            _current.Graph.Evaluate(now - _currentStartTime, eval);
        }
        eval.FlushTo(_target);
    }
}
