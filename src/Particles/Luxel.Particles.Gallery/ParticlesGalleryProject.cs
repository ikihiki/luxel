using Microsoft.Extensions.DependencyInjection;

namespace Luxel.Particles.Gallery;

public static class ParticlesGalleryProject
{
    public static StoryOwnership Ownership { get; } = StoryOwnership.BrowserSafe("Particles", "Particles.Base");

    public static int ProductionComponentCount => ProductionComponents.Count;
    public static IReadOnlyList<GeneratedComponentStoryDescriptor> ProductionComponents
        => Luxel.Gallery.Generated.ComponentStoryRegistration_Luxel_Particles_UI.Descriptors;

    public static IServiceCollection AddParticlesGallery(this IServiceCollection services) => services.AddStoryCatalog(Register);

    public static void Register(StoryCatalogBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        using IDisposable ownership = builder.BeginOwnership(Ownership);
        var categoryBuilder = new StoryCatalogBuilder();
        Luxel.Gallery.Generated.ComponentStoryRegistration_Luxel_Particles_UI.Register(categoryBuilder);
        ParticleViewDocs.Register(categoryBuilder, ProductionComponents);

        var authoredBuilder = new StoryCatalogBuilder();
        Luxel.Gallery.Generated.StoryRegistration_Luxel_Particles_Gallery.Register(authoredBuilder);
        foreach (StoryInfo story in authoredBuilder.Build().All)
            categoryBuilder.Add(story, replaceGenerated: true);

        foreach (StoryInfo story in categoryBuilder.Build().All)
            builder.Add(story, replaceGenerated: true);
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
        Luxel.Gallery.Generated.ComponentStoryRegistration_Luxel_Particles_UI.Register(builder);
        return builder.Build();
    }
}
