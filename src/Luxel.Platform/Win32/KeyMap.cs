using Luxel.UI;

namespace Luxel.Platform;

/// <summary>Win32 仮想キーコード → Luxel.UI.Key の対応表 (テスト可能, raw ushort)。</summary>
public static class KeyMap
{
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
        // ショートカット用の文字キー (Ctrl+A/B/C/E/I/V/X/Y/Z)。無修飾の文字入力は WM_CHAR 側が担う
        0x41 => Key.A,
        0x42 => Key.B,
        0x43 => Key.C,
        0x45 => Key.E,
        0x49 => Key.I,
        0x56 => Key.V,
        0x58 => Key.X,
        0x59 => Key.Y,
        0x5A => Key.Z,

        _ => Key.None,
    };
}
