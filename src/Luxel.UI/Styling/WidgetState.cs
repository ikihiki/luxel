namespace Luxel.UI;

/// <summary>
/// Tailwind / CSS の擬似クラス相当。<see cref="Bindable{T}"/> の状態レイヤ
/// (<see cref="Bindable{T}.SetState"/>) と <see cref="Widget.IsStateActive"/> で使う。
///
/// Tailwind の state prefix (<c>hover:</c>/<c>focus:</c>/<c>active:</c>/<c>disabled:</c>) を C# 型安全な enum で表現。
/// </summary>
public enum WidgetState
{
    /// <summary>状態指定なし (基本スタイル)。Tailwind の prefix なしに相当。</summary>
    Default,
    /// <summary>マウスホバー中。Tailwind <c>hover:</c>。</summary>
    Hover,
    /// <summary>押下中。Tailwind <c>active:</c>。</summary>
    Pressed,
    /// <summary>キーボードフォーカス中。Tailwind <c>focus:</c>。</summary>
    Focused,
    /// <summary>非活性。Tailwind <c>disabled:</c>。</summary>
    Disabled,
    /// <summary>チェック済 (CheckBox/Switch/Toggle)。Tailwind <c>checked:</c>。</summary>
    Checked,
    /// <summary>選択中 (Tab/List item)。Tailwind の selected (<c>aria-selected:</c>) 相当。</summary>
    Selected,
}
