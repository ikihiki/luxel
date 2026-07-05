namespace Luxel.Animation.UI;

using global::Luxel.UI;

/// <summary>
/// Luxel.UI の <see cref="Signal{T}"/> をターゲットにする補助。
/// AnimationPlayer.Play(animatable, target.For(signal)) で Signal を直接アニメート可能。
/// Animation core は Luxel.UI を知らず、本 Adapter で疎結合に接続する。
/// </summary>
public static class SignalAnimationTarget
{
    /// <summary>Signal を Action&lt;T&gt; setter として受け取るヘルパ。AnimationPlayer.Play の第 2 引数に渡す。</summary>
    public static Action<T> For<T>(Signal<T> signal)
    {
        ArgumentNullException.ThrowIfNull(signal);
        return v => signal.Value = v;
    }
}

/// <summary>
/// AnimationPlayer を <see cref="UiHost"/> のフレームに結線する拡張。
/// Animation 側は絶対時刻 (<see cref="IClock"/>) で駆動、UI 側は dt 累積で Tick するため、ここで橋渡し。
/// </summary>
public static class AnimationUiBridge
{
    /// <summary>
    /// AnimationPlayer を IClock で更新し、UiHost を dt で進める標準ヘルパ。
    /// ユーザーは <c>FixedFrameClock</c> や <c>WallClock</c> を渡してフレームの絶対時刻を制御する。
    /// </summary>
    public static void Drive(AnimationPlayer player, UiHost host, IClock clock, float dt)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(clock);
        player.Update(clock);
        host.Tick(dt);
    }
}
