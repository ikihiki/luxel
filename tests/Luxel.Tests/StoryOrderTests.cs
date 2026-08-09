using Luxel.UI;
using Xunit;
using Luxel.Gallery;

namespace Luxel.Tests;

/// <summary>StoryはOrder属性ではなく登録順で列挙される。</summary>
public class StoryOrderTests
{
    private static StoryInfo S(string path, int order) => new(path, 1, 1, null, _ => null!, order);

    [Fact]
    public void All_PreservesRegistrationOrder_AndReplacementPosition()
    {
        StoryRegistry.Register(S("OrdTestB/Second", 2));
        StoryRegistry.Register(S("OrdTestB/First", 1));
        StoryRegistry.Register(S("OrdTestA/Late", 50));
        StoryRegistry.Register(S("OrdTestB/Second", 999));

        IReadOnlyList<StoryInfo> all = StoryRegistry.All;
        int second = IndexOf(all, "OrdTestB/Second");
        int first = IndexOf(all, "OrdTestB/First");
        int late = IndexOf(all, "OrdTestA/Late");

        Assert.True(second < first);
        Assert.True(first < late);
        Assert.Equal(999, all[second].Order);
    }

    [Fact]
    public void CatalogBuilder_PreservesRegistrationOrder()
    {
        StoryCatalog catalog = new StoryCatalogBuilder()
            .Add(S("Order/Z", 0))
            .Add(S("Order/A", -100))
            .Add(S("Order/M", 1000))
            .Build();

        Assert.Equal(["Order/Z", "Order/A", "Order/M"], catalog.All.Select(story => story.Path));
    }

    private static int IndexOf(IReadOnlyList<StoryInfo> all, string path)
    {
        for (int i = 0; i < all.Count; i++) if (all[i].Path == path) return i;
        return -1;
    }
}
