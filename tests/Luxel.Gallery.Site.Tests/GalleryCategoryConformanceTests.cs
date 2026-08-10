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

using System.Text.RegularExpressions;
namespace Luxel.Gallery.Site.Tests;

public sealed class GalleryCategoryConformanceTests
{
    private static readonly (string Category, StoryOwnership Ownership, Func<StoryCatalog> CreateCatalog)[] BrowserSafeCategories =
    [
        ("Resources", ResourceGalleryProject.Ownership, ResourceGalleryProject.CreateCatalog),
        ("Audio", AudioGalleryProject.Ownership, AudioGalleryProject.CreateCatalog),
        ("UI", UiGalleryProject.Ownership, UiGalleryProject.CreateCatalog),
        ("Graphics", GraphicsGalleryProject.Ownership, GraphicsGalleryProject.CreateCatalog),
        ("Input", InputGalleryProject.Ownership, InputGalleryProject.CreateCatalog),
        ("Framework", FrameworkGalleryProject.Ownership, FrameworkGalleryProject.CreateCatalog),
        ("Animation", AnimationGalleryProject.Ownership, AnimationGalleryProject.CreateCatalog),
        ("Particles", ParticlesGalleryProject.Ownership, ParticlesGalleryProject.CreateCatalog),
        ("Scripting", ScriptingGalleryProject.Ownership, ScriptingGalleryProject.CreateCatalog),
        ("Editor", EditorGalleryProject.Ownership, EditorGalleryProject.CreateCatalog),
        ("DevTools", DevToolsGalleryProject.Ownership, DevToolsGalleryProject.CreateCatalog),
        ("GamesSamples", GamesSamplesGalleryProject.Ownership, GamesSamplesGalleryProject.CreateCatalog),
        ("GalleryDocs", GalleryDocsProject.Ownership, GalleryDocsProject.CreateCatalog),
        ("Platform", PlatformGalleryProject.Ownership, PlatformGalleryProject.CreateCatalog),
    ];

    [Fact]
    public void Browser_safe_category_catalogs_can_be_created_independently()
    {
        foreach ((string category, StoryOwnership ownership, Func<StoryCatalog> createCatalog) in BrowserSafeCategories)
        {
            StoryCatalog catalog = createCatalog();

            Assert.Equal(
                catalog.All.Count,
                catalog.All.Select(story => story.Path).Distinct(StringComparer.Ordinal).Count());
            Assert.All(catalog.All, story =>
            {
                Assert.Equal(ownership, story.Ownership);
                Assert.Equal(GalleryCompatibility.BrowserSafe, story.Ownership!.Compatibility);
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
    public void Story_references_and_sample_bundles_resolve_from_the_composed_catalog()
    {
        StoryCatalog catalog = GalleryStoryProject.CreateCatalog();
        string repositoryRoot = FindRepositoryRoot();
        var referencedRoutes = new HashSet<string>(StringComparer.Ordinal);
        var referencePattern = new Regex("(?:StoryReference\\.To\\(\\\"|story:)([A-Za-z0-9_./-]+)", RegexOptions.CultureInvariant);

        foreach (StoryInfo story in catalog.All)
        {
            foreach (Match match in referencePattern.Matches(story.Source ?? string.Empty))
                referencedRoutes.Add(match.Groups[1].Value);

            if (story.SampleBundle is not { } bundleId) continue;
            foreach (SampleBundleInfo bundle in SampleBundleMaterializer.DependencyClosure(bundleId))
            foreach (SampleFileInfo file in bundle.Files)
            {
                if (file.EffectiveMode == SampleFileMode.Generated) continue;
                string source = Path.Combine(repositoryRoot, file.Path);
                Assert.True(File.Exists(source) || Directory.Exists(source),
                    $"Sample source is missing: {bundle.Id} -> {file.Path}");
                if (file.Region is not null)
                    global::Luxel.Gallery.DocKit.DocsKit.ExtractRegion(File.ReadAllText(source), file.Path, file.Region);
            }
        }

        Assert.All(referencedRoutes, route => Assert.NotNull(catalog.Find(route)));
    }

    private static string FindRepositoryRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Luxel.slnx"))) return directory.FullName;
        throw new DirectoryNotFoundException("Could not locate the Luxel repository root.");
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
