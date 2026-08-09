using Microsoft.Extensions.DependencyInjection;

namespace Luxel.Particles.Gallery;

public static class ParticlesGalleryProject
{
    public static IServiceCollection AddParticlesGallery(this IServiceCollection services) => services.AddStoryCatalog(Register);
    public static void Register(StoryCatalogBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        Luxel.Gallery.Generated.StoryRegistration_Luxel_Particles_Gallery.Register(builder);
    }
    public static StoryCatalog CreateCatalog()
    {
        var builder = new StoryCatalogBuilder();
        Register(builder);
        return builder.Build();
    }
}
