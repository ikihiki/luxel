using Microsoft.Extensions.DependencyInjection;

namespace Luxel.Framework.Gallery;

public static class FrameworkGalleryProject
{
    public static IServiceCollection AddFrameworkGallery(this IServiceCollection services) => services.AddStoryCatalog(Register);
    public static void Register(StoryCatalogBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        Luxel.Gallery.Generated.StoryRegistration_Luxel_Framework_Gallery.Register(builder);
    }
    public static StoryCatalog CreateCatalog()
    {
        var builder = new StoryCatalogBuilder();
        Register(builder);
        return builder.Build();
    }
}
