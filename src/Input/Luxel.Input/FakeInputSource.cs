namespace Luxel.Input;

/// <summary>
/// テスト/シミュレーション用の入力ソース。<see cref="PressKey"/> 等を呼んで event を予約し、
/// 次の <see cref="Poll"/> でまとめて bus に流す。
///
/// <code>
/// var fake = new FakeInputSource();
/// fake.PressKey(KeyCode.W);
/// fake.SetAxis(AxisCode.GamepadLeftStickX, 0.7f);
/// bus.Clear();
/// fake.Poll(bus);
/// // bus.Events に 2 件並ぶ
/// </code>
/// </summary>
public sealed class FakeInputSource : IInputSource
{
    public string Name => "Fake";

    private readonly List<InputEvent> _pending = new();
    private readonly HashSet<KeyCode> _held = new();
    private readonly Dictionary<AxisCode, float> _axisValues = new();

    public IReadOnlyCollection<KeyCode> HeldKeys => _held;

    public void PressKey(KeyCode key)
    {
        if (_held.Add(key))
            _pending.Add(new InputEvent(InputEventKind.KeyDown, key, AxisCode.None, 1f, 0f, 0));
    }

    public void ReleaseKey(KeyCode key)
    {
        if (_held.Remove(key))
            _pending.Add(new InputEvent(InputEventKind.KeyUp, key, AxisCode.None, 0f, 0f, 0));
    }

    /// <summary>PressKey + ReleaseKey を同フレームに (瞬間押下シミュレート)。</summary>
    public void TapKey(KeyCode key)
    {
        PressKey(key);
        ReleaseKey(key);
    }

    public void SetAxis(AxisCode axis, float value)
    {
        _axisValues[axis] = value;
        _pending.Add(new InputEvent(InputEventKind.AxisChanged, KeyCode.None, axis, value, 0f, 0));
    }

    public void MovePointer(float x, float y)
        => _pending.Add(new InputEvent(InputEventKind.PointerMoved, KeyCode.None, AxisCode.None, x, y, 0));

    public void Poll(InputBus bus)
    {
        foreach (var e in _pending) bus.Enqueue(e);
        _pending.Clear();
    }
}
