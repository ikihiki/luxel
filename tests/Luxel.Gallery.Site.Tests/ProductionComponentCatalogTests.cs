using System.Text.Json;
using Luxel.Gallery;
using Luxel.UI;

namespace Luxel.Gallery.Site.Tests;

public sealed class ProductionComponentCatalogTests
{
    private static readonly JsonSerializerOptions CamelCase = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    [Fact]
    public void Production_inventory_has_exact_semantic_overview_and_runtime_basic_pairs()
    {
        StoryCatalog catalog = CoreUiStoryProject.CreateCatalog();
        IReadOnlyList<GeneratedComponentStoryDescriptor> descriptors = CoreUiStoryProject.ProductionComponents;

        Assert.Equal(60, CoreUiStoryProject.ProductionComponentCount);
        Assert.Equal(60, descriptors.Count);
        Assert.Equal(60, descriptors.Select(descriptor => descriptor.ComponentType).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(60, descriptors.Select(descriptor => descriptor.Category).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(120, descriptors.SelectMany(descriptor => new[] { descriptor.OverviewPath, descriptor.BasicPath })
            .Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(120, catalog.All.Count(story => story.ProductionComponent is not null));

        foreach (GeneratedComponentStoryDescriptor descriptor in descriptors)
        {
            StoryInfo overview = Assert.IsType<StoryInfo>(catalog.Find(descriptor.OverviewPath));
            Assert.Equal(descriptor, overview.ProductionComponent);
            Assert.Equal(StoryRegistrationKind.GeneratedComponentFallback, overview.RegistrationKind);
            StoryResult overviewResult = overview.BuildResult(new StoryContext());
            Assert.Equal(StoryResultKind.Markdown, overviewResult.Kind);
            Assert.StartsWith("# " + descriptor.Category + "\n", overviewResult.Markdown, StringComparison.Ordinal);
            StoryReference reference = Assert.Single(overviewResult.References);
            Assert.Equal(descriptor.BasicPath, reference.Path);

            StoryInfo basic = Assert.IsType<StoryInfo>(catalog.Find(descriptor.BasicPath));
            Assert.Equal(descriptor, basic.ProductionComponent);
            Assert.Equal(StoryRegistrationKind.GeneratedComponentFallback, basic.RegistrationKind);
            Assert.Equal(CoreUiStoryProject.RuntimeBundleId, basic.RuntimeBundleId);
            Assert.NotNull(basic.ArgDefinitions);
            StoryResult basicResult = basic.BuildResult(new StoryContext());
            Assert.Equal(StoryResultKind.Widget, basicResult.Kind);
            Assert.True(basicResult.Widget is GeneratedComponentStoryPreview or StoryCapabilityFallback,
                $"{descriptor.BasicPath} returned {basicResult.Widget?.GetType().FullName ?? "null"}.");
        }
    }

    [Fact]
    public void Exact_authored_override_replaces_only_generated_fallback_and_preserves_canonical_metadata()
    {
        StoryInfo generated = Assert.IsType<StoryInfo>(CoreUiStoryProject.CreateCatalog().Find("Controls/Button/Basic"));
        var builder = new StoryCatalogBuilder();
        builder.Add(generated);
        var authored = new StoryInfo(generated.Path, generated.Width, generated.Height, generated.Theme,
            _ => new StoryCapabilityFallback("Button", "Authored exact-path implementation."),
            Source: "authored Button Basic");

        builder.Add(authored, replaceGenerated: true);
        StoryInfo actual = Assert.IsType<StoryInfo>(builder.Build().Find(generated.Path));

        Assert.Equal(StoryRegistrationKind.Authored, actual.RegistrationKind);
        Assert.Equal(generated.ProductionComponent, actual.ProductionComponent);
        Assert.Equal(generated.RuntimeBundleId, actual.RuntimeBundleId);
        Assert.Same(generated.ArgDefinitions, actual.ArgDefinitions);
        Assert.Equal("authored Button Basic", actual.Source);

        var duplicateBuilder = new StoryCatalogBuilder();
        duplicateBuilder.Add(authored);
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => duplicateBuilder.Add(authored with { Source = "another project" }, replaceGenerated: true));
        Assert.Contains("only replace an exact generated component fallback", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Protocol_v2_runtime_manifest_matches_the_CoreUi_catalog_and_identifies_60_production_basics()
    {
        string root = GallerySiteExporter.FindRepositoryRoot();
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            root, "samples", "LuxelWebGpuBrowser", "wwwroot", "browser-runtime-manifest.json")));
        JsonElement manifest = document.RootElement;
        Assert.Equal(CoreUiStoryProject.RuntimeBundleId, manifest.GetProperty("bundleId").GetString());
        Assert.Equal(2, manifest.GetProperty("protocolVersion").GetInt32());

        Dictionary<string, JsonElement> runtimeByPath = manifest.GetProperty("stories").EnumerateArray()
            .ToDictionary(story => story.GetProperty("path").GetString()!, story => story.Clone(), StringComparer.Ordinal);
        StoryCatalog catalog = CoreUiStoryProject.CreateCatalog();
        foreach (StoryInfo story in CoreUiStoryProject.RuntimeStories(catalog))
        {
            JsonElement runtime = runtimeByPath[story.Path];
            Assert.Equal(story.Width, runtime.GetProperty("width").GetInt32());
            Assert.Equal(story.Height, runtime.GetProperty("height").GetInt32());
            Assert.Equal(
                JsonSerializer.Serialize(story.ArgDefinitions ?? Array.Empty<StoryArgDefinition>(), CamelCase),
                JsonSerializer.Serialize(runtime.GetProperty("args"), CamelCase));
            Assert.Equal(story.CapabilityNote, runtime.GetProperty("capabilityNote").ValueKind == JsonValueKind.Null
                ? null : runtime.GetProperty("capabilityNote").GetString());
            Assert.Equal(story.ProductionComponent?.ComponentType,
                runtime.GetProperty("componentType").ValueKind == JsonValueKind.Null
                    ? null : runtime.GetProperty("componentType").GetString());
        }

        JsonElement[] production = runtimeByPath.Values
            .Where(story => story.GetProperty("componentType").ValueKind == JsonValueKind.String)
            .ToArray();
        Assert.Equal(60, production.Length);
        Assert.All(production, story => Assert.EndsWith("/Basic", story.GetProperty("path").GetString(), StringComparison.Ordinal));
    }
}
