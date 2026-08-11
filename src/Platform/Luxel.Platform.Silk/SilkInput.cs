using System.Text;
using Silk.NET.GLFW;

namespace Luxel.Platform.Silk;

internal static class SilkInput
{
    public static WindowKey MapKey(Keys key) => key switch
    {
        >= Keys.A and <= Keys.Z => (WindowKey)((int)WindowKey.A + ((int)key - (int)Keys.A)),
        >= Keys.Number0 and <= Keys.Number9 => (WindowKey)((int)WindowKey.D0 + ((int)key - (int)Keys.Number0)),
        >= Keys.F1 and <= Keys.F12 => (WindowKey)((int)WindowKey.F1 + ((int)key - (int)Keys.F1)),
        Keys.Tab => WindowKey.Tab,
        Keys.Enter or Keys.KeypadEnter => WindowKey.Enter,
        Keys.Space => WindowKey.Space,
        Keys.Escape => WindowKey.Escape,
        Keys.Backspace => WindowKey.Backspace,
        Keys.Delete => WindowKey.Delete,
        Keys.Insert => WindowKey.Insert,
        Keys.Left => WindowKey.Left,
        Keys.Right => WindowKey.Right,
        Keys.Up => WindowKey.Up,
        Keys.Down => WindowKey.Down,
        Keys.Home => WindowKey.Home,
        Keys.End => WindowKey.End,
        Keys.PageUp => WindowKey.PageUp,
        Keys.PageDown => WindowKey.PageDown,
        Keys.Slash or Keys.KeypadDivide => WindowKey.Slash,
        Keys.ShiftLeft => WindowKey.LeftShift,
        Keys.ShiftRight => WindowKey.RightShift,
        Keys.ControlLeft => WindowKey.LeftControl,
        Keys.ControlRight => WindowKey.RightControl,
        Keys.AltLeft => WindowKey.LeftAlt,
        Keys.AltRight => WindowKey.RightAlt,
        _ => WindowKey.Unknown,
    };

    public static WindowKeyModifiers MapModifiers(KeyModifiers modifiers)
    {
        WindowKeyModifiers result = WindowKeyModifiers.None;
        if ((modifiers & KeyModifiers.Control) != 0) result |= WindowKeyModifiers.Control;
        if ((modifiers & KeyModifiers.Shift) != 0) result |= WindowKeyModifiers.Shift;
        if ((modifiers & KeyModifiers.Alt) != 0) result |= WindowKeyModifiers.Alt;
        if ((modifiers & KeyModifiers.Super) != 0) result |= WindowKeyModifiers.Meta;
        return result;
    }

    public static WindowPointerButton MapButton(MouseButton button) => button switch
    {
        MouseButton.Left => WindowPointerButton.Left,
        MouseButton.Right => WindowPointerButton.Right,
        MouseButton.Middle => WindowPointerButton.Middle,
        MouseButton.Button4 => WindowPointerButton.X1,
        MouseButton.Button5 => WindowPointerButton.X2,
        _ => WindowPointerButton.None,
    };

    public static bool IsRepeat(InputAction action) => action == InputAction.Repeat;

    public static string? CodePointToString(uint codePoint)
        => codePoint <= int.MaxValue && Rune.TryCreate((int)codePoint, out Rune rune)
            ? rune.ToString()
            : null;
}
