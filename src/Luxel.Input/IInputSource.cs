namespace Luxel.Input;

/// <summary>物理入力イベント種別。</summary>
public enum InputEventKind
{
    KeyDown,
    KeyUp,
    AxisChanged,
    PointerMoved,
}

/// <summary>物理入力イベント (raw)。ソース間で共通形式にし <see cref="InputBus"/> に集約する。</summary>
public readonly record struct InputEvent(
    InputEventKind Kind,
    KeyCode Key,
    AxisCode Axis,
    float Value,        // KeyDown/KeyUp = 1/0、Axis = -1..1、Pointer = ピクセル
    float ValueY,       // 2D 軸 (mouse move / stick position の Y 成分)
    long TimestampTicks);

/// <summary>Platform Windowから入力源を生成するLuxel.Input側のfactory。</summary>
public static class WindowInput
{
    public static WindowInputSource CreateInputSource(this Luxel.Platform.Window window, string name = "Window")
        => new(window, name);
}

/// <summary>Windowの正規化済み入力イベントをInputBusへ変換する汎用入力源。</summary>
public sealed class WindowInputSource : IInputSource, Luxel.Platform.IWindowInputHandler, IDisposable
{
    private readonly Luxel.Platform.Window _window;
    private readonly object _gate = new();
    private readonly List<InputEvent> _pending = new();
    private readonly HashSet<KeyCode> _held = new();
    private KeyCode? _lastPressed;
    private bool _disposed;

    public WindowInputSource(Luxel.Platform.Window window, string name = "Window")
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));
        Name = name;
        _window.AddInputHandler(this);
        _window.FocusChanged += OnFocusChanged;
    }

    public string Name { get; }

    public void PointerMoved(Luxel.Platform.WindowPointerEvent input)
        => Add(new(InputEventKind.PointerMoved, KeyCode.None, AxisCode.None, input.X, input.Y, 0));

    public void PointerDown(Luxel.Platform.WindowPointerEvent input)
    {
        KeyCode key = PointerKey(input.Button);
        if (key != KeyCode.None) AddKeyDown(key, rememberForRebind: false);
    }

    public void PointerUp(Luxel.Platform.WindowPointerEvent input)
    {
        KeyCode key = PointerKey(input.Button);
        if (key != KeyCode.None) AddKeyUp(key);
    }

    public void Wheel(Luxel.Platform.WindowWheelEvent input)
        => Add(new(InputEventKind.AxisChanged, KeyCode.None, AxisCode.MouseWheel, input.Delta, 0f, 0));

    public void KeyDown(Luxel.Platform.WindowKeyEvent input)
    {
        KeyCode key = ToKeyCode(input.Key);
        if (key != KeyCode.None) AddKeyDown(key, rememberForRebind: true);
    }

    public void KeyUp(Luxel.Platform.WindowKeyEvent input)
    {
        KeyCode key = ToKeyCode(input.Key);
        if (key != KeyCode.None) AddKeyUp(key);
    }

    /// <summary>直近のキー押下を取り出してクリアする。キー設定UIなどで使用する。</summary>
    public KeyCode? TakePressed()
    {
        lock (_gate)
        {
            KeyCode? key = _lastPressed;
            _lastPressed = null;
            return key;
        }
    }

    public void Poll(InputBus bus)
    {
        ArgumentNullException.ThrowIfNull(bus);
        InputEvent[] events;
        lock (_gate) { events = _pending.ToArray(); _pending.Clear(); }
        foreach (InputEvent input in events) bus.Enqueue(input);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _pending.Clear();
            _held.Clear();
            _lastPressed = null;
        }
        _window.FocusChanged -= OnFocusChanged;
        _window.RemoveInputHandler(this);
    }

    /// <summary>現在保持中のキーとpointer buttonをrelease eventへ変換する。</summary>
    public void ReleaseAll()
    {
        lock (_gate)
        {
            if (_disposed) return;
            foreach (KeyCode key in _held.Order())
                _pending.Add(new(InputEventKind.KeyUp, key, AxisCode.None, 0f, 0f, 0));
            _held.Clear();
        }
    }

    private void OnFocusChanged(bool focused)
    {
        if (!focused) ReleaseAll();
    }

    private void AddKeyDown(KeyCode key, bool rememberForRebind)
    {
        lock (_gate)
        {
            if (_disposed) return;
            if (rememberForRebind) _lastPressed = key;
            if (_held.Add(key))
                _pending.Add(new(InputEventKind.KeyDown, key, AxisCode.None, 1f, 0f, 0));
        }
    }

    private void AddKeyUp(KeyCode key)
    {
        lock (_gate)
        {
            if (_disposed || !_held.Remove(key)) return;
            _pending.Add(new(InputEventKind.KeyUp, key, AxisCode.None, 0f, 0f, 0));
        }
    }

    private void Add(InputEvent input)
    {
        lock (_gate)
            if (!_disposed) _pending.Add(input);
    }

    private static KeyCode PointerKey(Luxel.Platform.WindowPointerButton button) => button switch
    {
        Luxel.Platform.WindowPointerButton.Left => KeyCode.Mouse0,
        Luxel.Platform.WindowPointerButton.Right => KeyCode.Mouse1,
        Luxel.Platform.WindowPointerButton.Middle => KeyCode.Mouse2,
        Luxel.Platform.WindowPointerButton.X1 => KeyCode.Mouse3,
        Luxel.Platform.WindowPointerButton.X2 => KeyCode.Mouse4,
        _ => KeyCode.None,
    };

    private static KeyCode ToKeyCode(Luxel.Platform.WindowKey key) => key switch
    {
        >= Luxel.Platform.WindowKey.A and <= Luxel.Platform.WindowKey.Z => (KeyCode)((int)KeyCode.A + (key - Luxel.Platform.WindowKey.A)),
        >= Luxel.Platform.WindowKey.D0 and <= Luxel.Platform.WindowKey.D9 => (KeyCode)((int)KeyCode.Num0 + (key - Luxel.Platform.WindowKey.D0)),
        >= Luxel.Platform.WindowKey.F1 and <= Luxel.Platform.WindowKey.F12 => (KeyCode)((int)KeyCode.F1 + (key - Luxel.Platform.WindowKey.F1)),
        Luxel.Platform.WindowKey.Space => KeyCode.Space, Luxel.Platform.WindowKey.Enter => KeyCode.Enter,
        Luxel.Platform.WindowKey.Escape => KeyCode.Escape, Luxel.Platform.WindowKey.Tab => KeyCode.Tab,
        Luxel.Platform.WindowKey.Backspace => KeyCode.Backspace, Luxel.Platform.WindowKey.Delete => KeyCode.Delete,
        Luxel.Platform.WindowKey.Insert => KeyCode.Insert, Luxel.Platform.WindowKey.Left => KeyCode.Left,
        Luxel.Platform.WindowKey.Right => KeyCode.Right, Luxel.Platform.WindowKey.Up => KeyCode.Up,
        Luxel.Platform.WindowKey.Down => KeyCode.Down, Luxel.Platform.WindowKey.Home => KeyCode.Home,
        Luxel.Platform.WindowKey.End => KeyCode.End, Luxel.Platform.WindowKey.PageUp => KeyCode.PageUp,
        Luxel.Platform.WindowKey.PageDown => KeyCode.PageDown, Luxel.Platform.WindowKey.LeftShift => KeyCode.LeftShift,
        Luxel.Platform.WindowKey.RightShift => KeyCode.RightShift, Luxel.Platform.WindowKey.LeftControl => KeyCode.LeftCtrl,
        Luxel.Platform.WindowKey.RightControl => KeyCode.RightCtrl, Luxel.Platform.WindowKey.LeftAlt => KeyCode.LeftAlt,
        Luxel.Platform.WindowKey.RightAlt => KeyCode.RightAlt,
        _ => KeyCode.None,
    };
}

/// <summary>
/// 物理入力の生成源。Window入力やXInputなどの具象sourceは本interfaceを実装し、
/// <see cref="Poll"/> で per-frame の event 列を <see cref="InputBus"/> に流し込む。
/// テスト用に <see cref="FakeInputSource"/> がある。
/// </summary>
public interface IInputSource
{
    /// <summary>ソース名 (debug 表示)。</summary>
    string Name { get; }

    /// <summary>このフレームで発生した event を bus に enqueue する。</summary>
    void Poll(InputBus bus);
}
