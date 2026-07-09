using System.Runtime.InteropServices;
using Luxel.UI;

namespace Luxel.Platform;

/// <summary>Win32 メッセージ処理中に現在の修飾キー状態を <c>GetKeyState</c> で拾う。
/// デリゲートは WndProc から同期呼び出しされるため、この時点の <c>GetKeyState</c> は
/// 「今処理しているメッセージ時点」の状態を返す (ADR-0011: グローバル状態を後から読まない)。</summary>
internal static partial class Win32Modifiers
{
    private const int VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_MENU = 0x12, VK_LWIN = 0x5B, VK_RWIN = 0x5C;
    private const int Down = 0x8000;

    [LibraryImport("user32.dll")]
    private static partial short GetKeyState(int nVirtKey);

    public static KeyModifiers Current()
    {
        KeyModifiers m = KeyModifiers.None;
        if ((GetKeyState(VK_CONTROL) & Down) != 0) m |= KeyModifiers.Ctrl;
        if ((GetKeyState(VK_SHIFT) & Down) != 0) m |= KeyModifiers.Shift;
        if ((GetKeyState(VK_MENU) & Down) != 0) m |= KeyModifiers.Alt;
        if (((GetKeyState(VK_LWIN) | GetKeyState(VK_RWIN)) & Down) != 0) m |= KeyModifiers.Meta;
        return m;
    }

    /// <summary>Win32 のマウスボタン番号 (0=L, 1=R, 2=M) を <see cref="PointerButton"/> へ。</summary>
    public static PointerButton Button(int b) => b switch { 1 => PointerButton.Right, 2 => PointerButton.Middle, _ => PointerButton.Left };
}
