namespace Luxel.Animation;

/// <summary>
/// AnimationPlayer が管理する 1 つの再生中アニメ。stateful (開始絶対時刻 + 速度 + ループ + 完了状態)。
/// 内部で dt を累積せず、外から渡される絶対時刻 (<see cref="IClock"/>) との差分で track time を求めるため、
/// 浮動小数の累積誤差が発生しない。
///
/// 型 T は実体 <c>TrackEntry&lt;T&gt;</c> が持つ。Player は型消去された <see cref="TrackEntryBase"/> で扱う。
/// </summary>
public abstract class TrackEntryBase
{
    /// <summary>Play 時に記録される開始絶対時刻 (秒)。</summary>
    public float StartTime { get; internal set; }

    /// <summary>速度倍率。1=通常、負値は未対応 (代わりに Reverse の概念は AN-M2 で追加)。</summary>
    public float TimeScale { get; init; } = 1f;

    /// <summary>ループ再生。</summary>
    public bool Loop { get; init; }

    /// <summary>このトラックでの経過時間 (秒、TimeScale 適用後)。最新 Update 時の値。</summary>
    public float TrackTime { get; protected set; }

    /// <summary>完了フラグ。Loop=false で Duration を超えると true、Player から除外される。</summary>
    public bool Done { get; protected set; }

    /// <summary>完了時に 1 度呼ばれる。</summary>
    public Action? OnComplete { get; set; }

    /// <summary>このトラックの全体時間 (秒)。</summary>
    public abstract float Duration { get; }

    /// <summary>一時停止中フラグ。<see cref="Pause"/> と <see cref="Resume"/> で切替。</summary>
    public bool Paused { get; private set; }

    private float _pausedAt;
    /// <summary>累積一時停止時間 (秒)。track time の計算でこの分を絶対時刻から差し引く。</summary>
    public float AccumulatedPausedTime { get; private set; }

    /// <summary>絶対時刻に基づき値を更新。dt 累積ではなく `(absoluteTime - StartTime - AccumulatedPausedTime) × TimeScale`。</summary>
    public abstract void UpdateAt(float absoluteTimeSec);

    /// <summary>強制完了 (時刻を Duration に進めて OnComplete 起動)。</summary>
    public abstract void Finish();

    /// <summary>一時停止。<paramref name="absoluteTimeSec"/> 時点で停止し、UpdateAt は no-op になる。</summary>
    public void Pause(float absoluteTimeSec)
    {
        if (Paused || Done) return;
        Paused = true;
        _pausedAt = absoluteTimeSec;
    }

    /// <summary>停止解除。停止中に経過した時間 (現在時刻 - 停止時刻) を AccumulatedPausedTime に加算。</summary>
    public void Resume(float absoluteTimeSec)
    {
        if (!Paused) return;
        Paused = false;
        AccumulatedPausedTime += absoluteTimeSec - _pausedAt;
    }

    public void Pause(IClock clock) => Pause(clock.TimeSec);
    public void Resume(IClock clock) => Resume(clock.TimeSec);

    /// <summary>
    /// ローカル時刻 (TrackTime, 0..Duration) を指定して再生位置を移動。
    /// StartTime を再計算: StartTime = absoluteTimeSec - (localTimeSec / TimeScale) - AccumulatedPausedTime。
    /// </summary>
    public void Seek(float localTimeSec, float absoluteTimeSec)
    {
        if (TimeScaleValue <= 0f) return;
        StartTime = absoluteTimeSec - localTimeSec / TimeScaleValue - AccumulatedPausedTime;
        Done = false;  // seek で完了状態をリセット
    }

    public void Seek(float localTimeSec, IClock clock) => Seek(localTimeSec, clock.TimeSec);

    /// <summary>派生クラスから TimeScale を取得するためのプロパティ (TrackEntry&lt;T&gt; で上書きせずそのまま使う)。</summary>
    protected virtual float TimeScaleValue => 1f;
}

/// <summary>型付き TrackEntry。Animatable と setter (Action&lt;T&gt;) を保持。</summary>
public sealed class TrackEntry<T> : TrackEntryBase
{
    public IAnimatable<T> Animatable { get; }
    public Action<T> Setter { get; }

    public override float Duration => Animatable.Duration;
    protected override float TimeScaleValue => TimeScale;

    internal TrackEntry(IAnimatable<T> animatable, Action<T> setter)
    {
        Animatable = animatable;
        Setter = setter;
    }

    public override void UpdateAt(float absoluteTimeSec)
    {
        if (Done || Paused) return;
        float t = (absoluteTimeSec - StartTime - AccumulatedPausedTime) * TimeScale;

        if (t < 0f) t = 0f;

        // Duration <= 0 はマーカー (Sequence/Parallel の OnComplete fan-in 用)。
        // StartTime に到達したら即完了させる。
        if (Animatable.Duration <= 0f)
        {
            if (absoluteTimeSec >= StartTime + AccumulatedPausedTime)
            {
                TrackTime = 0f;
                Setter(Animatable.Evaluate(0f));
                Done = true;
                OnComplete?.Invoke();
            }
            return;
        }

        if (t >= Animatable.Duration)
        {
            if (Loop)
            {
                t %= Animatable.Duration;
            }
            else
            {
                TrackTime = Animatable.Duration;
                Setter(Animatable.Evaluate(Animatable.Duration));
                Done = true;
                OnComplete?.Invoke();
                return;
            }
        }
        TrackTime = t;
        Setter(Animatable.Evaluate(t));
    }

    public override void Finish()
    {
        if (Done) return;
        TrackTime = Animatable.Duration;
        Setter(Animatable.Evaluate(Animatable.Duration));
        Done = true;
        OnComplete?.Invoke();
    }
}
