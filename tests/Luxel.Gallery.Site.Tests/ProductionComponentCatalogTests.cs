using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using Luxel.Gallery;
using Luxel.Gallery.Stories;
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
            Assert.NotNull(basic.ArgDefinitions);
            StoryResult basicResult = basic.BuildResult(new StoryContext());
            Assert.Equal(StoryResultKind.Widget, basicResult.Kind);
            Assert.True(basicResult.Widget is GeneratedComponentStoryPreview or StoryCapabilityFallback,
                $"{descriptor.BasicPath} returned {basicResult.Widget?.GetType().FullName ?? "null"}.");
        }
    }

    [Fact]
    public void Full_catalog_composes_resource_routes_without_leaking_them_into_CoreUi_runtime_catalog()
    {
        string[] resourcePaths =
        [
            .. ResourceCourseCatalog.Routes,
            "Examples/Resources/HelloTextAsset",
            "Examples/Resources/CustomPackageSource",
            "Examples/Resources/PlayerStatsPipeline",
            "Examples/Resources/ExtensionSelection",
            "Examples/Resources/SharedDependencyGraph",
            "Examples/Resources/ScopedRuntimeValues",
            "Examples/Resources/HotReloadRecovery",
            "Examples/Resources/BrowserHttpAssets",
            "Examples/Resources/Assets/DocumentInspector",
            "Examples/Resources/Assets/MeshPrimitiveInspector",
            "Examples/Resources/Assets/MaterialTextureInspector",
            "Examples/Resources/Assets/AnimatedSceneGraph",
            "Examples/Resources/Assets/GpuAssetRegistry",
            "Examples/Resources/Assets/ShaderBufferInspector",
            "Examples/Resources/Gltf/BoxDocumentLoad",
            "Examples/Resources/Gltf/ExternalBufferTrace",
            "Examples/Resources/Gltf/MalformedAccessorDiagnostics",
            "Examples/Resources/Gltf/BoxScene",
            "Examples/Resources/Gltf/AnimatedBox",
            "Examples/Resources/Gltf/RiggedSimpleSkinning",
            "Examples/Resources/Gltf/MorphWeights",
            "Examples/Resources/Gltf/ExternalDependencyReload",
        ];

        StoryCatalog resourceCatalog = ResourceStoryProject.CreateCatalog();
        StoryCatalog fullCatalog = GalleryStoryProject.CreateCatalog();
        StoryCatalog coreUiCatalog = CoreUiStoryProject.CreateCatalog();
        HashSet<string> browserPaths = coreUiCatalog.All
            .Select(story => story.Path)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal(resourcePaths.Order(StringComparer.Ordinal), resourceCatalog.All.Select(story => story.Path).Order(StringComparer.Ordinal));
        Assert.All(resourcePaths, path => Assert.NotNull(fullCatalog.Find(path)));
        Assert.All(resourcePaths, path => Assert.Null(coreUiCatalog.Find(path)));
        Assert.All(resourcePaths, path => Assert.DoesNotContain(path, browserPaths));
        Assert.DoesNotContain(
            typeof(CoreUiStoryProject).Assembly.GetReferencedAssemblies(),
            reference => reference.Name == "Luxel.Assets.Gltf");
    }

    [Fact]
    public void Every_resource_learn_page_embeds_its_explicit_canonical_examples()
    {
        StoryCatalog catalog = ResourceStoryProject.CreateCatalog();

        Assert.Equal(ResourceCourseCatalog.Routes.Order(StringComparer.Ordinal),
            ResourceLearnExamples.Routes.Keys.Order(StringComparer.Ordinal));

        foreach (string learnRoute in ResourceCourseCatalog.Routes)
        {
            StoryInfo page = Assert.IsType<StoryInfo>(catalog.Find(learnRoute));
            StoryResult result = page.BuildResult(new StoryContext());
            string[] expected = ResourceLearnExamples.Routes[learnRoute];

            Assert.True(expected.Length >= 2, $"{learnRoute} must embed multiple examples.");
            Assert.Equal(expected, result.References.Select(reference => reference.Path));
            Assert.True(result.Markdown.IndexOf("```luxel-story\n0\n```", StringComparison.Ordinal)
                < result.Markdown.IndexOf("## More runnable examples", StringComparison.Ordinal),
                $"{learnRoute} must place its primary example inside the explanation.");
            Assert.All(expected, example => Assert.NotNull(catalog.Find(example)));
        }
    }

    [Fact]
    public void Resource_examples_publish_concept_focused_source_for_inline_docs()
    {
        StoryCatalog catalog = ResourceStoryProject.CreateCatalog();
        StoryInfo hello = Assert.IsType<StoryInfo>(catalog.Find("Examples/Resources/HelloTextAsset"));
        StoryInfo reload = Assert.IsType<StoryInfo>(catalog.Find("Examples/Resources/HotReloadRecovery"));

        Assert.Contains("ResourceHandle<TextAsset>", hello.Source, StringComparison.Ordinal);
        Assert.Contains("await text.Ready", hello.Source, StringComparison.Ordinal);
        Assert.DoesNotContain("ResourceScenarios.Create", hello.Source, StringComparison.Ordinal);
        Assert.Contains("last good value", reload.Source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("PumpAsync", reload.Source, StringComparison.Ordinal);

        string[] examples = ResourceLearnExamples.Routes.Values.SelectMany(routes => routes)
            .Distinct(StringComparer.Ordinal).ToArray();
        Assert.All(examples, route =>
        {
            StoryInfo story = Assert.IsType<StoryInfo>(catalog.Find(route));
            Assert.False(string.IsNullOrWhiteSpace(story.Source), $"{route} needs concise instructional source.");
            Assert.DoesNotContain("ResourceScenarios.Create", story.Source, StringComparison.Ordinal);
            Assert.DoesNotContain("GltfStoryAssets.View", story.Source, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Every_canonical_resource_example_builds_a_widget_without_host_resources()
    {
        StoryCatalog catalog = ResourceStoryProject.CreateCatalog();
        string[] examples = ResourceLearnExamples.Routes.Values
            .SelectMany(routes => routes)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(22, examples.Length);
        foreach (string route in examples)
        {
            StoryInfo story = Assert.IsType<StoryInfo>(catalog.Find(route));
            using var context = new StoryContext();
            StoryResult result = story.BuildResult(context);
            Assert.Equal(StoryResultKind.Widget, result.Kind);
            Assert.NotNull(result.Widget);
        }
    }

    [Fact]
    public async Task Gpu_resource_examples_construct_private_systems_and_load_embedded_fixtures()
    {
        StoryCatalog catalog = ResourceStoryProject.CreateCatalog();
        string[] gpuExamples =
        [
            "Examples/Resources/Gltf/BoxScene",
            "Examples/Resources/Gltf/AnimatedBox",
            "Examples/Resources/Gltf/RiggedSimpleSkinning",
            "Examples/Resources/Gltf/MorphWeights",
        ];
        int before = GltfStoryAssets.CreatedSystemCount;

        foreach (string route in gpuExamples)
        {
            using var context = new StoryContext();
            StoryResult result = Assert.IsType<StoryInfo>(catalog.Find(route)).BuildResult(context);
            Assert.Equal(StoryResultKind.Widget, result.Kind);
        }

        Assert.Equal(before + gpuExamples.Length, GltfStoryAssets.CreatedSystemCount);
        Assert.Single((await GltfStoryAssets.LoadFixtureForTestAsync(GltfStoryAssets.Box)).Meshes);
        Assert.NotEmpty((await GltfStoryAssets.LoadFixtureForTestAsync(GltfStoryAssets.AnimatedBox)).Animations);
        Assert.NotEmpty((await GltfStoryAssets.LoadFixtureForTestAsync(GltfStoryAssets.RiggedSimple)).Skins);
    }

    [Fact]
    public async Task Cpu_resource_examples_execute_with_isolated_systems_and_deterministic_results()
    {
        StoryCatalog catalog = ResourceStoryProject.CreateCatalog();
        string[] gpuExamples =
        [
            "Examples/Resources/Gltf/BoxScene",
            "Examples/Resources/Gltf/AnimatedBox",
            "Examples/Resources/Gltf/RiggedSimpleSkinning",
            "Examples/Resources/Gltf/MorphWeights",
        ];
        string[] cpuExamples = ResourceLearnExamples.Routes.Values
            .SelectMany(routes => routes)
            .Distinct(StringComparer.Ordinal)
            .Except(gpuExamples, StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var systems = new HashSet<Luxel.Resources.ResourceSystem>(ReferenceEqualityComparer.Instance);

        Assert.Equal(18, cpuExamples.Length);
        foreach (string route in cpuExamples)
        {
            StoryInfo story = Assert.IsType<StoryInfo>(catalog.Find(route));
            using var context = new StoryContext();
            StoryResult result = story.BuildResult(context);
            ResourceScenarioWidget widget = Assert.IsType<ResourceScenarioWidget>(result.Widget);
            Assert.True(systems.Add(widget.Resources), $"{route} reused another scenario's ResourceSystem.");

            await widget.RunForTestAsync().WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Equal("Ready", widget.Status);
            Assert.DoesNotContain("unexpectedly imported", widget.Detail, StringComparison.OrdinalIgnoreCase);
            Assert.False(string.IsNullOrWhiteSpace(widget.Detail));
            widget.Dispose();
        }
    }

    [Fact]
    public void Removed_resource_routes_have_no_aliases()
    {
        StoryCatalog catalog = ResourceStoryProject.CreateCatalog();
        string[] removed =
        [
            "Learn/Resources/Assets/TypesAndRelationships",
            "Learn/Resources/Assets/ShaderCalculations",
            "Learn/Resources/Assets/GltfRuntime",
            "Examples/Resources/Pipeline",
            "Examples/Resources/DependencyDag",
            "Examples/Resources/Reload",
            "Examples/Resources/Lifetime",
            "Examples/3D/GltfBox",
            "Examples/3D/GltfAnimated",
            "Examples/3D/GltfSkinned",
            "Examples/3D/GltfMorph",
        ];

        Assert.All(removed, route => Assert.Null(catalog.Find(route)));
    }

    [Fact]
    public void AddResourceStory_registers_the_resource_catalog_in_service_registration_order()
    {
        var services = new ServiceCollection();
        services.AddStoryCatalog(builder => builder.Add(new StoryInfo("Before/Resource", 1, 1, null, _ => null!)));
        services.AddResourceStory();
        services.AddStoryCatalog(builder => builder.Add(new StoryInfo("After/Resource", 1, 1, null, _ => null!)));
        using ServiceProvider provider = services.BuildServiceProvider();

        StoryCatalog catalog = provider.GetRequiredService<StoryCatalog>();
        string[] resourcePaths = ResourceStoryProject.CreateCatalog().All.Select(story => story.Path).ToArray();

        Assert.Equal("Before/Resource", catalog.All[0].Path);
        Assert.Equal(resourcePaths, catalog.All.Skip(1).Take(resourcePaths.Length).Select(story => story.Path));
        Assert.Equal("After/Resource", catalog.All[^1].Path);
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
        Assert.Same(generated.ArgDefinitions, actual.ArgDefinitions);
        Assert.Equal("authored Button Basic", actual.Source);

        var duplicateBuilder = new StoryCatalogBuilder();
        duplicateBuilder.Add(authored);
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => duplicateBuilder.Add(authored with { Source = "another project" }, replaceGenerated: true));
        Assert.Contains("only replace an exact generated component fallback", error.Message, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Luxel.slnx"))) return current.FullName;
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("Luxel repository root was not found.");
    }
}
