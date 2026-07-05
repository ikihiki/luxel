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

/// <summary>
/// 物理入力の生成源。プラットフォーム固有 (Win32/XInput) は本 interface を実装し、
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
