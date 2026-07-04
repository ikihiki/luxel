namespace Luxel.Animation.UI;

using Luxel.UI;

/// <summary>
/// <see cref="Transition"/> を <see cref="Signal{T}"/> と結ぶ糖衣。Signal 値の変化を ReactiveEffect で
/// 購読し、自動的に補間 setter (animatedSetter) を呼び出す。
///
/// 使い方:
/// <code>
/// var hovered = new Signal&lt;bool&gt;(false);
/// var animatedColor = Transition.Animate&lt;uint&gt;(v =&gt; node.Color = v, player, clock, 0.3f);
/// using var sub = SignalTransition.Watch(hovered, h =&gt; animatedColor(h ? Red : Blue));
/// // hovered.Value = true を呼ぶと、color が自動的に補間される
/// </code>
/// </summary>
public static class SignalTransition
{
    /// <summary>
    /// Signal の値変化を <paramref name="animatedSetter"/> に流す ReactiveEffect を起こす。
    /// 返り値の <see cref="IDisposable"/> を Dispose すると購読停止 (進行中 transition は AnimationPlayer 側で自然に終了)。
    /// </summary>
    public static IDisposable Watch<T>(Signal<T> source, Action<T> animatedSetter)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(animatedSetter);
        return Reactive.Effect(() => animatedSetter(source.Value));
    }

    /// <summary>computed/関数式の値変化を購読する変種 (毎回 lambda の結果を取り、変化なら setter を呼ぶ)。</summary>
    public static IDisposable Watch<T>(Func<T> compute, Action<T> animatedSetter)
    {
        ArgumentNullException.ThrowIfNull(compute);
        ArgumentNullException.ThrowIfNull(animatedSetter);
        return Reactive.Effect(() => animatedSetter(compute()));
    }
}
