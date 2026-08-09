using Microsoft.Extensions.DependencyInjection;

namespace Luxel.DevTools.Gallery;

public static class DevToolsGalleryProject
{
    public static IServiceCollection AddDevToolsGallery(this IServiceCollection services)
        => services.AddStoryCatalog(Register);

    public static void Register(StoryCatalogBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        // No DevTools-specific authored stories remain in the central projects.
    }

    public static StoryCatalog CreateCatalog()
    {
        var builder = new StoryCatalogBuilder();
        Register(builder);
        return builder.Build();
    }
}
