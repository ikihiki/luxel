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

    [Theory]
    [InlineData("Controls/Button/Docs", StoryKind.Docs)]
    [InlineData("Controls/Button/Basic", StoryKind.Basic)]
    [InlineData("Controls/Button/Playground", StoryKind.Playground)]
    [InlineData("Controls/Button/Examples/Utilities", StoryKind.Example)]
    [InlineData("Controls/Button/States/Disabled", StoryKind.State)]
    [InlineData("Controls/Button/Accessibility/Keyboard", StoryKind.AccessibilityFixture)]
    [InlineData("Controls/Button/Test/Stress", StoryKind.TestFixture)]
    [InlineData("Learn/Framework/Overview", StoryKind.Unspecified)]
    public void CatalogBuilder_InfersCanonicalStoryKind(string path, StoryKind expected)
    {
        StoryInfo story = Assert.Single(new StoryCatalogBuilder().Add(S(path)).Build().All);
        Assert.Equal(expected, story.Kind);
    }

    [Fact]
    public void StoryRegistry_InfersCanonicalStoryKind()
    {
        const string path = "Controls/KindInferenceProbe/States/Selected";
        StoryRegistry.Register(S(path));

        Assert.Equal(StoryKind.State, Assert.Single(StoryRegistry.All, story => story.Path == path).Kind);
    }

    [Fact]
    public void CatalogBuilder_PreservesExplicitStoryKind()
    {
        StoryInfo story = Assert.Single(new StoryCatalogBuilder()
            .Add(S("Controls/Button/Examples/Recipe") with { Kind = StoryKind.TestFixture })
            .Build().All);

        Assert.Equal(StoryKind.TestFixture, story.Kind);
    }

    [Fact]
    public void UiCatalog_HasOneCanonicalDocsAndBasicEntryPerProductionComponent()
    {
        StoryCatalog catalog = global::Luxel.UI.Gallery.UiGalleryProject.CreateCatalog();

        Assert.DoesNotContain(catalog.All, story => story.Path.StartsWith("Controls/", StringComparison.Ordinal)
            && story.Path.EndsWith("/Overview", StringComparison.Ordinal));
        string[] invalidControlPaths = catalog.All
            .Where(story => story.Path.StartsWith("Controls/", StringComparison.Ordinal))
            .Select(story => story.Path)
            .Where(path =>
            {
                string[] segments = path.Split('/');
                return segments.Length < 3 || segments[2] is not ("Docs" or "Basic" or "Playground" or "Examples" or "States" or "Accessibility" or "Test");
            })
            .ToArray();
        Assert.True(invalidControlPaths.Length == 0,
            $"Noncanonical control story paths: {string.Join(", ", invalidControlPaths)}");

        foreach (GeneratedComponentStoryDescriptor component in global::Luxel.UI.Gallery.UiGalleryProject.ProductionComponents)
        {
            Assert.Single(catalog.All, story => story.Path == component.DocsPath);
            Assert.Single(catalog.All, story => story.Path == component.BasicPath);
            Assert.Equal(StoryKind.Docs, catalog.Find(component.DocsPath)!.Kind);
            Assert.Equal(StoryKind.Basic, catalog.Find(component.BasicPath)!.Kind);
        }
    }

    [Fact]
    public void KitDocsLinksUseCanonicalComponentFirstPaths()
    {
        StoryCatalog catalog = global::Luxel.UI.Gallery.UiGalleryProject.CreateCatalog();
        StoryInfo docs = Assert.IsType<StoryInfo>(catalog.Find("Controls/Kit/Docs"));
        using var context = new StoryContext();

        StoryResult result = docs.Build(context);

        Assert.Contains("story:Controls/Kit/Examples/Badges", result.Markdown, StringComparison.Ordinal);
        Assert.Contains("story:Controls/Kit/Examples/Alert", result.Markdown, StringComparison.Ordinal);
        Assert.Contains("story:Controls/Kit/Examples/Typography", result.Markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("story:Controls/Badges", result.Markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("story:Controls/AlertStory", result.Markdown, StringComparison.Ordinal);
    }

    private static int IndexOf(IReadOnlyList<StoryInfo> all, string path)
    {
        for (int i = 0; i < all.Count; i++) if (all[i].Path == path) return i;
        return -1;
    }
}
