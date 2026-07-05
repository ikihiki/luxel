using Luxel.Input;
using Windows.Win32.UI.Input.KeyboardAndMouse;

namespace Luxel.Platform;

/// <summary>
/// Win32 の keyboard / mouse event を <see cref="IInputSource"/> として <see cref="InputBus"/> に流す実装。
/// <see cref="AppWindow"/> が本ソースをインスタンス化し、<see cref="Win32Window"/> の callback から差分イベントを注入する。
/// vk コード → <see cref="KeyCode"/> のマッピングは <see cref="VkToKey"/> で行う。
/// </summary>
public sealed class Win32InputSource : IInputSource
{
    public string Name => "Win32";
    private readonly List<InputEvent> _pending = new();

    /// <summary>Win32Window の KeyDowned callback から呼ぶ。</summary>
    internal void HandleKeyDown(VIRTUAL_KEY vk)
    {
        var kc = VkToKey((ushort)vk);
        if (kc != KeyCode.None)
            _pending.Add(new InputEvent(InputEventKind.KeyDown, kc, AxisCode.None, 1f, 0f, 0));
    }

    /// <summary>Win32Window の KeyUpped callback から呼ぶ。</summary>
    internal void HandleKeyUp(VIRTUAL_KEY vk)
    {
        var kc = VkToKey((ushort)vk);
        if (kc != KeyCode.None)
            _pending.Add(new InputEvent(InputEventKind.KeyUp, kc, AxisCode.None, 0f, 0f, 0));
    }

    internal void HandleMouseDown(int button)
    {
        var kc = button switch { 0 => KeyCode.Mouse0, 1 => KeyCode.Mouse1, 2 => KeyCode.Mouse2, _ => KeyCode.None };
        if (kc != KeyCode.None) _pending.Add(new InputEvent(InputEventKind.KeyDown, kc, AxisCode.None, 1f, 0f, 0));
    }

    internal void HandleMouseUp(int button)
    {
        var kc = button switch { 0 => KeyCode.Mouse0, 1 => KeyCode.Mouse1, 2 => KeyCode.Mouse2, _ => KeyCode.None };
        if (kc != KeyCode.None) _pending.Add(new InputEvent(InputEventKind.KeyUp, kc, AxisCode.None, 0f, 0f, 0));
    }

    internal void HandlePointer(float x, float y)
        => _pending.Add(new InputEvent(InputEventKind.PointerMoved, KeyCode.None, AxisCode.None, x, y, 0));

    internal void HandleWheel(float delta)
        => _pending.Add(new InputEvent(InputEventKind.AxisChanged, KeyCode.None, AxisCode.MouseWheel, delta, 0f, 0));

    public void Poll(InputBus bus)
    {
        foreach (var e in _pending) bus.Enqueue(e);
        _pending.Clear();
    }

    // === Win32 vk → KeyCode ===
    private static KeyCode VkToKey(ushort vk) => vk switch
    {
        // 英字 (0x41='A' .. 0x5A='Z')
        >= 0x41 and <= 0x5A => (KeyCode)((int)KeyCode.A + (vk - 0x41)),
        // 数字 (0x30='0' .. 0x39='9')
        >= 0x30 and <= 0x39 => (KeyCode)((int)KeyCode.Num0 + (vk - 0x30)),
        // ファンクション
        >= 0x70 and <= 0x7B => (KeyCode)((int)KeyCode.F1 + (vk - 0x70)),
        // 制御キー
        0x20 => KeyCode.Space,
        0x0D => KeyCode.Enter,
        0x1B => KeyCode.Escape,
        0x09 => KeyCode.Tab,
        0x08 => KeyCode.Backspace,
        0x2E => KeyCode.Delete,
        0x2D => KeyCode.Insert,
        0x25 => KeyCode.Left,
        0x27 => KeyCode.Right,
        0x26 => KeyCode.Up,
        0x28 => KeyCode.Down,
        0x24 => KeyCode.Home,
        0x23 => KeyCode.End,
        0x21 => KeyCode.PageUp,
        0x22 => KeyCode.PageDown,
        0xA0 => KeyCode.LeftShift,
        0xA1 => KeyCode.RightShift,
        0xA2 => KeyCode.LeftCtrl,
        0xA3 => KeyCode.RightCtrl,
        0xA4 => KeyCode.LeftAlt,
        0xA5 => KeyCode.RightAlt,
        _ => KeyCode.None,
    };
}
