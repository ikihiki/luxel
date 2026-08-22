using Luxel.UI;
using Luxel.Controls;
using Luxel.Gallery;

namespace Luxel.Tests;

public sealed class ControlStoryQualityTests
{
    private static readonly IReadOnlyDictionary<string, BasicRule> ComplexAuthoredBasics =
        new Dictionary<string, BasicRule>(StringComparer.Ordinal)
        {
            ["Controls/Rendering/Canvas2D/Basic"] = new("Canvas2D", ["draw: scene =>", "FillRoundedRect", "FillCircle"]),
            ["Controls/Collections/DataGrid/Basic"] = new("DataGrid", ["DataGridRow", "columns:", "onSelect:"]),
            ["Controls/Rendering/GpuView/Basic"] = new("GpuView", ["GpuViewRenderResult.Ready", "CopyColorToFramebuffer", "animated: false"]),
            ["Controls/Collections/GridView/Basic"] = new("GridView", ["GridViewItem", "Disabled: true", "onSelect:"]),
            ["Controls/Overlay/Popover/Basic"] = new("Popover", ["new Signal<bool>(true)", "anchor: () => new Rect", "Button("]),
            ["Controls/Collections/TabStrip/Basic"] = new("TabStrip", ["onCloseRequest:", "Badge: \"2\"", "Disabled: true"]),
            ["Controls/Rendering/ParticleView/Basic"] = new("ParticleView", ["new ParticleSystem", "ps.Emit(", "animated: true"]),
            ["Controls/Editor/EditorShell/Basic"] = new("EditorShell", ["new EditorSession", "new EditorTestFixture", "DockTree.Single"]),
            ["Controls/Editor/SceneInspector/Basic"] = new("SceneInspector", ["SelectEntity(1)", "SceneSchemas.BuiltIns()", "SceneInspector("]),
        };

    // These generated Basics are intentionally retained because their complete visual fixture is
    // expressible by the generator's supported scalar, Widget, or GridLength[] rules. Any new
    // generated Basic must be reviewed instead of inheriting a blanket exemption.
    private static readonly IReadOnlyDictionary<string, string> ReviewedGeneratedBasics =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Controls/Collections/DocumentTabs/Basic"] = "DocumentTabs",
            ["Controls/Editor/NodeGraphView/Basic"] = "NodeGraphView",
            ["Controls/Editor/PropertyGrid/Basic"] = "PropertyGrid",
            ["Controls/Editor/StatusBar/Basic"] = "StatusBar",
            ["Controls/Editor/TextEditorView/Basic"] = "TextEditorView",
            ["Controls/Layout/Box/Basic"] = "Box",
            ["Controls/Layout/Center/Basic"] = "Center",
            ["Controls/Layout/Grid/Basic"] = "Grid",
            ["Controls/Layout/Spacer/Basic"] = "Spacer",
            ["Controls/Layout/Stack/Basic"] = "Stack",
            ["Controls/Rendering/DiagramBlock/Basic"] = "DiagramBlock",
            ["Controls/Rendering/Icon/Basic"] = "Icon",
            ["Controls/Rendering/MathBlockView/Basic"] = "MathBlockView",
            ["Controls/Text/Text/Basic"] = "Text",
        };

    [Fact]
    public void Every_public_non_docs_control_story_has_a_japanese_description()
    {
        StoryInfo[] stories = CreateCombinedControlCatalog().All
            .Where(static story => story.Path.StartsWith("Controls/", StringComparison.Ordinal)
                && story.Kind != StoryKind.Docs)
            .ToArray();

        string[] missing = stories
            .Where(static story => !ContainsJapanese(story.ShortDescription))
            .Select(static story => story.Path)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(missing.Length == 0,
            $"Public Controls stories need Japanese ShortDescription metadata: {string.Join(", ", missing)}");
    }

    [Fact]
    public void Canonical_basic_and_playground_paths_keep_japanese_descriptions_and_basic_has_no_args()
    {
        int descriptorCount = 0;
        foreach ((StoryCatalog catalog, IReadOnlyList<GeneratedComponentStoryDescriptor> descriptors) in OwnerCatalogs())
        {
            foreach (GeneratedComponentStoryDescriptor descriptor in descriptors.Where(static item => item.IsUserFacing))
            {
                descriptorCount++;
                StoryInfo basic = Assert.IsType<StoryInfo>(catalog.Find(descriptor.BasicPath));
                StoryInfo playground = Assert.IsType<StoryInfo>(catalog.Find(descriptor.PlaygroundPath));

                Assert.Equal(descriptor.BasicPath, basic.Path);
                Assert.Equal(descriptor.PlaygroundPath, playground.Path);
                Assert.Equal(StoryKind.Basic, basic.Kind);
                Assert.Equal(StoryKind.Playground, playground.Kind);
                Assert.Empty(basic.ArgDefinitions ?? []);
                Assert.True(ContainsJapanese(basic.ShortDescription), descriptor.BasicPath);
                Assert.True(ContainsJapanese(basic.LongDescription), descriptor.BasicPath);
                Assert.True(ContainsJapanese(playground.ShortDescription), descriptor.PlaygroundPath);
                Assert.True(ContainsJapanese(playground.LongDescription), descriptor.PlaygroundPath);
            }
        }

        Assert.Equal(62, descriptorCount);
    }

    [Fact]
    public void All_62_canonical_basics_are_substantive_by_explicit_story_rules()
    {
        var basics = new List<(GeneratedComponentStoryDescriptor Descriptor, StoryInfo Story)>();
        foreach ((StoryCatalog catalog, IReadOnlyList<GeneratedComponentStoryDescriptor> descriptors) in OwnerCatalogs())
            foreach (GeneratedComponentStoryDescriptor descriptor in descriptors.Where(static item => item.IsUserFacing))
                basics.Add((descriptor, Assert.IsType<StoryInfo>(catalog.Find(descriptor.BasicPath))));

        Assert.Equal(62, basics.Count);
        Assert.Equal(ComplexAuthoredBasics.Keys.Order(StringComparer.Ordinal),
            basics.Where(item => ComplexAuthoredBasics.ContainsKey(item.Story.Path))
                .Select(static item => item.Story.Path).Order(StringComparer.Ordinal));

        string[] generatedPaths = basics
            .Where(static item => item.Story.RegistrationKind == StoryRegistrationKind.GeneratedComponentFallback)
            .Select(static item => item.Story.Path)
            .Order(StringComparer.Ordinal)
            .ToArray();
        string[] reviewedGeneratedPaths = ReviewedGeneratedBasics.Keys.Order(StringComparer.Ordinal).ToArray();
        Assert.True(reviewedGeneratedPaths.SequenceEqual(generatedPaths, StringComparer.Ordinal),
            $"Reviewed generated Basics changed. Expected: {string.Join(", ", reviewedGeneratedPaths)}; actual: {string.Join(", ", generatedPaths)}");

        foreach ((GeneratedComponentStoryDescriptor descriptor, StoryInfo basic) in basics)
        {
            Assert.Equal(descriptor.BasicPath, basic.Path);
            Assert.Equal(descriptor, basic.ProductionComponent);
            Assert.Equal(StoryKind.Basic, basic.Kind);
            Assert.Empty(basic.ArgDefinitions ?? []);
            Assert.Null(basic.CapabilityNote);
            Assert.True(ContainsJapanese(basic.ShortDescription), basic.Path);
            Assert.True(ContainsJapanese(basic.LongDescription), basic.Path);

            string source = Assert.IsType<string>(basic.Source);
            Assert.DoesNotContain(nameof(StoryCapabilityFallback), source, StringComparison.Ordinal);

            if (ComplexAuthoredBasics.TryGetValue(basic.Path, out BasicRule? complex))
            {
                Assert.Equal(complex.ControlName, descriptor.ControlName);
                Assert.Equal(StoryRegistrationKind.Authored, basic.RegistrationKind);
                Assert.Contains("ArgsEnabled = false", source, StringComparison.Ordinal);
                Assert.All(complex.RequiredSourceFragments,
                    fragment => Assert.Contains(fragment, source, StringComparison.Ordinal));
            }
            else if (ReviewedGeneratedBasics.TryGetValue(basic.Path, out string? generatedControl))
            {
                Assert.Equal(generatedControl, descriptor.ControlName);
                Assert.Equal(StoryRegistrationKind.GeneratedComponentFallback, basic.RegistrationKind);
                Assert.Contains("生成済み基本例", source, StringComparison.Ordinal);
            }
            else
            {
                Assert.Equal(StoryRegistrationKind.Authored, basic.RegistrationKind);
            }

            if (basic.Path == "Controls/Rendering/ImageBlock/Basic")
            {
                Assert.Contains("ImagePayload", source, StringComparison.Ordinal);
                Assert.Contains("ctx.Resources", source, StringComparison.Ordinal);
                continue;
            }

            using var context = new StoryContext();
            StoryResult result = basic.Build(context);
            Assert.Equal(StoryResultKind.Widget, result.Kind);
            Widget widget = Assert.IsAssignableFrom<Widget>(result.Widget);
            Assert.IsNotType<StoryCapabilityFallback>(widget);
            if (widget is EditorTestFixture fixture) fixture.Session.Dispose();
        }
    }

    [Fact]
    public void Controls_capability_notes_are_reserved_for_real_runtime_warnings()
    {
        StoryInfo[] misuse = CreateCombinedControlCatalog().All
            .Where(static story => story.Path.StartsWith("Controls/", StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(story.CapabilityNote))
            .OrderBy(static story => story.Path, StringComparer.Ordinal)
            .ToArray();

        Assert.True(misuse.Length == 0,
            $"Controls descriptions must use ShortDescription/LongDescription, not CapabilityNote: {string.Join(", ", misuse.Select(static story => story.Path))}");
    }

    private static IEnumerable<(StoryCatalog Catalog, IReadOnlyList<GeneratedComponentStoryDescriptor> Descriptors)> OwnerCatalogs()
    {
        yield return (global::Luxel.UI.Gallery.UiGalleryProject.CreateCatalog(),
            global::Luxel.UI.Gallery.UiGalleryProject.ProductionComponents);
        yield return (global::Luxel.Editor.Gallery.EditorGalleryProject.CreateCatalog(),
            global::Luxel.Editor.Gallery.EditorGalleryProject.ProductionComponents);
        yield return (global::Luxel.Particles.Gallery.ParticlesGalleryProject.CreateCatalog(),
            global::Luxel.Particles.Gallery.ParticlesGalleryProject.ProductionComponents);
    }

    private static StoryCatalog CreateCombinedControlCatalog()
    {
        var builder = new StoryCatalogBuilder();
        foreach ((StoryCatalog catalog, _) in OwnerCatalogs())
            foreach (StoryInfo story in catalog.All)
                builder.Add(story, replaceGenerated: true);
        return builder.Build();
    }

    private static bool ContainsJapanese(string? value)
        => !string.IsNullOrWhiteSpace(value) && value.Any(static character =>
            character is >= '\u3040' and <= '\u30ff' or >= '\u3400' and <= '\u9fff');

    private sealed record BasicRule(string ControlName, string[] RequiredSourceFragments);
}
