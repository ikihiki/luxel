using Luxel.Animation;
using Luxel.UI.Styling;

namespace Luxel.Animation.UI;

/// <summary>
/// 状態切替時のプロパティ別 Transition 仕様を束ねる record。
/// <c>StateStyle</c> と並行構造で、対応プロパティが値変化したら自動補間する。
///
/// <para>tuple 暗黙変換 (<see cref="TransitionSpec"/>) を使って:</para>
/// <code>
/// transitions: new TransitionSet {
///     Background = (0.3f, CubicBezierCurve.EaseInOut),
///     Scale = (0.1f, CubicBezierCurve.EaseOut),
///     Opacity = 0.2f,    // float 一個 → duration のみ
/// }
/// </code>
/// </summary>
public sealed record TransitionSet
{
    public TransitionSpec? Background { get; init; }
    public TransitionSpec? Foreground { get; init; }
    public TransitionSpec? Opacity { get; init; }
    public TransitionSpec? Scale { get; init; }
    public TransitionSpec? Translate { get; init; }
    public TransitionSpec? Rotate { get; init; }
    public TransitionSpec? Rounded { get; init; }
    public TransitionSpec? BorderColor { get; init; }
    public TransitionSpec? BorderWidth { get; init; }
    public TransitionSpec? Padding { get; init; }
    public TransitionSpec? Margin { get; init; }
    public TransitionSpec? Width { get; init; }
    public TransitionSpec? Height { get; init; }
    public TransitionSpec? FontSize { get; init; }

    /// <summary>空 (全フィールド null = 補間なし)。</summary>
    public static readonly TransitionSet Empty = new();
}
