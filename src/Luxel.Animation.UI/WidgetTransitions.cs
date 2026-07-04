using Luxel.Animation;
using Luxel.UI;

namespace Luxel.Animation.UI;

/// <summary>
/// 添付プロパティ (<c>P.Transition.Color(...)</c> 等) から TransitionSpec を抽出し、
/// raw setter を <see cref="Transition.Animate"/> でラップしたものを返すヘルパ群。
///
/// 使用パターン (Widget 自身の修正なし、ユーザーが parts を読んで自前で配線):
/// <code>
/// INodePart[] parts = [
///     P.Transition.Color(0.25f, CubicBezierCurve.EaseInOut),
///     P.Transition.Scale(0.15f, CubicBezierCurve.EaseOut),
/// ];
/// var colorSetter = WidgetTransitions.Wrap&lt;uint&gt;(parts, TransitionKeys.Color, v =&gt; node.Color = v, player, clock);
/// var scaleSetter = WidgetTransitions.Wrap&lt;float&gt;(parts, TransitionKeys.Scale, v =&gt; ApplyScale(v), player, clock);
/// </code>
///
/// Widget 経由でも使える (Widget.GetAttached を読む):
/// <code>
/// var setter = WidgetTransitions.WrapFromWidget&lt;uint&gt;(widget, TransitionKeys.Color, raw, player, clock);
/// </code>
/// </summary>
public static class WidgetTransitions
{
    /// <summary>INodePart 配列から指定キーの TransitionSpec を取り出す。</summary>
    public static TransitionSpec? FindSpec(INodePart[] parts, string key)
    {
        if (parts == null) return null;
        foreach (var p in parts)
        {
            if (p is TransitionAttachment ta && ta.Key == key) return ta.Spec;
        }
        return null;
    }

    /// <summary>
    /// 添付パーツから TransitionSpec を抽出し、見つかれば <see cref="Transition.Animate"/> でラップした setter を返す。
    /// 見つからない (or Duration=0) なら rawSetter をそのまま返す。
    /// </summary>
    public static Action<T> Wrap<T>(INodePart[] parts, string key, Action<T> rawSetter,
                                     AnimationPlayer player, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(rawSetter);
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(clock);
        var spec = FindSpec(parts, key);
        if (spec is null || spec.Value.Duration <= 0f) return rawSetter;
        return Transition.Animate(rawSetter, player, clock, spec.Value.Duration, spec.Value.Curve, spec.Value.Delay);
    }

    /// <summary>Widget の <see cref="Widget.GetAttached{T}"/> から spec を取って同様にラップする。</summary>
    public static Action<T> WrapFromWidget<T>(Widget widget, string key, Action<T> rawSetter,
                                                AnimationPlayer player, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(widget);
        ArgumentNullException.ThrowIfNull(rawSetter);
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(clock);
        var spec = widget.GetAttached<TransitionSpec>(key, default);
        if (spec.Duration <= 0f) return rawSetter;
        return Transition.Animate(rawSetter, player, clock, spec.Duration, spec.Curve, spec.Delay);
    }

}
