using Microsoft.Extensions.DependencyInjection;
using Luxel.UI;

namespace Luxel.Gallery;

/// <summary>Browser-safe control stories and all generated production component Overview/Basic pairs.</summary>
public static class CoreUiStoryProject
{
    public const string BrowserBundleId = "webgpu-browser-v1";
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

    /// <summary>WASM-safeなCoreUi StoryをGeneric Hostのservice collectionへ追加する。</summary>
    public static IServiceCollection AddCoreUiStory(this IServiceCollection services)
        => services.AddStoryCatalog(Register);

    public static bool IsProductionCanonicalPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return ProductionCanonicalPaths.Contains(path);
    }

    public static void Register(StoryCatalogBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ValidateProductionDescriptors();

        // Auto-generated canonical ownership. These registrars are emitted into this Gallery leaf from
        // Gallery-neutral component metadata and invoke direct typed factories without reflection.
        Luxel.Gallery.Generated.ComponentStoryRegistration_Luxel_Controls.Register(builder);
        Luxel.Gallery.Generated.ComponentStoryRegistration_Luxel_Diagram.Register(builder);
        Luxel.Gallery.Generated.ComponentStoryRegistration_Luxel_MathText.Register(builder);
        Luxel.Gallery.Generated.ComponentStoryRegistration_Luxel_Particles_UI.Register(builder);
        Luxel.Gallery.Generated.ComponentStoryRegistration_Luxel_Gallery_UI.Register(builder);

        // Handwritten browser-safe implementation/pattern stories and authored component playgrounds.
        // Generated canonical Overview/Basic paths remain the production fallback unless the composition
        // root explicitly calls StoryCatalogBuilder.Add(story, replaceGenerated: true).
        var authoredBuilder = new StoryCatalogBuilder();
        Luxel.Gallery.Generated.StoryRegistration_Luxel_Gallery_Stories_CoreUi.Register(authoredBuilder);
        foreach (StoryInfo story in authoredBuilder.Build().All)
        {
            if (ProductionCanonicalPaths.Contains(story.Path)) continue;
            // Membership in this catalog is the only browser-execution boundary.
            builder.Add(story);
        }
    }

    public static StoryCatalog CreateCatalog()
    {
        var builder = new StoryCatalogBuilder();
        Register(builder);
        return builder.Build();
    }

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
