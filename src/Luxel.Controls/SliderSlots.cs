using Luxel.UI;
using Luxel.UI.Styling;

namespace Luxel.Controls;

/// <summary>Slider の slot キー (内部 primitives の上書き先)。</summary>
public enum SliderSlotKey { Track, Knob }

/// <summary>Slider の slot 上書きファクトリ: <c>Slider(...)[SliderSlot.Knob(() => Border(...))]</c>。</summary>
public static class SliderSlot
{
    public static ISlotPart Track(Func<Widget> template) => new SlotPart<SliderSlotKey>(SliderSlotKey.Track, template);
    public static ISlotPart Knob(Func<Widget> template) => new SlotPart<SliderSlotKey>(SliderSlotKey.Knob, template);
}
