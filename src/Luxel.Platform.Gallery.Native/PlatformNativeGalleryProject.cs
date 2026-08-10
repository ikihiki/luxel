using Microsoft.Extensions.DependencyInjection;

namespace Luxel.Platform.Gallery.Native;

public static class PlatformNativeGalleryProject
{
    public static IServiceCollection AddPlatformNativeGallery(this IServiceCollection services)
        => services.AddStoryCatalog(Register);

    public static void Register(StoryCatalogBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        global::Luxel.Platform.Gallery.PlatformGalleryProject.Register(builder);
        Luxel.Gallery.Generated.StoryRegistration_Luxel_Platform_Gallery_Native.Register(builder);
    }

    public static StoryCatalog CreateCatalog()
    {
        var builder = new StoryCatalogBuilder();
        Register(builder);
        return builder.Build();
    }
}
