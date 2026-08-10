using Microsoft.Extensions.DependencyInjection;

namespace Luxel.Audio.Gallery;

/// <summary>Owns the browser-safe Audio learning pages and executable stories.</summary>
public static class AudioGalleryProject
{
    public static IServiceCollection AddAudioGallery(this IServiceCollection services)
        => services.AddStoryCatalog(Register);

    public static void Register(StoryCatalogBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        Luxel.Gallery.Generated.StoryRegistration_Luxel_Audio_Gallery.Register(builder);
    }

    public static StoryCatalog CreateCatalog()
    {
        var builder = new StoryCatalogBuilder();
        Register(builder);
        return builder.Build();
    }
}
