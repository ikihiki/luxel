using Microsoft.Extensions.DependencyInjection;

namespace Luxel.Platform.Gallery;

public static class PlatformGalleryProject
{
    public static IServiceCollection AddPlatformGallery(this IServiceCollection services)
        => services.AddStoryCatalog(Register);

    public static void Register(StoryCatalogBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        // No browser-safe Platform stories are authored yet; keep the category catalog boundary explicit.
    }

    public static StoryCatalog CreateCatalog()
    {
        var builder = new StoryCatalogBuilder();
        Register(builder);
        return builder.Build();
    }
}
