namespace Luxel.Platform;

internal static class Win32KeyMap
{
    public static WindowKey FromVirtualKey(ushort vk) => vk switch
    {
        >= 0x41 and <= 0x5A => (WindowKey)((int)WindowKey.A + (vk - 0x41)),
        >= 0x30 and <= 0x39 => (WindowKey)((int)WindowKey.D0 + (vk - 0x30)),
        >= 0x70 and <= 0x7B => (WindowKey)((int)WindowKey.F1 + (vk - 0x70)),
        0x09 => WindowKey.Tab, 0x0D => WindowKey.Enter, 0x20 => WindowKey.Space,
        0x1B => WindowKey.Escape, 0x08 => WindowKey.Backspace, 0x2E => WindowKey.Delete,
        0x2D => WindowKey.Insert, 0x25 => WindowKey.Left, 0x27 => WindowKey.Right,
        0x26 => WindowKey.Up, 0x28 => WindowKey.Down, 0x24 => WindowKey.Home,
        0x23 => WindowKey.End, 0x21 => WindowKey.PageUp, 0x22 => WindowKey.PageDown,
        0xBF => WindowKey.Slash,
        0xA0 => WindowKey.LeftShift, 0xA1 => WindowKey.RightShift,
        0xA2 => WindowKey.LeftControl, 0xA3 => WindowKey.RightControl,
        0xA4 => WindowKey.LeftAlt, 0xA5 => WindowKey.RightAlt,
        _ => WindowKey.Unknown,
    };
}
