namespace Luxel.Input;

/// <summary>
/// 1 フレームぶんの物理入力イベントを集める queue。<see cref="IInputSource.Poll"/> で enqueue、
/// アクション層 (INPUT-M2) の binding が Drain で dequeue する。
/// </summary>
public sealed class InputBus
{
    private readonly List<InputEvent> _events = new();

    /// <summary>このフレームで累積された event (読み取り専用)。</summary>
    public IReadOnlyList<InputEvent> Events => _events;

    public void Enqueue(InputEvent e) => _events.Add(e);

    /// <summary>KeyDown/KeyUp の shortcut。</summary>
    public void EnqueueKey(KeyCode key, bool down, long timestampTicks = 0)
        => _events.Add(new InputEvent(
            down ? InputEventKind.KeyDown : InputEventKind.KeyUp,
            key, AxisCode.None, down ? 1f : 0f, 0f, timestampTicks));

    public void EnqueueAxis(AxisCode axis, float value, long timestampTicks = 0)
        => _events.Add(new InputEvent(InputEventKind.AxisChanged, KeyCode.None, axis, value, 0f, timestampTicks));

    public void EnqueuePointer(float x, float y, long timestampTicks = 0)
        => _events.Add(new InputEvent(InputEventKind.PointerMoved, KeyCode.None, AxisCode.None, x, y, timestampTicks));

    /// <summary>次のフレーム開始時に呼ぶ ─ 累積 event をクリア。</summary>
    public void Clear() => _events.Clear();
}
