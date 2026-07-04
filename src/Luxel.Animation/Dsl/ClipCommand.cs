namespace Luxel.Animation;

/// <summary>
/// <see cref="AnimationClip"/> を再生する Command。複数 Track を各々 TrackEntry として Player に Schedule する。
/// AN-M3 の主要エントリポイント: `Animate.Play(clip, target)` の代わりに Clip を直接 Schedule。
/// </summary>
public sealed class ClipCommand : IAnimationCommand
{
    private readonly AnimationClip _clip;
    private readonly IAnimationTarget _target;
    private float _timeScale = 1f;
    private float _delay;
    private bool _loop;
    private Action? _onComplete;

    public float TotalDuration => _delay + _clip.Duration / _timeScale;

    public ClipCommand(AnimationClip clip, IAnimationTarget target)
    {
        _clip = clip ?? throw new ArgumentNullException(nameof(clip));
        _target = target ?? throw new ArgumentNullException(nameof(target));
    }

    public ClipCommand WithTimeScale(float scale)
    {
        if (scale <= 0f) throw new ArgumentException("scale > 0", nameof(scale));
        _timeScale = scale;
        return this;
    }

    public ClipCommand WithDelay(float delaySec) { _delay = delaySec; return this; }
    public ClipCommand WithLoop(bool loop = true) { _loop = loop; return this; }
    public ClipCommand OnComplete(Action action) { _onComplete = action; return this; }

    public void Play(AnimationPlayer player, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(clock);
        Schedule(player, clock, clock.TimeSec);
    }

    public void Schedule(AnimationPlayer player, IClock clock, float startTimeAbs)
    {
        // 各 Track を 1 つの TrackEntry にする。
        // Track の値型は実行時にしか決まらないので、Animatable<object> 風に object 経由で書く。
        foreach (var track in _clip.Tracks)
        {
            ScheduleTrack(player, clock, startTimeAbs + _delay, track);
        }
        if (_onComplete != null)
        {
            float endTime = startTimeAbs + _delay + _clip.Duration / _timeScale;
            SequenceCommand.ScheduleCompletionMarker(player, clock, endTime, _onComplete);
        }
    }

    private void ScheduleTrack(AnimationPlayer player, IClock clock, float startTimeAbs, TrackBase track)
    {
        // Track を Animatable<object> でラップ
        var anim = new TrackAnimatable(track, _clip.Duration);
        var entry = player.Play<object>(anim, value =>
        {
            _target.Apply(track.TargetPath, value);
        }, clock, timeScale: _timeScale, loop: _loop);
        entry.StartTime = startTimeAbs;
    }

    /// <summary>TrackBase を IAnimatable&lt;object&gt; として包む。Sample 値はオブジェクトとして返す。</summary>
    private sealed class TrackAnimatable : IAnimatable<object>
    {
        private readonly TrackBase _track;
        public float Duration { get; }

        public TrackAnimatable(TrackBase track, float clipDuration)
        {
            _track = track;
            Duration = clipDuration;   // Clip 全体の Duration に合わせる (Loop 時の境界も統一)
        }

        public object Evaluate(float timeSec)
        {
            // TrackBase 側で Sample → Apply するが、ここでは Apply は target に直接呼びたいため、
            // 値だけ取り出す仕組みが必要。型消去された Sample を持つ helper を作る。
            return TrackValue.Sample(_track, timeSec);
        }
    }
}

/// <summary>TrackBase から型消去で値を取り出す。Track&lt;T&gt; の Sample を object として返す。</summary>
internal static class TrackValue
{
    public static object Sample(TrackBase track, float timeSec)
    {
        // 型ディスパッチ — 主要型を全てここで網羅。他は reflection も検討。
        return track switch
        {
            Track<float> t => t.Sample(timeSec),
            Track<System.Numerics.Vector2> t => t.Sample(timeSec),
            Track<System.Numerics.Vector3> t => t.Sample(timeSec),
            Track<System.Numerics.Vector4> t => t.Sample(timeSec),
            Track<System.Numerics.Quaternion> t => t.Sample(timeSec),
            Track<uint> t => (object)t.Sample(timeSec),
            _ => throw new NotSupportedException($"Track<T> for {track.GetType()} not supported"),
        };
    }
}
