namespace Luxel.UI.Styling;

/// <summary>
/// Slot 上書きの宣言。<c>Slider()[SliderSlot.Knob(() => Circle())]</c> のように
/// control の indexer 経由で渡し、内部の primitives (knob/track 等) を上書きする。
/// </summary>
public interface ISlotPart
{
    void ApplyTo(Widget widget);
}

/// <summary>Slot を受け取る能力を持つ widget の marker interface。</summary>
public interface ISlotted<TKey>
{
    void SetSlot(TKey key, Func<Widget> template);
}

/// <summary><see cref="ISlotPart"/> の汎用実装。typed key で widget の slot dictionary に書き込む。</summary>
public sealed class SlotPart<TKey>(TKey key, Func<Widget> template) : ISlotPart
{
    public TKey Key { get; } = key;
    public Func<Widget> Template { get; } = template;

    public void ApplyTo(Widget widget)
    {
        ArgumentNullException.ThrowIfNull(widget);
        if (widget is not ISlotted<TKey> slotted)
            throw new InvalidOperationException(
                $"Slot key type '{typeof(TKey).Name}' cannot be applied to widget '{widget.GetType().Name}'.");
        slotted.SetSlot(Key, Template);
    }
}
