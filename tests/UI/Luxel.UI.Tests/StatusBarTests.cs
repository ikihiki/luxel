using Luxel.Controls;
using Luxel.UI;

namespace Luxel.Tests;

public sealed class StatusBarTests
{
    [Fact]
    public void CollapseRemovesLowestPriorityItemsAndHonorsVisibility()
    {
        StatusBarItem[] items =
        [
            new("important", new Spacer(), Priority: 100, PreferredWidth: 80),
            new("optional", new Spacer(), Priority: 0, PreferredWidth: 80),
            new("hidden", new Spacer(), Visible: false, PreferredWidth: 80),
        ];
        IReadOnlyList<StatusBarItem> result = StatusBar.Collapse(items, 100);
        Assert.Equal(["important"], result.Select(x => x.Key));
    }

    [Fact]
    public void StableKeysAndRegionsArePreserved()
    {
        var item = new StatusBarItem("center", new Spacer(), StatusBarRegion.Center, Separator: true);
        Assert.Equal("center", item.Key);
        Assert.Equal(StatusBarRegion.Center, item.Region);
        Assert.True(item.Separator);
    }
}
