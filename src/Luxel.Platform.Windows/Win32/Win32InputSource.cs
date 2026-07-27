using Luxel.Input;

namespace Luxel.Platform.Windows;

/// <summary>
/// Win32 の keyboard / mouse event を <see cref="IInputSource"/> として <see cref="InputBus"/> に流す実装。
/// Win32ウィンドウの入力callbackから差分イベントを注入して使う。
/// portable <see cref="WindowKey"/> → <see cref="KeyCode"/> のマッピングは内部対応表で行う。
/// </summary>
public sealed class Win32InputSource : IInputSource
{
    public string Name => "Win32";
    private readonly List<InputEvent> _pending = new();

    /// <summary>Win32Window の KeyDowned callback から呼ぶ。</summary>
    internal void HandleKeyDown(WindowKeyEvent input)
    {
        var kc = ToKeyCode(input.Key);
        if (kc != KeyCode.None)
            _pending.Add(new InputEvent(InputEventKind.KeyDown, kc, AxisCode.None, 1f, 0f, 0));
    }

    /// <summary>Win32Window の KeyUpped callback から呼ぶ。</summary>
    internal void HandleKeyUp(WindowKeyEvent input)
    {
        var kc = ToKeyCode(input.Key);
        if (kc != KeyCode.None)
            _pending.Add(new InputEvent(InputEventKind.KeyUp, kc, AxisCode.None, 0f, 0f, 0));
    }

    internal void HandlePointerDown(WindowPointerEvent input)
    {
        var kc = PointerKey(input.Button);
        if (kc != KeyCode.None) _pending.Add(new InputEvent(InputEventKind.KeyDown, kc, AxisCode.None, 1f, 0f, 0));
    }

    internal void HandlePointerUp(WindowPointerEvent input)
    {
        var kc = PointerKey(input.Button);
        if (kc != KeyCode.None) _pending.Add(new InputEvent(InputEventKind.KeyUp, kc, AxisCode.None, 0f, 0f, 0));
    }

    internal void HandlePointer(WindowPointerEvent input)
        => _pending.Add(new InputEvent(InputEventKind.PointerMoved, KeyCode.None, AxisCode.None, input.X, input.Y, 0));

    internal void HandleWheel(WindowWheelEvent input)
        => _pending.Add(new InputEvent(InputEventKind.AxisChanged, KeyCode.None, AxisCode.MouseWheel, input.Delta, 0f, 0));

    public void Poll(InputBus bus)
    {
        foreach (var e in _pending) bus.Enqueue(e);
        _pending.Clear();
    }

    private static KeyCode PointerKey(WindowPointerButton button) => button switch
    {
        WindowPointerButton.Left => KeyCode.Mouse0,
        WindowPointerButton.Right => KeyCode.Mouse1,
        WindowPointerButton.Middle => KeyCode.Mouse2,
        _ => KeyCode.None,
    };

    private static KeyCode ToKeyCode(WindowKey key) => key switch
    {
        >= WindowKey.A and <= WindowKey.Z => (KeyCode)((int)KeyCode.A + (key - WindowKey.A)),
        >= WindowKey.D0 and <= WindowKey.D9 => (KeyCode)((int)KeyCode.Num0 + (key - WindowKey.D0)),
        >= WindowKey.F1 and <= WindowKey.F12 => (KeyCode)((int)KeyCode.F1 + (key - WindowKey.F1)),
        WindowKey.Space => KeyCode.Space, WindowKey.Enter => KeyCode.Enter, WindowKey.Escape => KeyCode.Escape,
        WindowKey.Tab => KeyCode.Tab, WindowKey.Backspace => KeyCode.Backspace, WindowKey.Delete => KeyCode.Delete,
        WindowKey.Insert => KeyCode.Insert, WindowKey.Left => KeyCode.Left, WindowKey.Right => KeyCode.Right,
        WindowKey.Up => KeyCode.Up, WindowKey.Down => KeyCode.Down, WindowKey.Home => KeyCode.Home,
        WindowKey.End => KeyCode.End, WindowKey.PageUp => KeyCode.PageUp, WindowKey.PageDown => KeyCode.PageDown,
        WindowKey.LeftShift => KeyCode.LeftShift, WindowKey.RightShift => KeyCode.RightShift,
        WindowKey.LeftControl => KeyCode.LeftCtrl, WindowKey.RightControl => KeyCode.RightCtrl,
        WindowKey.LeftAlt => KeyCode.LeftAlt, WindowKey.RightAlt => KeyCode.RightAlt,
        _ => KeyCode.None,
    };
}
