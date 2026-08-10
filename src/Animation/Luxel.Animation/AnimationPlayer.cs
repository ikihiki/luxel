namespace Luxel.Animation;

/// <summary>
/// 複数 TrackEntry を frame-driven で進める scheduler。<see cref="Update(IClock)"/> または
/// <see cref="Update(float)"/> を毎フレーム呼ぶ。内部で dt を累積せず、外から渡される絶対時刻と
/// 各 TrackEntry の StartTime の差分で track time を求めるため浮動小数累積誤差が発生しない。
/// </summary>
public sealed class AnimationPlayer
{
    private readonly List<TrackEntryBase> _tracks = new();
    private float _lastTime;

    /// <summary>アクティブな TrackEntry 数 (完了済みを除く)。</summary>
    public int ActiveCount => _tracks.Count;

    /// <summary>最後に観測した時刻 (Play 時の StartTime はこれを基準にする)。</summary>
    public float LastTime => _lastTime;

    /// <summary>
    /// Animatable&lt;T&gt; と setter を組み合わせて再生開始する。
    /// StartTime は <paramref name="clock"/> の現在時刻 (省略時は <see cref="LastTime"/>)。
    /// </summary>
    public TrackEntry<T> Play<T>(IAnimatable<T> animatable, Action<T> setter, IClock? clock = null,
                                   float timeScale = 1f, bool loop = false)
    {
        ArgumentNullException.ThrowIfNull(animatable);
        ArgumentNullException.ThrowIfNull(setter);
        float startTime = clock?.TimeSec ?? _lastTime;
        var entry = new TrackEntry<T>(animatable, setter)
        {
            StartTime = startTime,
            TimeScale = timeScale,
            Loop = loop,
        };
        _tracks.Add(entry);
        // 初期値を即座に反映 (t=0)
        setter(animatable.Evaluate(0f));
        return entry;
    }

    /// <summary>全 TrackEntry を絶対時刻で更新する。完了した entry は自動で除外。</summary>
    public void Update(float absoluteTimeSec)
    {
        _lastTime = absoluteTimeSec;
        if (_tracks.Count == 0) return;
        for (int i = 0; i < _tracks.Count; i++)
        {
            _tracks[i].UpdateAt(absoluteTimeSec);
        }
        _tracks.RemoveAll(t => t.Done);
    }

    /// <summary>IClock を介して更新するヘルパ。</summary>
    public void Update(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        Update(clock.TimeSec);
    }

    /// <summary>指定 entry を停止して除外する (OnComplete は発火しない)。</summary>
    public void Stop(TrackEntryBase entry) => _tracks.Remove(entry);

    /// <summary>全 track を即座にクリア。</summary>
    public void Clear() => _tracks.Clear();
}
