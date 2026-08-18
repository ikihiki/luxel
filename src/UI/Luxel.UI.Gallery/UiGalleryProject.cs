using Microsoft.Extensions.DependencyInjection;

namespace Luxel.UI.Gallery;

/// <summary>Owns browser-safe UI stories and generated production component fallbacks.</summary>
public static class UiGalleryProject
{
    public static StoryOwnership Ownership { get; } = StoryOwnership.BrowserSafe("UI", "UI.Base");

    private static readonly Lazy<GeneratedComponentStoryDescriptor[]> ProductionLazy = new(CreateProduction);

    public static int ProductionComponentCount => ProductionComponents.Count;
    public static IReadOnlyList<GeneratedComponentStoryDescriptor> ProductionComponents => ProductionLazy.Value;

    public static IServiceCollection AddUiGallery(this IServiceCollection services)
        => services.AddStoryCatalog(Register);

    public static void Register(StoryCatalogBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        using IDisposable ownership = builder.BeginOwnership(Ownership);
        var categoryBuilder = new StoryCatalogBuilder();
        RegisterProductionComponents(categoryBuilder);
        UiControlDocs.Register(categoryBuilder, ProductionComponents);
        // Keep library-level guides and compatibility categories after exact component-identity Docs.
        global::Luxel.Gallery.Stories.ControlDocsApi.RegisterControlStories(categoryBuilder);
        Merge(Luxel.Gallery.Generated.StoryRegistration_Luxel_UI_Gallery.Register);
        Merge(Luxel.Gallery.Generated.ComponentStoryRegistration_Luxel_UI_Gallery.Register);

        StoryCatalog catalog = categoryBuilder.Build();
        foreach (StoryInfo story in catalog.All)
            builder.Add(story, replaceGenerated: true);
        ControlStoryAliases.Add(builder, catalog, ProductionComponents);
        ControlStoryAliases.AddIfHidden(builder, catalog, "Controls/Layout/Docs", "Controls/Layout/Layout/Docs");
        ControlStoryAliases.AddIfHidden(builder, catalog, "Controls/Kit/Docs", "Controls/Layout/Kit/Docs");

        void Merge(Action<StoryCatalogBuilder> register)
        {
            var authoredBuilder = new StoryCatalogBuilder();
            register(authoredBuilder);
            foreach (StoryInfo story in authoredBuilder.Build().All)
                categoryBuilder.Add(story, replaceGenerated: true);
        }
    }

    public static StoryCatalog CreateCatalog()
    {
        var builder = new StoryCatalogBuilder();
        Register(builder);
        return builder.Build();
    }

    public static StoryCatalog CreateProductionCatalog()
    {
        var builder = new StoryCatalogBuilder();
        RegisterProductionComponents(builder);
        return builder.Build();
    }

    private static GeneratedComponentStoryDescriptor[] CreateProduction() =>
    [
        .. Luxel.Gallery.Generated.ComponentStoryRegistration_Luxel_Controls.Descriptors,
        .. Luxel.Gallery.Generated.ComponentStoryRegistration_Luxel_Diagram.Descriptors,
        .. Luxel.Gallery.Generated.ComponentStoryRegistration_Luxel_MathText.Descriptors,
        .. Luxel.Gallery.Generated.ComponentStoryRegistration_Luxel_Gallery_UI.Descriptors,
    ];

    private static void RegisterProductionComponents(StoryCatalogBuilder builder)
    {
        ValidateProductionDescriptors();
        Luxel.Gallery.Generated.ComponentStoryRegistration_Luxel_Controls.Register(builder);
        Luxel.Gallery.Generated.ComponentStoryRegistration_Luxel_Diagram.Register(builder);
        Luxel.Gallery.Generated.ComponentStoryRegistration_Luxel_MathText.Register(builder);
        Luxel.Gallery.Generated.ComponentStoryRegistration_Luxel_Gallery_UI.Register(builder);
    }

    private static void ValidateProductionDescriptors()
    {
        GeneratedComponentStoryDescriptor[] descriptors = ProductionLazy.Value;
        if (descriptors.Select(descriptor => descriptor.ComponentType).Distinct(StringComparer.Ordinal).Count() != descriptors.Length)
            throw new InvalidOperationException("Generated UI production component types must be unique.");
        int expectedPaths = descriptors.Sum(static descriptor => descriptor.IsUserFacing ? 3 : 2);
        if (descriptors.SelectMany(static descriptor => descriptor.IsUserFacing
                    ? new[] { descriptor.DocsPath, descriptor.BasicPath, descriptor.PlaygroundPath }
                    : new[] { descriptor.DocsPath, descriptor.BasicPath })
                .Distinct(StringComparer.Ordinal).Count() != expectedPaths)
            throw new InvalidOperationException("Generated UI production component paths must be unique.");
    }
}
