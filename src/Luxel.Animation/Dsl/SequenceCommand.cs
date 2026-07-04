namespace Luxel.Animation;

/// <summary>
/// 子コマンドを順番に再生する。前の子の TotalDuration が経過したら次の子が開始。
/// </summary>
public sealed class SequenceCommand : IAnimationCommand
{
    private readonly List<IAnimationCommand> _children;
    private Action? _onComplete;

    public float TotalDuration { get; }

    internal SequenceCommand(IAnimationCommand[] children)
    {
        _children = new List<IAnimationCommand>(children);
        float total = 0f;
        foreach (var c in _children) total += c.TotalDuration;
        TotalDuration = total;
    }

    public SequenceCommand OnComplete(Action action) { _onComplete = action; return this; }

    public void Schedule(AnimationPlayer player, IClock clock, float startTimeAbs)
    {
        float offset = 0f;
        for (int i = 0; i < _children.Count; i++)
        {
            var child = _children[i];
            child.Schedule(player, clock, startTimeAbs + offset);
            offset += child.TotalDuration;
        }
        if (_onComplete != null)
        {
            // 最終時刻に発火する 1 frame だけの dummy track を投入
            ScheduleCompletionMarker(player, clock, startTimeAbs + offset, _onComplete);
        }
    }

    /// <summary>Player に投入して再生開始。</summary>
    public void Play(AnimationPlayer player, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(clock);
        Schedule(player, clock, clock.TimeSec);
    }

    internal static void ScheduleCompletionMarker(AnimationPlayer player, IClock clock, float startTimeAbs, Action onComplete)
    {
        // 長さ 0 の Tween でマーカーを置く (即完了で OnComplete 起動)。
        var marker = new Animatable<float> { Tween = new FloatTween(0f, 0f), Duration = 0f };
        var entry = player.Play(marker, _ => { }, clock);
        entry.StartTime = startTimeAbs;
        entry.OnComplete = onComplete;
    }
}
