using Luxel.UI;
using Xunit;
using Luxel.Gallery;

namespace Luxel.Tests;

/// <summary>StoryはOrder属性ではなく登録順で列挙される。</summary>
public class StoryOrderTests
{
    private static StoryInfo S(string path) => new(path, _ => null!);

    [Fact]
    public void All_PreservesRegistrationOrder_AndReplacementPosition()
    {
        StoryRegistry.Register(S("OrdTestB/Second"));
        StoryRegistry.Register(S("OrdTestB/First"));
        StoryRegistry.Register(S("OrdTestA/Late"));
        StoryRegistry.Register(S("OrdTestB/Second"));

        IReadOnlyList<StoryInfo> all = StoryRegistry.All;
        int second = IndexOf(all, "OrdTestB/Second");
        int first = IndexOf(all, "OrdTestB/First");
        int late = IndexOf(all, "OrdTestA/Late");

        Assert.True(second < first);
        Assert.True(first < late);
    }

    [Fact]
    public void CatalogBuilder_PreservesRegistrationOrder()
    {
        StoryCatalog catalog = new StoryCatalogBuilder()
            .Add(S("Order/Z"))
            .Add(S("Order/A"))
            .Add(S("Order/M"))
            .Build();

        Assert.Equal(["Order/Z", "Order/A", "Order/M"], catalog.All.Select(story => story.Path));
    }

    [Fact]
    public void Presentation_order_uses_learning_route_and_places_shallow_paths_first()
    {
        StoryInfo[] stories =
        [
            S("Examples/Deep/Demo"),
            S("Internals/Architecture"),
            S("Tutorials/3DApp/Overview"),
            S("Controls/Button"),
            S("Start/Welcome"),
            S("Tutorials/Overview"),
            S("Reference/Luxel.UI"),
            S("Learn/Graphics/Overview"),
            S("Examples/Overview"),
        ];

        Assert.Equal(
        [
            "Start/Welcome",
            "Tutorials/Overview",
            "Tutorials/3DApp/Overview",
            "Learn/Graphics/Overview",
            "Controls/Button",
            "Examples/Overview",
            "Examples/Deep/Demo",
            "Reference/Luxel.UI",
            "Internals/Architecture",
        ], StoryPresentationOrder.Apply(stories).Select(story => story.Path));
    }

    private static int IndexOf(IReadOnlyList<StoryInfo> all, string path)
    {
        for (int i = 0; i < all.Count; i++) if (all[i].Path == path) return i;
        return -1;
    }
}
