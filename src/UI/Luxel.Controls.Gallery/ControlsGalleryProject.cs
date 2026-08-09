using Microsoft.Extensions.DependencyInjection;

namespace Luxel.Controls.Gallery;

/// <summary>Owns the browser-safe authored control and UI pattern stories.</summary>
public static class ControlsGalleryProject
{
    public static IServiceCollection AddControlsGallery(this IServiceCollection services)
        => services.AddStoryCatalog(Register);

    public static void Register(StoryCatalogBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        Luxel.Gallery.Generated.StoryRegistration_Luxel_Controls_Gallery.Register(builder);
        Luxel.Gallery.Generated.ComponentStoryRegistration_Luxel_Controls_Gallery.Register(builder);
    }

    public static StoryCatalog CreateCatalog()
    {
        var builder = new StoryCatalogBuilder();
        Register(builder);
        return builder.Build();
    }
}
