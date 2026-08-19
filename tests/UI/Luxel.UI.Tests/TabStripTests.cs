using Luxel.Controls;
using Luxel.UI;
using static Luxel.Controls.Kit;

namespace Luxel.Tests;

public sealed class TabStripTests
{
    [Fact]
    public void DuplicateKeysAreRejected()
        => Assert.Throws<ArgumentException>(() => Luxel.Controls.TabStrip.ValidateItems([new("a", "A"), new("a", "Again")]));

    [Fact]
    public void KeyboardNavigationSkipsDisabledAndRetainsFocus()
    {
        TabStrip tabs = TabStrip(items: [new("a", "A"), new("b", "B", Disabled: true), new("c", "C")], selectedKey: "a");
        Assert.Equal("c", tabs.MoveFocus(Key.Right));
        Assert.Equal("a", tabs.MoveFocus(Key.Right));
        Assert.Equal("c", tabs.MoveFocus(Key.End));
        Assert.Equal("a", tabs.MoveFocus(Key.Home));
    }

    [Fact]
    public void ItemContractCarriesMarkerBadgeTooltipAndCloseState()
    {
        var dirty = new Signal<bool>(true);
        var item = new TabStripItem("a", "A", dirty, "3", "tooltip", Closable: false);
        Assert.True(item.Marker!.Value);
        Assert.Equal("3", item.Badge);
        Assert.Equal("tooltip", item.Tooltip);
        Assert.False(item.Closable);
    }
}
