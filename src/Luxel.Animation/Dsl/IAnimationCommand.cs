namespace Luxel.Animation;

/// <summary>
/// Animation コマンド = 「Player に 1 つ以上の TrackEntry を時間オフセット付きで投入する」操作。
/// <see cref="TweenCommand{T}"/>, <see cref="SequenceCommand"/>, <see cref="ParallelCommand"/> 等の
/// 上層で組み合わせ可能。Player は最終的に低レベル TrackEntry を回す。
/// </summary>
public interface IAnimationCommand
{
    /// <summary>このコマンド全体の再生時間 (秒)。Sequence は子の合計、Parallel は最大、Tween は Duration。</summary>
    float TotalDuration { get; }

    /// <summary>
    /// startTimeAbs 時刻から再生開始するよう、子の TrackEntry を player に投入する。
    /// Sequence は子を時間オフセットで連鎖、Parallel は同時に投入。
    /// </summary>
    void Schedule(AnimationPlayer player, IClock clock, float startTimeAbs);
}
