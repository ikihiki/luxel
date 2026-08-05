using Luxel.UI;

namespace Luxel.Gallery;

/// <summary>Browser-safe control stories and all generated production component Overview/Basic pairs.</summary>
public static class CoreUiStoryProject
{
    public const string RuntimeBundleId = "webgpu-browser-v1";
    public const int ProductionComponentCount =
        Luxel.Gallery.Generated.ComponentStoryRegistration_Luxel_Controls.ComponentCount
        + Luxel.Gallery.Generated.ComponentStoryRegistration_Luxel_Diagram.ComponentCount
        + Luxel.Gallery.Generated.ComponentStoryRegistration_Luxel_MathText.ComponentCount
        + Luxel.Gallery.Generated.ComponentStoryRegistration_Luxel_Particles_UI.ComponentCount
        + Luxel.Gallery.Generated.ComponentStoryRegistration_Luxel_Gallery_UI.ComponentCount;

    // Keep generated descriptor access lazy. browser-WASM invokes this catalog explicitly; eager
    // type initialization hides useful schema/registrar errors behind TypeInitializationException.
    private static readonly Lazy<GeneratedComponentStoryDescriptor[]> ProductionLazy = new(CreateProduction);
    private static readonly Lazy<HashSet<string>> ProductionCanonicalPathsLazy = new(() => Production
        .SelectMany(static descriptor => new[] { descriptor.OverviewPath, descriptor.BasicPath })
        .ToHashSet(StringComparer.Ordinal));
    private static GeneratedComponentStoryDescriptor[] Production => ProductionLazy.Value;
    private static HashSet<string> ProductionCanonicalPaths => ProductionCanonicalPathsLazy.Value;

    /// <summary>Authoritative source-generated production component inventory.</summary>
    public static IReadOnlyList<GeneratedComponentStoryDescriptor> ProductionComponents => Production;

    private static GeneratedComponentStoryDescriptor[] CreateProduction() =>
    [
        .. Luxel.Gallery.Generated.ComponentStoryRegistration_Luxel_Controls.Descriptors,
        .. Luxel.Gallery.Generated.ComponentStoryRegistration_Luxel_Diagram.Descriptors,
        .. Luxel.Gallery.Generated.ComponentStoryRegistration_Luxel_MathText.Descriptors,
        .. Luxel.Gallery.Generated.ComponentStoryRegistration_Luxel_Particles_UI.Descriptors,
        .. Luxel.Gallery.Generated.ComponentStoryRegistration_Luxel_Gallery_UI.Descriptors,
    ];

    public static bool IsProductionCanonicalPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return ProductionCanonicalPaths.Contains(path);
    }

    public static void Register(StoryCatalogBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ValidateProductionDescriptors();

        // Auto-generated canonical ownership. These registrars live with the component assemblies,
        // invoke direct typed factories, and expose static schemas without executing a story.
        Luxel.Gallery.Generated.ComponentStoryRegistration_Luxel_Controls.Register(builder, RuntimeBundleId);
        Luxel.Gallery.Generated.ComponentStoryRegistration_Luxel_Diagram.Register(builder, RuntimeBundleId);
        Luxel.Gallery.Generated.ComponentStoryRegistration_Luxel_MathText.Register(builder, RuntimeBundleId);
        Luxel.Gallery.Generated.ComponentStoryRegistration_Luxel_Particles_UI.Register(builder, RuntimeBundleId);
        Luxel.Gallery.Generated.ComponentStoryRegistration_Luxel_Gallery_UI.Register(builder, RuntimeBundleId);

        // Handwritten browser-safe implementation/pattern stories and authored component playgrounds.
        // Generated canonical Overview/Basic paths remain the production fallback unless the composition
        // root explicitly calls StoryCatalogBuilder.Add(story, replaceGenerated: true).
        var authoredBuilder = new StoryCatalogBuilder();
        Luxel.Gallery.Generated.StoryRegistration_Luxel_Gallery_Stories_CoreUi.Register(authoredBuilder);
        Luxel.Gallery.Generated.ComponentStoryRegistration_Luxel_Gallery_Stories_CoreUi.Register(authoredBuilder);
        foreach (StoryInfo story in authoredBuilder.Build().All)
        {
            if (ProductionCanonicalPaths.Contains(story.Path)) continue;
            // This assembly is the browser-safe story boundary. Any authored story added here is
            // automatically exported and executable by the WebAssembly runtime.
            builder.Add(story with { RuntimeBundleId = story.RuntimeBundleId ?? RuntimeBundleId });
        }
    }

    public static StoryCatalog CreateCatalog()
    {
        var builder = new StoryCatalogBuilder();
        Register(builder);
        return builder.Build();
    }

    public static IReadOnlyList<StoryInfo> RuntimeStories(StoryCatalog? catalog = null)
        => (catalog ?? CreateCatalog()).All
            .Where(story => story.RuntimeBundleId == RuntimeBundleId)
            .OrderBy(story => story.Path, StringComparer.Ordinal)
            .ToArray();

    private static void ValidateProductionDescriptors()
    {
        if (Production.Length != ProductionComponentCount)
            throw new InvalidOperationException(
                $"Generated production descriptor count mismatch: constants={ProductionComponentCount}, descriptors={Production.Length}.");
        if (Production.Select(descriptor => descriptor.ComponentType).Distinct(StringComparer.Ordinal).Count() != Production.Length)
            throw new InvalidOperationException("Generated production component types must be unique.");
        if (ProductionCanonicalPaths.Count != Production.Length * 2)
            throw new InvalidOperationException("Generated production Overview/Basic paths must be unique.");
    }
}
