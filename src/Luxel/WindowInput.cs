namespace Luxel;

/// <summary>Portable logical keys reported by a window backend.</summary>
public enum WindowKey
{
    Unknown,
    Tab, Enter, Space, Escape, Backspace, Delete, Insert,
    Left, Right, Up, Down, Home, End, PageUp, PageDown,
    A, B, C, D, E, F, G, H, I, J, K, L, M, N, O, P, Q, R, S, T, U, V, W, X, Y, Z,
    D0, D1, D2, D3, D4, D5, D6, D7, D8, D9,
    F1, F2, F3, F4, F5, F6, F7, F8, F9, F10, F11, F12,
    Slash,
    LeftShift, RightShift, LeftControl, RightControl, LeftAlt, RightAlt,
}

[Flags]
public enum WindowKeyModifiers
{
    None = 0,
    Control = 1,
    Shift = 2,
    Alt = 4,
    Meta = 8,
}

public enum WindowPointerButton
{
    None,
    Left,
    Right,
    Middle,
    X1,
    X2,
}

public readonly record struct WindowKeyEvent(
    WindowKey Key,
    WindowKeyModifiers Modifiers = WindowKeyModifiers.None,
    bool IsRepeat = false);

public readonly record struct WindowPointerEvent(
    float X,
    float Y,
    WindowPointerButton Button = WindowPointerButton.None,
    WindowKeyModifiers Modifiers = WindowKeyModifiers.None);

public readonly record struct WindowWheelEvent(
    float X,
    float Y,
    float Delta,
    WindowKeyModifiers Modifiers = WindowKeyModifiers.None);
