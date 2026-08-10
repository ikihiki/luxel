using Luxel.Animation.Gallery;
using Luxel.Audio.Gallery;
using Luxel.DevTools.Gallery;
using Luxel.Editor.Gallery;
using Luxel.Framework.Gallery;
using Luxel.Gallery.Docs;
using Luxel.GamesSamples.Gallery;
using Luxel.Graphics.Gallery;
using Luxel.Input.Gallery;
using Luxel.Particles.Gallery;
using Luxel.Platform.Gallery;
using Luxel.Platform.Gallery.Native;
using Luxel.Resources.Gallery;
using Luxel.Scripting.Gallery;
using Luxel.UI.Gallery;

namespace Luxel.Gallery.Site.Tests;

public sealed class GalleryCategoryConformanceTests
{
    private static readonly (string Category, Func<StoryCatalog> CreateCatalog)[] BrowserSafeCategories =
    [
        ("Resources", ResourceGalleryProject.CreateCatalog),
        ("Audio", AudioGalleryProject.CreateCatalog),
        ("UI", UiGalleryProject.CreateCatalog),
        ("Graphics", GraphicsGalleryProject.CreateCatalog),
        ("Input", InputGalleryProject.CreateCatalog),
        ("Framework", FrameworkGalleryProject.CreateCatalog),
        ("Animation", AnimationGalleryProject.CreateCatalog),
        ("Particles", ParticlesGalleryProject.CreateCatalog),
        ("Scripting", ScriptingGalleryProject.CreateCatalog),
        ("Editor", EditorGalleryProject.CreateCatalog),
        ("DevTools", DevToolsGalleryProject.CreateCatalog),
        ("GamesSamples", GamesSamplesGalleryProject.CreateCatalog),
        ("GalleryDocs", GalleryDocsProject.CreateCatalog),
        ("Platform", PlatformGalleryProject.CreateCatalog),
    ];

    [Fact]
    public void Browser_safe_category_catalogs_can_be_created_independently()
    {
        foreach ((string category, Func<StoryCatalog> createCatalog) in BrowserSafeCategories)
        {
            StoryCatalog catalog = createCatalog();

            Assert.Equal(
                catalog.All.Count,
                catalog.All.Select(story => story.Path).Distinct(StringComparer.Ordinal).Count());
            Assert.All(catalog.All, story =>
            {
                Assert.False(string.IsNullOrWhiteSpace(story.Path), $"{category} contains an empty route.");
                Assert.False(story.Path.StartsWith('/') || story.Path.EndsWith('/'),
                    $"{category} contains a non-canonical route: {story.Path}");
                Assert.False(string.IsNullOrWhiteSpace(story.Source),
                    $"{category} route '{story.Path}' does not expose captured source.");
                if (story.SampleBundle is not null)
                {
                    Assert.IsType<SampleBundleInfo>(SampleBundleRegistry.Find(story.SampleBundle));
                }
            });
        }
    }

    [Fact]
    public void Compatibility_aggregate_contains_the_standalone_category_union()
    {
        StoryCatalog aggregate = GalleryStoryProject.CreateCatalog();
        string[] expectedRoutes = BrowserSafeCategories
            .Where(category => category.Category != "Platform")
            .SelectMany(category => category.CreateCatalog().All.Select(story => story.Path))
            .Concat(PlatformNativeGalleryProject.CreateCatalog().All.Select(story => story.Path))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        string[] actualRoutes = aggregate.All.Select(story => story.Path).Order(StringComparer.Ordinal).ToArray();

        Assert.Equal(expectedRoutes, actualRoutes);
        Assert.All(expectedRoutes, route => Assert.NotNull(aggregate.Find(route)));
    }

    [Fact]
    public void Platform_native_catalog_is_a_strict_extension_of_the_platform_base_catalog()
    {
        StoryCatalog browser = PlatformGalleryProject.CreateCatalog();
        StoryCatalog native = PlatformNativeGalleryProject.CreateCatalog();
        string[] browserRoutes = browser.All.Select(story => story.Path).Order(StringComparer.Ordinal).ToArray();
        string[] nativeRoutes = native.All.Select(story => story.Path).Order(StringComparer.Ordinal).ToArray();
        string[] nativeOnlyRoutes = nativeRoutes.Except(browserRoutes, StringComparer.Ordinal).ToArray();

        Assert.NotEmpty(nativeOnlyRoutes);
        Assert.All(browserRoutes, route => Assert.Contains(route, nativeRoutes));
        Assert.Equal(nativeRoutes.Length, nativeRoutes.Distinct(StringComparer.Ordinal).Count());
        Assert.All(nativeOnlyRoutes, route => Assert.Null(browser.Find(route)));
    }
}
