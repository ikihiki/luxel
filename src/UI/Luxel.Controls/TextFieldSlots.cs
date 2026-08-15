using Luxel.UI;
using Luxel.UI.Styling;

namespace Luxel.Controls;

public enum TextFieldSlotKey { Leading, Trailing }

/// <summary>TextField の文字入力領域の前後に widget を埋め込む slot。</summary>
public static class TextFieldSlot
{
    public static ISlotPart Leading(Func<Widget> template)
        => new SlotPart<TextFieldSlotKey>(TextFieldSlotKey.Leading, template);

    public static ISlotPart Trailing(Func<Widget> template)
        => new SlotPart<TextFieldSlotKey>(TextFieldSlotKey.Trailing, template);
}
