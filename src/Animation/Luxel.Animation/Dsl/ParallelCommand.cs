namespace Luxel.Animation;

/// <summary>子コマンドを同時に再生する。TotalDuration は子の最大。</summary>
public sealed class ParallelCommand : IAnimationCommand
{
    private readonly List<IAnimationCommand> _children;
    private Action? _onComplete;

    public float TotalDuration { get; }

    internal ParallelCommand(IAnimationCommand[] children)
    {
        _children = new List<IAnimationCommand>(children);
        float maxDur = 0f;
        foreach (var c in _children)
            if (c.TotalDuration > maxDur) maxDur = c.TotalDuration;
        TotalDuration = maxDur;
    }

    public ParallelCommand OnComplete(Action action) { _onComplete = action; return this; }

    public void Schedule(AnimationPlayer player, IClock clock, float startTimeAbs)
    {
        foreach (var c in _children) c.Schedule(player, clock, startTimeAbs);
        if (_onComplete != null)
        {
            SequenceCommand.ScheduleCompletionMarker(player, clock, startTimeAbs + TotalDuration, _onComplete);
        }
    }

    public void Play(AnimationPlayer player, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(clock);
        Schedule(player, clock, clock.TimeSec);
    }
}
