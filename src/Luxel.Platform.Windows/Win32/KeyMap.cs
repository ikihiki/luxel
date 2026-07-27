using Luxel.UI;

namespace Luxel.Platform.Windows;

/// <summary>Win32 仮想キーコード → Luxel.UI.Key の対応表 (テスト可能, raw ushort)。</summary>
public static class KeyMap
{
    public static Key FromWindowKey(WindowKey key) => key switch
    {
        WindowKey.Tab => Key.Tab, WindowKey.Enter => Key.Enter, WindowKey.Escape => Key.Escape,
        WindowKey.Space => Key.Space, WindowKey.Left => Key.Left, WindowKey.Up => Key.Up,
        WindowKey.Right => Key.Right, WindowKey.Down => Key.Down, WindowKey.Home => Key.Home,
        WindowKey.End => Key.End, WindowKey.Backspace => Key.Backspace, WindowKey.Delete => Key.Delete,
        WindowKey.PageUp => Key.PageUp, WindowKey.PageDown => Key.PageDown,
        >= WindowKey.A and <= WindowKey.Z => Key.A + (key - WindowKey.A),
        >= WindowKey.D0 and <= WindowKey.D9 => Key.D0 + (key - WindowKey.D0),
        >= WindowKey.F1 and <= WindowKey.F12 => Key.F1 + (key - WindowKey.F1),
        WindowKey.Slash => Key.Slash,
        _ => Key.None,
    };

    public static Key FromVk(ushort vk) => vk switch
    {
        0x09 => Key.Tab,
        0x0D => Key.Enter,
        0x1B => Key.Escape,
        0x20 => Key.Space,
        0x25 => Key.Left,
        0x26 => Key.Up,
        0x27 => Key.Right,
        0x28 => Key.Down,
        0x24 => Key.Home,
        0x23 => Key.End,
        0x08 => Key.Backspace,
        0x2E => Key.Delete,
        0x21 => Key.PageUp,
        0x22 => Key.PageDown,
        // ショートカット用の文字キー。無修飾の文字入力は WM_CHAR 側が担う
        >= 0x41 and <= 0x5A => (char)('A' + (vk - 0x41)) switch
        {
            'A' => Key.A, 'B' => Key.B, 'C' => Key.C, 'D' => Key.D, 'E' => Key.E, 'F' => Key.F,
            'G' => Key.G, 'H' => Key.H, 'I' => Key.I, 'J' => Key.J, 'K' => Key.K, 'L' => Key.L,
            'M' => Key.M, 'N' => Key.N, 'O' => Key.O, 'P' => Key.P, 'Q' => Key.Q, 'R' => Key.R,
            'S' => Key.S, 'T' => Key.T, 'U' => Key.U, 'V' => Key.V, 'W' => Key.W, 'X' => Key.X,
            'Y' => Key.Y, 'Z' => Key.Z, _ => Key.None,
        },
        >= 0x30 and <= 0x39 => Key.D0 + (vk - 0x30),   // 数字段 (Key.D0..D9 は連番)
        >= 0x70 and <= 0x7B => Key.F1 + (vk - 0x70),   // F1..F12 (連番)
        0xBF => Key.Slash,   // VK_OEM_2 (US 配列の / ?)

        _ => Key.None,
    };
}
