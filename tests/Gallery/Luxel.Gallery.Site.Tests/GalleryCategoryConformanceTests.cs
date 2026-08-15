using Luxel.Animation.Gallery;
using Luxel.Audio.Gallery;
using Luxel.DevTools.Gallery;
using Luxel.Editor.Gallery;
using Luxel.Editor.Gallery.Native;
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
using Luxel.Scripting.Gallery.Native;
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

    private static readonly (string Category, Func<StoryCatalog> CreateBase, Func<StoryCatalog> CreateNative)[] NativeCategories =
    [
        ("Editor", EditorGalleryProject.CreateCatalog, EditorNativeGalleryProject.CreateCatalog),
        ("Platform", PlatformGalleryProject.CreateCatalog, PlatformNativeGalleryProject.CreateCatalog),
        ("Scripting", ScriptingGalleryProject.CreateCatalog, ScriptingNativeGalleryProject.CreateCatalog),
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
            });
        }
    }

    [Fact]
    public void Compatibility_aggregate_contains_the_standalone_category_union()
    {
        StoryCatalog aggregate = GalleryStoryProject.CreateCatalog();
        var nativeCategoryNames = NativeCategories.Select(category => category.Category).ToHashSet(StringComparer.Ordinal);
        string[] expectedRoutes = BrowserSafeCategories
            .Where(category => !nativeCategoryNames.Contains(category.Category))
            .SelectMany(category => category.CreateCatalog().All.Select(story => story.Path))
            .Concat(NativeCategories.SelectMany(category => category.CreateNative().All.Select(story => story.Path)))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        string[] actualRoutes = aggregate.All.Select(story => story.Path).Order(StringComparer.Ordinal).ToArray();

        Assert.Equal(expectedRoutes, actualRoutes);
        Assert.All(expectedRoutes, route => Assert.NotNull(aggregate.Find(route)));
    }

    [Fact]
    public void Story_references_resolve_from_the_composed_catalog()
    {
        StoryCatalog catalog = GalleryStoryProject.CreateCatalog();
        string repositoryRoot = FindRepositoryRoot();
        var referencedRoutes = new HashSet<string>(StringComparer.Ordinal);
        var referencePattern = new Regex("(?:StoryReference\\.To\\(\\\"|story:)([A-Za-z0-9_./-]+)", RegexOptions.CultureInvariant);

        foreach (StoryInfo story in catalog.All)
        {
            foreach (Match match in referencePattern.Matches(story.Source ?? string.Empty))
                referencedRoutes.Add(match.Groups[1].Value);

        }

        Assert.All(referencedRoutes.Where(route => !route.StartsWith("RealWindow/", StringComparison.Ordinal)),
            route => Assert.NotNull(catalog.Find(route)));
    }

    private static string FindRepositoryRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Luxel.slnx"))) return directory.FullName;
        throw new DirectoryNotFoundException("Could not locate the Luxel repository root.");
    }

    [Fact]
    public void Native_catalogs_are_strict_extensions_of_their_browser_bases()
    {
        foreach ((string category, Func<StoryCatalog> createBase, Func<StoryCatalog> createNative) in NativeCategories)
        {
            StoryCatalog browser = createBase();
            StoryCatalog native = createNative();
            string[] browserRoutes = browser.All.Select(story => story.Path).Order(StringComparer.Ordinal).ToArray();
            string[] nativeRoutes = native.All.Select(story => story.Path).Order(StringComparer.Ordinal).ToArray();
            string[] nativeOnlyRoutes = nativeRoutes.Except(browserRoutes, StringComparer.Ordinal).ToArray();

            Assert.NotEmpty(nativeOnlyRoutes);
            Assert.All(browserRoutes, route => Assert.Contains(route, nativeRoutes));
            Assert.Equal(nativeRoutes.Length, nativeRoutes.Distinct(StringComparer.Ordinal).Count());
            Assert.All(nativeOnlyRoutes, route =>
            {
                Assert.Null(browser.Find(route));
                StoryInfo story = Assert.IsType<StoryInfo>(native.Find(route));
                Assert.Equal(category, story.Ownership?.Category);
                Assert.Equal(GalleryCompatibility.NativeOnly, story.Ownership?.Compatibility);
            });
        }
    }

    [Fact]
    public void Desktop_only_routes_are_absent_from_browser_catalogs_and_owned_by_native_extensions()
    {
        AssertNativeOnly(EditorGalleryProject.CreateCatalog(), EditorNativeGalleryProject.CreateCatalog(),
            "Apps/Studio/Shell",
            "Learn/Production/StudioToPlayer",
            "Learn/Production/Ship",
            "Learn/Production/Workbench");
        AssertNativeOnly(ScriptingGalleryProject.CreateCatalog(), ScriptingNativeGalleryProject.CreateCatalog(),
            "Examples/Scripting/NativeHotReload",
            "Examples/Scripting/Repl");

        StoryCatalog scriptingBrowser = ScriptingGalleryProject.CreateCatalog();
        Assert.NotNull(scriptingBrowser.Find("Learn/Scripting/ScriptingOverview"));
        Assert.NotNull(scriptingBrowser.Find("Learn/Scripting/ScriptingReload"));
        Assert.NotNull(scriptingBrowser.Find("Examples/Scripting/LiveCsx"));
        Assert.NotNull(scriptingBrowser.Find("Examples/Scripting/HotReload"));
        Assert.NotNull(scriptingBrowser.Find("Examples/Scripting/Notebook"));
        Assert.NotNull(scriptingBrowser.Find("Examples/Scripting/Playground"));

        static void AssertNativeOnly(StoryCatalog browser, StoryCatalog native, params string[] routes)
        {
            foreach (string route in routes)
            {
                Assert.Null(browser.Find(route));
                StoryInfo story = Assert.IsType<StoryInfo>(native.Find(route));
                Assert.Equal(GalleryCompatibility.NativeOnly, story.Ownership?.Compatibility);
            }
        }
    }
}
