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

    public static WindowKeyModifiers CurrentWindow()
    {
        WindowKeyModifiers m = WindowKeyModifiers.None;
        if ((GetKeyState(VK_CONTROL) & Down) != 0) m |= WindowKeyModifiers.Control;
        if ((GetKeyState(VK_SHIFT) & Down) != 0) m |= WindowKeyModifiers.Shift;
        if ((GetKeyState(VK_MENU) & Down) != 0) m |= WindowKeyModifiers.Alt;
        if (((GetKeyState(VK_LWIN) | GetKeyState(VK_RWIN)) & Down) != 0) m |= WindowKeyModifiers.Meta;
        return m;
    }

    public static KeyModifiers ToUi(WindowKeyModifiers modifiers)
    {
        KeyModifiers m = KeyModifiers.None;
        if (modifiers.HasFlag(WindowKeyModifiers.Control)) m |= KeyModifiers.Ctrl;
        if (modifiers.HasFlag(WindowKeyModifiers.Shift)) m |= KeyModifiers.Shift;
        if (modifiers.HasFlag(WindowKeyModifiers.Alt)) m |= KeyModifiers.Alt;
        if (modifiers.HasFlag(WindowKeyModifiers.Meta)) m |= KeyModifiers.Meta;
        return m;
    }

    public static KeyModifiers Current() => ToUi(CurrentWindow());

    public static PointerButton ToUi(WindowPointerButton button) => button switch
    {
        WindowPointerButton.Right => PointerButton.Right,
        WindowPointerButton.Middle => PointerButton.Middle,
        _ => PointerButton.Left,
    };
}
