namespace Luxel.Animation;

/// <summary>
/// 単一 Tween を表す Command。<see cref="Animate.Tween{T}"/> から生成される。
/// fluent メソッド (WithCurve / WithDelay / WithLoop / OnComplete) で構成を組み立てる。
/// </summary>
public sealed class TweenCommand<T> : IAnimationCommand
{
    private readonly Action<T> _setter;
    private ITween<T> _tween;
    private ICurve _curve = LinearCurve.Instance;
    private float _duration;
    private float _delay;
    private float _timeScale = 1f;
    private bool _loop;
    private Action? _onComplete;

    /// <summary>delay + duration。</summary>
    public float TotalDuration => _delay + _duration;

    /// <summary>Tween 本体の長さ (delay 除く)。</summary>
    public float Duration => _duration;

    internal TweenCommand(Action<T> setter, ITween<T> tween, float duration)
    {
        _setter = setter;
        _tween = tween;
        _duration = duration;
    }

    public TweenCommand<T> WithCurve(ICurve curve)
    {
        _curve = curve ?? LinearCurve.Instance;
        return this;
    }

    /// <summary>再生開始前に待つ秒数。</summary>
    public TweenCommand<T> WithDelay(float delaySec)
    {
        if (delaySec < 0f) throw new ArgumentException("delay >= 0", nameof(delaySec));
        _delay = delaySec;
        return this;
    }

    public TweenCommand<T> WithTimeScale(float scale)
    {
        if (scale <= 0f) throw new ArgumentException("scale > 0", nameof(scale));
        _timeScale = scale;
        return this;
    }

    public TweenCommand<T> WithLoop(bool loop = true)
    {
        _loop = loop;
        return this;
    }

    public TweenCommand<T> OnComplete(Action action)
    {
        _onComplete = action;
        return this;
    }

    /// <summary>Player に投入して再生開始。返り値は Schedule した TrackEntry。</summary>
    public TrackEntry<T> Play(AnimationPlayer player, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(clock);
        return ScheduleInternal(player, clock, clock.TimeSec);
    }

    public void Schedule(AnimationPlayer player, IClock clock, float startTimeAbs)
    {
        ScheduleInternal(player, clock, startTimeAbs);
    }

    private TrackEntry<T> ScheduleInternal(AnimationPlayer player, IClock clock, float startTimeAbs)
    {
        var anim = new Animatable<T> { Curve = _curve, Tween = _tween, Duration = _duration };
        var entry = player.Play(anim, _setter, clock, timeScale: _timeScale, loop: _loop);
        entry.StartTime = startTimeAbs + _delay;
        entry.OnComplete = _onComplete;
        return entry;
    }
}
