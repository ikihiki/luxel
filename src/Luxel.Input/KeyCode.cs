namespace Luxel.Input;

/// <summary>
/// 物理キーコード。プラットフォームの vk/scan code とは独立 ─ 各 <see cref="IInputSource"/> 実装が
/// マッピングを担う (Win32 → <see cref="KeyCode"/>、XInput → <see cref="KeyCode"/> の gamepad 群)。
/// UI 用の論理キー <see cref="Luxel.UI.Key"/> とは別レイヤー (UI は Tab/Enter 等の意味的キー、
/// Input は「押されたのは A キー」の物理キー)。
/// </summary>
public enum KeyCode
{
    None = 0,

    // === 英字 (Win32 vk 0x41..0x5A に対応)
    A = 1, B, C, D, E, F, G, H, I, J, K, L, M, N, O, P, Q, R, S, T, U, V, W, X, Y, Z,
    // === 数字
    Num0, Num1, Num2, Num3, Num4, Num5, Num6, Num7, Num8, Num9,
    // === ファンクション
    F1, F2, F3, F4, F5, F6, F7, F8, F9, F10, F11, F12,
    // === 制御
    Space, Enter, Escape, Tab, Backspace, Delete, Insert,
    Left, Right, Up, Down, Home, End, PageUp, PageDown,
    LeftShift, RightShift, LeftCtrl, RightCtrl, LeftAlt, RightAlt,

    // === マウスボタン (Source は別だが event 型を統一するため同 enum 内)
    Mouse0 = 200, Mouse1, Mouse2, Mouse3, Mouse4,
    MouseWheelUp, MouseWheelDown,

    // === Gamepad (XInput 相当、INPUT-M8 で実装)
    GamepadA = 300, GamepadB, GamepadX, GamepadY,
    GamepadLB, GamepadRB, GamepadBack, GamepadStart,
    GamepadDPadUp, GamepadDPadDown, GamepadDPadLeft, GamepadDPadRight,
    GamepadLeftStick, GamepadRightStick,
    // Axis 系 (float は KeyCode ではなく Axis enum で扱うが、button 化された stick 4 方向はここ)
}

/// <summary>アナログ軸 (float / Vector2) 用の識別子。</summary>
public enum AxisCode
{
    None = 0,
    MouseX, MouseY, MouseWheel,
    GamepadLeftStickX, GamepadLeftStickY,
    GamepadRightStickX, GamepadRightStickY,
    GamepadLeftTrigger, GamepadRightTrigger,
}
