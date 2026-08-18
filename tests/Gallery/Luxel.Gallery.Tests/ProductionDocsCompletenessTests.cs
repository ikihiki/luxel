using Luxel.Gallery;
using Luxel.UI;

namespace Luxel.Tests;

public sealed class ProductionDocsCompletenessTests
{
    private static readonly HashSet<string> Categories =
        ["Layout", "Input", "Text", "Collections", "Overlay", "Rendering", "Editor"];

    [Fact]
    public void Production_inventory_and_canonical_paths_are_unique_across_owners()
    {
        GeneratedComponentStoryDescriptor[] ui = [.. global::Luxel.UI.Gallery.UiGalleryProject.ProductionComponents];
        GeneratedComponentStoryDescriptor[] editor = [.. global::Luxel.Editor.Gallery.EditorGalleryProject.ProductionComponents];
        GeneratedComponentStoryDescriptor[] particles = [.. global::Luxel.Particles.Gallery.ParticlesGalleryProject.ProductionComponents];
        GeneratedComponentStoryDescriptor[] all = [.. ui, .. editor, .. particles];

        Assert.Equal(53, ui.Length);
        Assert.Equal(6, editor.Length);
        Assert.Single(particles);
        Assert.Equal(60, all.Length);
        Assert.Equal(57, all.Count(static descriptor => descriptor.IsUserFacing));
        Assert.Equal(all.Length, all.Select(static descriptor => descriptor.ComponentType).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(all.Length * 2, all.SelectMany(static descriptor => new[] { descriptor.DocsPath, descriptor.BasicPath })
            .Distinct(StringComparer.Ordinal).Count());

        foreach (GeneratedComponentStoryDescriptor descriptor in all.Where(static descriptor => descriptor.IsUserFacing))
        {
            Assert.Contains(descriptor.Category, Categories);
            Assert.StartsWith($"Controls/{descriptor.Category}/{descriptor.ControlName}/", descriptor.DocsPath, StringComparison.Ordinal);
            Assert.EndsWith("/Docs", descriptor.DocsPath, StringComparison.Ordinal);
            Assert.EndsWith("/Basic", descriptor.BasicPath, StringComparison.Ordinal);
        }
        Assert.All(all.Where(static descriptor => !descriptor.IsUserFacing), descriptor =>
            Assert.StartsWith("Gallery/Infrastructure/", descriptor.DocsPath, StringComparison.Ordinal));
    }

    [Fact]
    public void All_control_stories_use_the_fixed_category_taxonomy()
    {
        StoryInfo[] stories =
        [
            .. global::Luxel.UI.Gallery.UiGalleryProject.CreateCatalog().All,
            .. global::Luxel.Editor.Gallery.EditorGalleryProject.CreateCatalog().All,
            .. global::Luxel.Particles.Gallery.ParticlesGalleryProject.CreateCatalog().All,
        ];

        string[] invalid = stories.Where(static story => story.Path.StartsWith("Controls/", StringComparison.Ordinal))
            .Select(static story => story.Path)
            .Where(path =>
            {
                string[] segments = path.Split('/');
                return segments.Length < 4 || !Categories.Contains(segments[1]);
            })
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.True(invalid.Length == 0, $"Noncanonical control stories: {string.Join(", ", invalid)}");
    }

    [Fact]
    public void Each_owner_catalog_has_one_docs_and_basic_story_with_matching_ownership()
    {
        VerifyOwner(global::Luxel.UI.Gallery.UiGalleryProject.CreateCatalog(),
            global::Luxel.UI.Gallery.UiGalleryProject.ProductionComponents,
            global::Luxel.UI.Gallery.UiGalleryProject.Ownership);
        VerifyOwner(global::Luxel.Editor.Gallery.EditorGalleryProject.CreateCatalog(),
            global::Luxel.Editor.Gallery.EditorGalleryProject.ProductionComponents,
            global::Luxel.Editor.Gallery.EditorGalleryProject.Ownership);
        VerifyOwner(global::Luxel.Particles.Gallery.ParticlesGalleryProject.CreateCatalog(),
            global::Luxel.Particles.Gallery.ParticlesGalleryProject.ProductionComponents,
            global::Luxel.Particles.Gallery.ParticlesGalleryProject.Ownership);
    }

    [Fact]
    public void Button_basic_explicitly_disables_args_while_playground_keeps_them()
    {
        StoryCatalog catalog = global::Luxel.UI.Gallery.UiGalleryProject.CreateCatalog();

        StoryInfo basic = Assert.IsType<StoryInfo>(catalog.Find("Controls/Input/Button/Basic"));
        Assert.Empty(Assert.IsAssignableFrom<IReadOnlyList<StoryArgDefinition>>(basic.ArgDefinitions));

        StoryInfo playground = Assert.IsType<StoryInfo>(catalog.Find("Controls/Input/Button/Playground"));
        Assert.NotEmpty(Assert.IsAssignableFrom<IReadOnlyList<StoryArgDefinition>>(playground.ArgDefinitions));
    }

    [Fact]
    public void Legacy_control_paths_are_aliases_and_not_sidebar_entries()
    {
        StoryCatalog ui = global::Luxel.UI.Gallery.UiGalleryProject.CreateCatalog();
        Assert.DoesNotContain(ui.All, story => story.Path == "Controls/Button/Basic");
        Assert.Equal("Controls/Input/Button/Basic", ui.Find("Controls/Button/Basic")?.Path);
        Assert.Equal("Controls/Input/CheckBox/Basic", ui.Find("Controls/Check/Basic")?.Path);
        Assert.Equal("Controls/Collections/ScrollViewer/Basic", ui.Find("Controls/Scroll/Basic")?.Path);

        StoryCatalog editor = global::Luxel.Editor.Gallery.EditorGalleryProject.CreateCatalog();
        Assert.DoesNotContain(editor.All, story => story.Path == "Editor/Controls/AssetBrowser/Basic");
        Assert.Equal("Controls/Collections/AssetBrowser/Basic", editor.Find("Controls/AssetBrowserBasic")?.Path);
        Assert.Equal("Controls/Collections/AssetBrowser/Basic", editor.Find("Editor/Controls/AssetBrowser/Basic")?.Path);
        Assert.Equal("Controls/Editor/CommandPalette/Docs", editor.Find("Controls/CommandPalette/Docs")?.Path);
    }

    [Fact]
    public void Xml_doc_resolver_is_case_sensitive_and_preserves_fallbacks()
    {
        Assert.Equal("fallback", GalleryXmlDocText.Resolve("xml:T:Missing", "fallback"));
        Assert.Equal(string.Empty, GalleryXmlDocText.Resolve("xml:T:Missing", string.Empty));
        Assert.Equal("fallback", GalleryXmlDocText.Resolve("XML:T:Missing", "fallback"));
    }

    [Fact]
    public void Control_api_registry_uses_fully_qualified_identity_and_rejects_ambiguous_short_names()
    {
        var left = new ControlApi("Probe.Left", "Collision", "left", []);
        var right = new ControlApi("Probe.Right", "Collision", "right", []);
        ControlApiRegistry.Register(left);
        ControlApiRegistry.Register(right);

        Assert.Same(left, ControlApiRegistry.Find("Probe.Left.Collision"));
        Assert.Same(right, ControlApiRegistry.Find("Probe.Right.Collision"));
        Assert.Null(ControlApiRegistry.Find("Collision"));
    }

    [Fact]
    public void Localized_control_api_registration_wins_regardless_of_initializer_order()
    {
        var localized = new ControlApi("Probe.Priority", "Localized", "日本語", []);
        var raw = new ControlApi("Probe.Priority", "Localized", "English", []);

        ControlApiRegistry.RegisterLocalized(localized);
        ControlApiRegistry.Register(raw);

        Assert.Same(localized, ControlApiRegistry.Find("Probe.Priority.Localized"));
    }

    private static void VerifyOwner(StoryCatalog catalog,
        IReadOnlyList<GeneratedComponentStoryDescriptor> descriptors, StoryOwnership ownership)
    {
        foreach (GeneratedComponentStoryDescriptor descriptor in descriptors)
        {
            StoryInfo docs = Assert.Single(catalog.All, story => story.Path == descriptor.DocsPath);
            StoryInfo basic = Assert.Single(catalog.All, story => story.Path == descriptor.BasicPath);
            Assert.Equal(descriptor, docs.ProductionComponent);
            Assert.Equal(descriptor, basic.ProductionComponent);
            Assert.Equal(ownership, docs.Ownership);
            Assert.Equal(ownership, basic.Ownership);
        }
    }
}
